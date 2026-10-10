# PHASE-09c Help Center, SEO and Caching Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `TechStrap.Portal` its public help center on top of the 09a foundation and the 09b customer flows.
- **API:** the two small public lists the portal needs: a category's published articles, and the active products (key and display name).
- **Pages:** the knowledge base home, category, search and article pages, with the shared components (`KbArticleCard`, `KbBreadcrumbs`, `Pager`, `StateMessage`) and the words in `KbCopy`.
- **SEO and speed:** the SEO head and escaped JSON-LD, output caching of the help-center pages, and the sitemap with its own cache.
- **Close-out:** the developer guide, the PHASE-09 ticks and the D-045 as-built notes.

**Architecture:**
- **One answer for "not there".** Every help-center page builds on `ProductPageBase`. An unknown product, an unknown, invisible or empty category, a page past the end and an unpublished or misfiled article all end in the neutral 404 of an unknown route, byte for byte, with none of the product's theme. A key or slug that is not a slug is refused by the client without a call.
- **Text is text.** Only `KbArticleBody` (the API's sanitized HTML) and 09b's `CustomerMessageBody` turn text into markup. Everything else an agent wrote or a visitor typed is encoded by Razor, and every structured-data string is a `JsonLdText` whose converter keeps a `</script>` inside its string.
- **One cache policy, one predicate.** The framework's output cache keeps a help-center page for a minute, varying by the `page` query value only. `PortalCachePaths` decides what is kept; the search page, the forms, `/t/*` and every non-200 answer never are. The shared headers and the per-path rules wrap the cache, and one small step restores the request's own correlation id on a hit.
- **A sitemap no crawler can break.** `MapSeoSitemap` with a provider backed by one `IMemoryCache` entry (15 minutes), single-flight, a build on its own task with its own cancellation token, a failure remembered for a minute while the last good sitemap is served, and at most 50,000 addresses.

**Tech Stack:** .NET 10, ASP.NET Core Blazor static SSR, the framework's `AddOutputCache` and `IMemoryCache` (no new package), `SyntaxCircus.Blazor.Seo` 0.1.4, EF Core on Postgres (Testcontainers), bUnit and AngleSharp (which bUnit brings), xUnit v3, Shouldly, NSubstitute and Pester.

**Spec:** `docs/architecture/PHASE-09-public-portal.md` (T02 the rest of the KB client, T04 the sitemap, T12 to T15), `docs/architecture/UX-BRIEF-portal.md` (the KB, search, article and SEO sections), `docs/development/PORTAL-APP.md`, `docs/architecture/04-DECISION-LOG.md` (D-002, D-017, D-019, D-024, D-038, D-039, D-043, D-044, D-045) and the owner rulings of 2026-10-06 (the 09c scope plan), recorded as a dated "09c addendum" inside D-045 in Task 1 (no new decision number). This plan covers **09c only**. 09d (the styling, accessibility and no-JS pass, the double-send guard, the JavaScript niceties and the test hardening) gets its own plan.

### Owner rulings (2026-10-06), recorded as the D-045 09c addendum
1. **API: a category's articles.** `GET api/public/kb/{productKey}/categories/{categorySlug}/articles?page=&pageSize=`: published only, the product's plus the shared ones, the existing `PublishedIn` predicate, newest update first, `PagedResponse<PublicKbArticleSummaryDto>`; an unknown or inactive product or an unknown or invisible category is a 404; `public, max-age=60`; the Public rate limit.
2. **API: the active products.** `GET api/public/products` returns `PublicProductSummaryDto(Key, DisplayName)` for active products only, ordered by key, capped at 1,000, `public, max-age=300`. `/` still lists nothing.
3. **Portal client.** `IPublicKbClient` gains `ListCategoriesAsync`, `ListCategoryArticlesAsync`, `GetArticleAsync` and `GetSitemapAsync`; `IPublicProductClient` gains `ListAsync`. Reads are retried and forward the visitor's address.
4. **Pages.** `/p/{key}/kb`, `/p/{key}/kb/{category}` (paged, `?page=`, no script needed), `/p/{key}/kb/search?q=&page=` (a GET form: an empty-query prompt, a contact link for no result, plain-text snippets, `noindex` for a query, never cached) and `/p/{key}/kb/{category}/{slug}`; the neutral 404 for an unknown product, category or unpublished article; shared components and `KbCopy`; the product home's search box now works.
5. **SEO.** `SeoHead` (title, description, canonical, Open Graph, `NoIndex`); a `BreadcrumbList` and a local `Article` as JSON-LD, every string through `JsonLdText.Safe`; a hostile-title host test.
6. **Sitemap.** `MapSeoSitemap` with static entries and a provider: one products call and one sitemap call per product; absolute URLs from `TECHSTRAP_PORTAL_PUBLIC_URL`; a 15-minute `IMemoryCache` with single-flight, its own cancellation token, a failure cached for a minute with the last good value served meanwhile, a 50,000-URL cap logged when hit; the first crawler's address on the build's calls is accepted and recorded.
7. **Output caching.** A base policy with a path predicate (`PortalCachePaths.IsKbPage`): 60 seconds, varying by `page` only, not by host; never search, `/t/*`, the forms, suggest or `/not-found`; `UseOutputCache` after the error pages and before the endpoints; a success-only `Cache-Control: public, max-age=60` from a new Hosting `PathHeaderRule.SetOnSuccess`.
8. **KB images.** A plain-http image is blocked in Production by `img-src https:`; that is documented, not changed.
9. **Docs.** `PORTAL-APP.md`, the PHASE-09 ticks and corrected handoff names, the roadmap and discovery rows ("09c complete (pending merge); 09d not started") and the D-045 addendum and as-built notes.

### Decisions made while drafting (the D-045 addendum records them)
- **Task split.** The five tasks of the brief. Two moves: the 09a `SeoHostTests` pin "the sitemap is not mapped" is replaced in Task 2 (mapping the sitemap there breaks it; Task 5 keeps the full sitemap host tests), and the output-cache host tests that need pages are split: Task 2 proves the mechanism on a small host with the real wiring (`OutputCachePipelineTests`), Tasks 3 and 4 prove it on the real pages.
- **The spike, proved in a scratch copy before Tasks 2 to 4 were written (a temporary page, a probe pipeline and tests; each finding is now a durable test).**
  - **The cache policy.** `AddBasePolicy(p => p.With(predicate).Expire(60 s).SetVaryByQuery("page").SetVaryByHost(false))` keeps exactly the help-center paths: the same request twice makes one API call, while the product home and any other path make two. A 404, a 429 and a 503 are never stored (an unknown product asked twice reached the API twice; a 503 asked twice made six calls, three read retries each).
  - **The default key is wide.** Without the explicit rules six requests with different `utm` and `page` values made five API calls and two other `Host` values made two more; with `SetVaryByQuery("page")` and `SetVaryByHost(false)` the same requests made three and none. A cache that varies by the whole query string and the host lets a visitor fill the store with `?utm=1`, `?utm=2` and so on, so the Portal also keeps a request only when its `page` value is absent or one to four digits.
  - **A hit is not fully fresh.** Security headers and a rule's headers are right on a hit, but the stored copy replays the first request's `X-Correlation-Id`: a request that sent `cid-two` got `cid-one`. A step before `UseOutputCache` that sets the header again from `HttpContext.Items` when the response starts fixes it (the callbacks run last-registered-first, so it runs after the cache has written the stored copy). Pinned by `OutputCachePipelineTests` and `KbPageCacheHostTests`.
  - **Query binding.** `[SupplyParameterFromQuery]` binds case-insensitively and takes the first of two values, but an `int?` answers 500 for `?page=abc` and for a number that does not fit. The pages bind text and parse it (`KbPaging.Parse`).
  - **Head and JSON-LD.** `SeoHead` renders in static SSR (the layout already has `<HeadOutlet />`). The block is written as `type="application/ld&#x2B;json"`, which a browser reads as `application/ld+json`, so the tests parse the page with AngleSharp. It is data, not script: the CSP (`script-src 'self'`) does not change. `JsonLd` writes the JSON through a markup string with `UnsafeRelaxedJsonEscaping`: a title of `</script><img src=x onerror=alert(1)>` ended the block and an `img` element appeared in the page. A Razor file cannot hold the text `</script` in a string (the Razor parser reads a tag), so the hostile texts live in C#.
  - **Pre-escaping cannot work.** Escaping the text before it reaches the package's schema records is escaped a second time by the serializer (the JSON then reads back as backslash text). So the Portal's own records carry each string as a `JsonLdText`, a value type whose converter writes the string with `JavaScriptEncoder.Default` (`<`, `>`, `&`, the apostrophe, `+` and every non-ASCII character as u-escapes) through `Utf8JsonWriter.WriteRawValue`: no raw `<` in the block, and the JSON reads back as the original.
  - **The sitemap.** `MapSeoSitemap` accepts a provider that reads an `IMemoryCache` with single-flight: twenty concurrent requests made one build, the result is `application/xml` with each `<loc>` XML-escaped, and `cacheDuration` sets only the client's `Cache-Control`. `MemoryCacheOptions` has no `TimeProvider` (only an obsolete clock), so the cache takes its lifetimes as constructor values and the tests that wait use short real ones.
- **Deviations from the brief, each with its reason.**
  - **`JsonLdText` and Portal records instead of "escape, then use `BreadcrumbListSchema`".** The ruling says strings are escaped before they reach the schema; the spike shows that is double-escaped. The closest safe design is the converter above. The breadcrumb list is therefore a Portal record (`BreadcrumbListLd`), not the package's `BreadcrumbListSchema`, whose strings cannot carry the converter. The article record is local as ruled.
  - **The correlation-id step** (`UsePortalOutputCache`) is not in the brief; the spike found the defect.
  - **A cached request needs a plain `page` value** (absent, or one to four digits), so the key space is bounded by real content; the brief says only "varying by `page`".
  - **The search page is `no-store` for browsers** (a header rule), beside "never cached by the server". Two 09a/09b host tests that listed `/p/{key}/kb/search` among the paths that are never `no-store` are changed.
  - **A page past the end is the neutral 404, and an empty category is the API's 404**, so the spec's "empty category shows an empty state" becomes: the help-center home of a product with no article, and a search with no result, show the empty state. One page per real page number, and no empty page for a crawler to find.
  - **The static `/` entry is listed only when no default product is configured** (with one, `/` is a redirect, which a sitemap must not list), and the static entries count against the 50,000 limit, so the whole file never exceeds it.
  - **A sitemap that cannot be built and has no earlier version is the 500 page**, not a 503: the package's provider has no way to set a status.
  - **One extra shared component, `KbSearchBox`**, used by the help-center home and the search page (the product home keeps its own markup, which 09a pins).
  - **No page-level slug check on the category page.** A mutation showed it was dead code (the client refuses a malformed slug without a call, which `PublicKbClientTests` pins), so it was removed rather than pinned.
  - **`KbSlugShape` lives in the Portal** (80 characters), like `ProductKeyShape` (40): the Portal cannot reference the Domain, so there is no parity test, only the client tests.
  - **"Pin the Admin as unchanged"** is a Hosting test with exactly the Admin's call shape plus `PublicCacheHeaderPinTests` in `TechStrap.Admin.Tests`.
- **An honest survivor.** Removing `.Expire(PortalCachePaths.Lifetime)` from the policy changes nothing observable (the framework's default expiry is also 60 seconds); the call stays as documentation and the constant is pinned by `PortalCachePathsTests`. It is listed under Task 2.
- **A mutation-run hazard.** A mutation that removes a timeout (Task 2, row 32) would hang a test that waits for a build that never ends. That test carries a `Timeout` so it fails instead; `mut.py` reports a mutation that does not compile as "NOT A MUTATION" (exit code 4) instead of calling it killed.
- **No new package and no new configuration key**, so there is no D-043 edit in 09c. The output cache, `IMemoryCache` and `Utf8JsonWriter.WriteRawValue` are in the shared framework; AngleSharp comes with bUnit, which the Portal tests already reference.
- **No migration.** The category list and the product list read existing tables; `dotnet ef migrations has-pending-model-changes` is clean (Task 1, last step).
- **Shared files between tasks** (the tasks run in order, so the overlaps are safe): `PortalRoutes.cs` (Tasks 2 and 3), `_components.scss` (Tasks 3 and 4), `KbCopy.cs` (Tasks 3 and 4), `PortalRoutesTests.cs` (Tasks 2 and 3), `ProductPageBase.cs` (Task 3 only), `RepositoryDocs.Tests.ps1`, `04-DECISION-LOG.md`, `02-ARCHITECTURE.md`, `PHASE-09-public-portal.md` and `PORTAL-APP.md` (Tasks 1 and 5), `PortalSitemapBuilderTests.cs` (Task 2 only).

## Global Constraints

- **Build.** .NET SDK 10.0.401 targeting `net10.0`, with `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild`. Private fields are `_camelCase`, constants are PascalCase, namespaces are file-scoped.
- **Packages.** No new package in any project. `AddOutputCache`, `IMemoryCache` and `Utf8JsonWriter.WriteRawValue` are in the shared framework.
- **Project references.** The Portal references exactly Contracts and Hosting. Hosting stays a leaf: it may reference only packages.
- **Config (D-043).** 09c adds no configuration key. A key added later gets four edits: the host's `appsettings.json`, `src/TechStrap.Portal/.env.example`, `deploy/.env.portal.example` and compose where compose owns it; `scripts/tests/ConfigContract.Tests.ps1` must pass.
- **Portal conventions.**
  - Static SSR only, with no interactive render mode.
  - Components never inject `HttpClient`; only `Clients/` mentions it.
  - Copy lives in `*Copy` constants (`KbCopy` for the help center); no inline script or style.
  - All plain-text DTO fields are encoded. The `MarkupString` sites are exactly `CustomerMessageBody` and `KbArticleBody`; the word must not appear in any other Portal file, comments included (Task 4's architecture test counts the files).
  - Every route is a `PortalRoutes` constant or builder; no `/p...` or `/t...` literal outside `PortalRoutes`.
  - Every structured-data string is a `JsonLdText`; never put text an author wrote into the package's schema records or its `JsonLd` component.
  - A `.razor` file cannot hold the text `</script` in a string; put a hostile text in a C# test.
- **Encoding.** Write non-ASCII as `\u` escapes, using Python or .NET, never GNU sed. The Write tool decodes `\uXXXX`, so grep afterwards (`grep -nP "[^\x00-\x7F]"`). `SourceEncodingTests` and `SourceEscapeTests` must pass. Everything in this plan is ASCII; tests build non-ASCII text from `char.ConvertFromUtf32(...)`.
- **Secrets and PII.** Never log tokens, emails, names, subjects, references or search text. The sitemap cache logs a count and an exception, never a visitor's text.
- **Tests.**
  - Failing test first, with RED and GREEN recorded.
  - Prove each pin with a recorded mutation; every mutation must keep the Release build compiling (avoid `if (false)`: CS0162; use a condition the compiler cannot fold).
  - A test that waits for a real lifetime (the sitemap cache) uses a few hundred milliseconds and nothing shorter; a test that could hang carries an xUnit `Timeout` and reads `TestContext.Current.CancellationToken` in its own body (analyzer xUnit1069).
  - Every API-calling host test uses `FormTestKit.Factory`, whose factory asserts the visitor's address on every call when it is disposed.
  - Tests that set environment variables go in `ProcessEnvironmentCollection` (09c adds none).
  - Run a `--no-incremental` rebuild before the final runs, because mutation runs can leave stale DLLs.
  - No background mutation runs: every mutation runs in the foreground and the file is restored before the next.
- **Commits.** Use Conventional Commits. End each one with exactly these two lines, on separate lines:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
  ```
- **Forbidden.** `git add -f`, committing `.superpowers/`, `docker compose down -v`, killing processes you did not start, and committing without first running `git diff --cached --stat`.
- **Verification.**
  - `dotnet build TechStrap.slnx -c Release`: 0 warnings.
  - `dotnet test --solution TechStrap.CI.slnf -c Release`: passes.
  - `pwsh -File scripts/Invoke-ScriptTests.ps1`: passes (node tests included when node is installed).
  - `dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api --configuration Release --no-build`: no changes.
- **Branch.** Work on `feat/phase-09c-kb-seo` (cut from main at 8ab679e, which includes 09a, #14, and 09b, #15). Never edit or commit on `main`.

## Review Focus

1. **XSS in KB rendering and SEO.** `KbArticleBody` is the only new markup site; the JSON-LD is escaped and a hostile title or category cannot break out; meta attributes, the heading, the breadcrumb, a search snippet and the typed search text are encoded.
   - Pinned in Task 3 (`KbHomeHostTests`, `KbCategoryHostTests`, `KbSearchHostTests`, `KbComponentTests`) and Task 4 (`KbArticleHostTests`, `JsonLdTextTests`, `KbStructuredDataTests`, `KbPlainTextTests`, `PortalRuleTests`).
2. **Content leaks.** No draft, archived, other-product or invisible-category article reaches a page, the category list or the sitemap; an unknown product, category or slug gets the neutral, byte-identical 404.
   - Pinned in Task 1 (`ListPublicKbCategoryArticlesHandlerTests`, `PublicKbIntegrationTests`, `KbEndpointTests`), Task 3 (`KbCategoryHostTests`, `KbHomeHostTests`, `KbSearchHostTests`), Task 4 (`KbArticleHostTests`) and Task 5 (`SitemapHostTests`).
3. **Cache safety.** `/t/*`, the forms, search and suggest are never cached; a 404, 429 or 503 is never cached and never gets a public header; no `Set-Cookie` on KB pages; the headers and the correlation id are fresh on a hit; a key is never shared between products.
   - Pinned in Task 2 (`PortalCachePathsTests`, `OutputCachePipelineTests`, `ProgramOrderTests`, `PortalHeaderRulesTests`, `PathHeaderRuleHostTests`, `PublicCacheHeaderPinTests`), Task 3 (`KbPageCacheHostTests`) and Task 4 (`KbArticleHostTests`).
4. **Sitemap robustness.** An aborted crawler cannot poison the cache; one build serves a stampede; the URLs are absolute and only what the API returned (published only) is listed; the cap holds.
   - Pinned in Task 2 (`PortalSitemapCacheTests`, `PortalSitemapBuilderTests`) and Task 5 (`SitemapHostTests`).
5. **Product enumeration.** The products list carries the key and display name of active products only; `/` still lists nothing (09a's `RootPageHostTests` still pass).
   - Pinned in Task 1 (`ListPublicProductsRequestHandlerTests`, `PublicProductEndpointTests`, `PublicKbIntegrationTests`).

---

### Task 1: API additions: a category's articles, the active products, and the D-045 09c addendum

**Review Focus pin:** 2 (content leaks: a draft, an archived article, another product's article or category, an empty or unknown category and an inactive product are one 404) and 5 (the products list carries the key and display name of active products only). This task also records the owner's rulings and the spike's findings in D-045 and gives the Portal the two endpoints its pages and sitemap read.

**Files:**

- Modify: `docs/architecture/02-ARCHITECTURE.md`
- Modify: `docs/architecture/04-DECISION-LOG.md`
- Modify: `src/TechStrap.Api/Controllers/PublicKbController.cs`
- Modify: `src/TechStrap.Api/Controllers/PublicProductsController.cs`
- Create: `src/TechStrap.Application/Knowledge/ListPublicKbCategoryArticlesRequestHandler.cs`
- Modify: `src/TechStrap.Application/Knowledge/PublicKbModels.cs`
- Modify: `src/TechStrap.Application/Persistence/IKbRepository.cs`
- Create: `src/TechStrap.Application/Products/ListPublicProductsRequestHandler.cs`
- Modify: `src/TechStrap.Contracts/Kb/PublicKbDtos.cs`
- Create: `src/TechStrap.Contracts/Products/PublicProductSummaryDto.cs`
- Modify: `src/TechStrap.Infrastructure/Persistence/Repositories/KbRepository.PublicReads.cs`
- Test (modify): `scripts/tests/RepositoryDocs.Tests.ps1`
- Test (modify): `tests/TechStrap.Api.Tests/Controllers/ControllerActions.cs`
- Test (modify): `tests/TechStrap.Api.Tests/Intake/PublicProductEndpointTests.cs`
- Test (modify): `tests/TechStrap.Api.Tests/Kb/KbEndpointTests.cs`
- Test (create): `tests/TechStrap.Api.Tests/Kb/PublicListRateLimitTests.cs`
- Test (modify): `tests/TechStrap.Api.Tests/OpenApiSecurityTests.cs`
- Test (create): `tests/TechStrap.Application.Tests/Knowledge/ListPublicKbCategoryArticlesHandlerTests.cs`
- Test (create): `tests/TechStrap.Application.Tests/Products/ListPublicProductsRequestHandlerTests.cs`
- Test (modify): `tests/TechStrap.Infrastructure.IntegrationTests/PublicKbIntegrationTests.cs`

**Interfaces:**
- Consumes: `IKbRepository`, `KbRepository.PublishedIn` (the one predicate: Published, the product's plus the shared space, in a category the product can see), `PublicProductScope` (internal: `ResolveAsync`, `IsSlug`), `KbErrors.CategoryNotFound()` (`kb-category-not-found`, `ResultErrorKind.NotFound`), `IProductRepository.ListAsync(bool activeOnly, ct)`, `PagedResult<T>` (SyntaxCircus.Common), `PagedResponse<T>`, `Paging`, `KbLimits`, the test fixtures `KbFixture`, `TicketScenario`, `PersistenceTestHost`, `ApiTestDatabase`, `IntakeTestData` and `ControllerActions`.
- Produces:
  - Contracts: `PublicKbArticleSummaryDto(string Slug, string Title, string? Summary, string CategorySlug, string CategoryName, string? ProductKey, DateTimeOffset UpdatedAt)` (all plain text, no body, no author, no id); `PublicProductSummaryDto(string Key, string DisplayName)` and `PublicProductLimits.MaxListed` (1,000).
  - Application: `IListPublicKbCategoryArticlesRequestHandler.HandleAsync(string? productKey, string? categorySlug, int page, int pageSize, CancellationToken) : Task<Result<PagedResponse<PublicKbArticleSummaryDto>>>` (page size 10 by default and at most 25; a 404 `kb-category-not-found` for anything that is not a category with a published article); `IListPublicProductsRequestHandler.HandleAsync(CancellationToken) : Task<Result<IReadOnlyList<PublicProductSummaryDto>>>`; the model `PublicKbCategoryArticle`; `IKbRepository.ListPublicCategoryArticlesAsync(Guid productId, string categorySlug, int page, int pageSize, CancellationToken) : Task<PagedResult<PublicKbCategoryArticle>?>` (null when the product can see no published article in a category with that slug).
  - API: `GET api/public/kb/{productKey}/categories/{categorySlug}/articles?page=&pageSize=` (`PublicKbController.CategoryArticles`, `public, max-age=60`, an error is `no-store`) and `GET api/public/products` (`PublicProductsController.List`, `public, max-age=300`), both under the Public rate limit.
  - Docs: the D-045 09c addendum (rulings and spike findings, dated 2026-10-06, no new decision number), the two new rows and the rate-limit row in 02-ARCHITECTURE, and the Pester pins.

- [ ] **Step 1: Keep the helper tools outside the repository**

Every mutation step of this plan uses two small tools (the mutation helper of 09a and 09b with one addition, and a driver that runs a list of mutations). Keep them OUTSIDE the repository, for example in `C:\tmp\p09c-tools\` (the plan calls that folder `$T`). `mut.py` applies one or more replacements to a file, runs a command, reports KILLED, SURVIVED or NOT A MUTATION (the mutated code does not compile) and always puts the file back. `run_muts.py` runs a list of mutations (a spec file) in the foreground, one at a time, and appends one line per mutation to a results file.

`mut.py`

```python
"""Mutation helper for the PHASE-09c plan. Keep it OUTSIDE the repository (for example in a temp folder).

usage: python mut.py <file> --replace <old> <new> [--replace <old> <new> ...] -- <command ...>

Applies each replacement to <file> (each <old> must match exactly once; "\\n" in an argument means a newline), runs the command in the current
directory, prints the lines that summarize the run, and ALWAYS puts the file back. The mutation is KILLED when the command fails (exit code 0), a SURVIVOR
(exit code 3) when it passes and NOT A MUTATION (exit code 4) when the mutated code does not compile: pick another mutation. Run it from the repository root, after `git add`-ing the task's files, so a failed run can also be undone with
`git checkout -- <file>`.
"""
import subprocess
import sys

args = sys.argv[1:]
if "--" not in args or "--replace" not in args:
    sys.exit(__doc__)
split = args.index("--")
head, command = args[:split], args[split + 1 :]
path = head[0]
pairs = []
rest = head[1:]
while rest:
    if rest[0] != "--replace" or len(rest) < 3:
        sys.exit(__doc__)
    pairs.append((rest[1].replace("\\n", "\n"), rest[2].replace("\\n", "\n")))
    rest = rest[3:]

with open(path, encoding="utf-8", newline="") as handle:
    original = handle.read()
crlf = "\r\n" in original
mutated = original
for old, new in pairs:
    if crlf:
        old, new = old.replace("\n", "\r\n"), new.replace("\n", "\r\n")
    if mutated.count(old) != 1:
        sys.exit(f"ABORT: {mutated.count(old)} occurrences of {old[:70]!r} in {path}")
    mutated = mutated.replace(old, new)

try:
    with open(path, "w", encoding="utf-8", newline="") as handle:
        handle.write(mutated)
    result = subprocess.run(command, capture_output=True, text=True, timeout=900)
    lines = [line.strip() for line in (result.stdout + result.stderr).splitlines() if line.strip()]
    summary = [line for line in lines if line.startswith(("total:", "failed:", "succeeded:", "Tests Passed", "error"))
               or "Tests Passed" in line or "[-]" in line]
    print("\n".join(summary[:6]))
    broken = any("error CS" in line or line.startswith("Build failed") for line in lines)
    if broken:
        print("\n".join(line[-200:] for line in lines if "error CS" in line)[:600])
    print("NOT A MUTATION: the mutated code does not compile" if broken else "KILLED (the command failed)" if result.returncode != 0 else "SURVIVED (the command passed)")
    code = 4 if broken else 0 if result.returncode != 0 else 3
finally:
    with open(path, "w", encoding="utf-8", newline="") as handle:
        handle.write(original)
sys.exit(code)
```

`run_muts.py`

```python
"""run_muts.py <spec.py> <out.txt>: runs each mutation of spec.MUTATIONS in the foreground through mut.py, appends 'id: KILLED|SURVIVED|ABORT' to out.txt.

spec.MUTATIONS = [(id, file, [(old, new), ...], [command...]), ...]  (run from the repo root)
"""
import importlib.util
import os
import subprocess
import sys

spec_path, out_path = sys.argv[1], sys.argv[2]
spec = importlib.util.spec_from_file_location("spec", spec_path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
only = set(sys.argv[3:])
mut = os.path.join(os.path.dirname(os.path.abspath(__file__)), "mut.py")
for ident, path, pairs, command in module.MUTATIONS:
    if only and ident.split()[0] not in only:
        continue
    args = [sys.executable, mut, path]
    for old, new in pairs:
        args += ["--replace", old, new]
    args += ["--"] + command
    result = subprocess.run(args, capture_output=True, text=True)
    text = (result.stdout + result.stderr).strip().splitlines()
    verdict = {0: "KILLED", 3: "SURVIVED", 4: "NOT-COMPILING"}.get(result.returncode, "ABORT")
    with open(out_path, "a", encoding="utf-8") as handle:
        handle.write(f"{ident}: {verdict}  | {' / '.join(text[-3:])[:200]}\n")
```

Run every mutation from the repository root, after `git add -A`-ing the task's files, so `git checkout -- <file>` also restores a file if a run is interrupted. A mutation that SURVIVES is a failed task unless this plan lists it as an honest survivor; a NOT-COMPILING row is a bad mutation: pick another. In the spec files, `PT(...)` and the command lists are `dotnet test --project ... --filter-query ...`, and `PESTER` is `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal -Path scripts/tests/RepositoryDocs.Tests.ps1`.

Example (this task, spec `$T\specs\m1.py`, rows 1 to 20), from the repository root:

```bash
python $T/run_muts.py $T/specs/m1.py $T/res_m1.txt 1 2 3 4 5 6
```

- [ ] **Step 2: Write the failing tests**

Five test files arrive, five change, and one Pester block. The handler tests pin the neutral 404 (every malformed, unknown, inactive or invisible case), the page size and page normalization, the trimming and the mapping; the product handler tests pin "active only", the ordering, the cap and the DTO shape; the Postgres tests run both handlers over the real repository and renderer scenario (published only, scope, order, paging, a page past the end and the repository's own page size cap); the host tests pin the headers, the 404s, the OpenAPI and `ControllerActions` rows and the rate limit; the Pester block pins the addendum and the rows in the architecture tables.

`scripts/tests/RepositoryDocs.Tests.ps1`

```diff
@@ -232,6 +232,33 @@ Describe 'D-045 addendum (PHASE-09b rulings, 2026-10-06)' {
     }
 }
 
+Describe 'D-045 addendum (PHASE-09c rulings, 2026-10-06)' {
+    BeforeAll {
+        $script:Log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md'
+        $script:Section = [regex]::Match($script:Log, '(?s)## D-045:.*?(?=\r?\n## D-\d+:|\z)').Value
+    }
+
+    It 'is a dated addendum inside D-045, not a new decision number' {
+        $script:Section | Should -Match '(?m)^### Addendum \(2026-10-06, PHASE-09c knowledge base pages, SEO and caching\)'
+        $script:Log | Should -Not -Match '(?m)^## D-046'
+    }
+
+    It 'records each ruling the 09c plan rests on' {
+        foreach ($phrase in 'categories/{categorySlug}/articles', 'PublicKbArticleSummaryDto', 'PublicProductSummaryDto', 'PublicProductLimits.MaxListed', 'PublishedIn', 'kb-category-not-found', 'ListCategoryArticlesAsync',
+                'KbArticleBody', 'KbArticleCard', 'KbBreadcrumbs', 'JsonLdText', 'MapSeoSitemap', 'IMemoryCache', 'single-flight', '50,000', 'AddOutputCache', 'PortalCachePaths', 'SetOnSuccess', 'X-Correlation-Id',
+                'img-src', 'SetVaryByQuery') {
+            $script:Section | Should -Match ([regex]::Escape($phrase)) -Because "the addendum must mention $phrase"
+        }
+    }
+
+    It 'lists the two new routes in the architecture tables and the public rate limit row' {
+        $architecture = Get-RepoText 'docs/architecture/02-ARCHITECTURE.md'
+        $architecture | Should -Match ([regex]::Escape('`GET /api/public/kb/{productKey}/categories/{categorySlug}/articles?page=&pageSize=`'))
+        $architecture | Should -Match ([regex]::Escape('`GET /api/public/products` (anonymous, `public` limit; for the Portal sitemap)'))
+        $architecture | Should -Match ([regex]::Escape('`GET /api/public/products`, `GET /api/public/products/{key}`, public KB endpoints, sitemap'))
+    }
+}
+
 Describe 'D-045 as built in 09b' {
     BeforeAll {
         $log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md'
```

`tests/TechStrap.Api.Tests/Controllers/ControllerActions.cs`

```diff
@@ -52,6 +52,7 @@ public static class ControllerActions
         ["IntakeController.Submit"] = 201,
         ["PublicIntakeController.Submit"] = 201,
         ["PublicProductsController.Get"] = 200,
+        ["PublicProductsController.List"] = 200,
         ["KbArticlesController.List"] = 200,
         ["KbArticlesController.Get"] = 200,
         ["KbArticlesController.Create"] = 201,
@@ -66,6 +67,7 @@ public static class ControllerActions
         ["KbImagesController.Upload"] = 201,
         ["PublicKbController.Search"] = 200,
         ["PublicKbController.Categories"] = 200,
+        ["PublicKbController.CategoryArticles"] = 200,
         ["PublicKbController.Article"] = 200,
         ["PublicKbController.Sitemap"] = 200,
         ["CustomerTicketsController.Get"] = 200,
```

`tests/TechStrap.Api.Tests/Intake/PublicProductEndpointTests.cs`

```diff
@@ -39,6 +39,30 @@ public sealed class PublicProductEndpointTests(TestPostgres postgres) : IAsyncLi
         body.Key.ShouldBe("orbitly");
     }
 
+    [Fact]
+    public async Task The_anonymous_list_names_the_active_products_by_key_and_display_name_only_with_public_caching()
+    {
+        // Arrange
+        var (factory, _, _) = await StartAsync();
+        await using var _f = factory;
+        using var client = factory.CreateClient();
+
+        // Act
+        using var response = await client.GetAsync("/api/public/products", TestContext.Current.CancellationToken);
+
+        // Assert
+        response.StatusCode.ShouldBe(HttpStatusCode.OK);
+        response.Headers.CacheControl!.Public.ShouldBeTrue();
+        response.Headers.CacheControl.MaxAge.ShouldBe(TimeSpan.FromSeconds(300));
+        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
+        var list = System.Text.Json.JsonSerializer.Deserialize<List<PublicProductSummaryDto>>(raw, System.Text.Json.JsonSerializerOptions.Web)!;
+        list.ShouldBe([new PublicProductSummaryDto("orbitly", "Orbitly"), new PublicProductSummaryDto("paperplane", "Paperplane")]);
+        raw.ShouldNotContain("dormant");
+        raw.ShouldNotContain("logoPath");
+        raw.ShouldNotContain("accentColour");
+        System.Text.Json.JsonDocument.Parse(raw).RootElement[0].EnumerateObject().Select(property => property.Name).ShouldBe(["key", "displayName"]);
+    }
+
     [Theory]
     [InlineData("nope")]
     [InlineData("dormant")]
```

`tests/TechStrap.Api.Tests/Kb/KbEndpointTests.cs`

```diff
@@ -181,6 +181,72 @@ public sealed class KbEndpointTests(TestPostgres postgres) : IDisposable
         (await ReadAsync<List<PublicKbCategoryDto>>(await started.Anonymous.GetAsync("/api/public/kb/orbitly/categories", Ct))).ShouldBeEmpty();
     }
 
+    [Fact]
+    public async Task A_category_list_serves_only_published_articles_with_the_public_cache_header_and_pages()
+    {
+        await using var started = await StartAsync();
+        var product = await CreateProductAsync(started.Admin, "orbitly", "ORB");
+        var other = await CreateProductAsync(started.Admin, "paperplane", "PPL");
+        var category = await CreateCategoryAsync(started.Agent, product.Id, "account");
+        var otherCategory = await CreateCategoryAsync(started.Agent, other.Id, "billing");
+        var shared = await CreateCategoryAsync(started.Agent, null, "general");
+        await CreateArticleAsync(started.Agent, product.Id, category.Id, "still-a-draft");
+        foreach (var slug in new[] { "reset-password", "change-email", "close-account" })
+        {
+            var article = await CreateArticleAsync(started.Agent, product.Id, category.Id, slug);
+            (await started.Agent.PostAsync($"/api/kb/articles/{article.Id}/publish", null, Ct)).EnsureSuccessStatusCode();
+        }
+
+        var elsewhere = await CreateArticleAsync(started.Agent, other.Id, otherCategory.Id, "paperplane-only");
+        (await started.Agent.PostAsync($"/api/kb/articles/{elsewhere.Id}/publish", null, Ct)).EnsureSuccessStatusCode();
+        var sharedArticle = await CreateArticleAsync(started.Agent, null, shared.Id, "shared-tips");
+        (await started.Agent.PostAsync($"/api/kb/articles/{sharedArticle.Id}/publish", null, Ct)).EnsureSuccessStatusCode();
+
+        using var firstPage = await started.Anonymous.GetAsync("/api/public/kb/orbitly/categories/account/articles?pageSize=2", Ct);
+        using var secondPage = await started.Anonymous.GetAsync("/api/public/kb/orbitly/categories/account/articles?pageSize=2&page=2", Ct);
+        using var sharedPage = await started.Anonymous.GetAsync("/api/public/kb/orbitly/categories/general/articles", Ct);
+        var first = await ReadAsync<PagedResponse<PublicKbArticleSummaryDto>>(firstPage);
+        var second = await ReadAsync<PagedResponse<PublicKbArticleSummaryDto>>(secondPage);
+
+        first.ShouldSatisfyAllConditions(page => page.Items.Count.ShouldBe(2), page => page.TotalCount.ShouldBe(3), page => page.Page.ShouldBe(1), page => page.PageSize.ShouldBe(2));
+        second.Items.Count.ShouldBe(1);
+        first.Items.Concat(second.Items).Select(item => item.Slug).Order().ShouldBe(["change-email", "close-account", "reset-password"]);
+        first.Items.ShouldAllBe(item => item.ProductKey == "orbitly" && item.CategorySlug == "account" && item.Summary == "Summary of " + item.Slug && item.Title == "Title " + item.Slug);
+        (await ReadAsync<PagedResponse<PublicKbArticleSummaryDto>>(sharedPage)).Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
+            item => item.Slug.ShouldBe("shared-tips"), item => item.ProductKey.ShouldBeNull());
+        firstPage.Headers.GetValues("Cache-Control").Single().ShouldBe("public, max-age=60");
+        var raw = await firstPage.Content.ReadAsStringAsync(Ct);
+        raw.ShouldNotContain("still-a-draft");
+        raw.ShouldNotContain("paperplane-only");
+    }
+
+    [Theory]
+    [InlineData("/api/public/kb/orbitly/categories/no-such-category/articles")]
+    [InlineData("/api/public/kb/orbitly/categories/billing/articles")]
+    [InlineData("/api/public/kb/orbitly/categories/empty/articles")]
+    [InlineData("/api/public/kb/orbitly/categories/search/articles")]
+    [InlineData("/api/public/kb/orbitly/categories/NOT-A-SLUG/articles")]
+    [InlineData("/api/public/kb/nobody/categories/account/articles")]
+    public async Task An_unknown_invisible_or_empty_category_or_an_unknown_product_is_one_404_that_is_never_cached(string path)
+    {
+        await using var started = await StartAsync();
+        var product = await CreateProductAsync(started.Admin, "orbitly", "ORB");
+        var other = await CreateProductAsync(started.Admin, "paperplane", "PPL");
+        var account = await CreateCategoryAsync(started.Agent, product.Id, "account");
+        await CreateCategoryAsync(started.Agent, product.Id, "empty");
+        var billing = await CreateCategoryAsync(started.Agent, other.Id, "billing");
+        var published = await CreateArticleAsync(started.Agent, product.Id, account.Id, "reset-password");
+        (await started.Agent.PostAsync($"/api/kb/articles/{published.Id}/publish", null, Ct)).EnsureSuccessStatusCode();
+        var elsewhere = await CreateArticleAsync(started.Agent, other.Id, billing.Id, "invoices");
+        (await started.Agent.PostAsync($"/api/kb/articles/{elsewhere.Id}/publish", null, Ct)).EnsureSuccessStatusCode();
+
+        using var response = await started.Anonymous.GetAsync(path, Ct);
+
+        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
+        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
+        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("kb-category-not-found");
+    }
+
     [Fact]
     public async Task An_unknown_product_gets_empty_lists_and_one_uniform_not_found_for_the_article()
     {
```

`tests/TechStrap.Api.Tests/Kb/PublicListRateLimitTests.cs` (new)

```csharp
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Api.Tests.Auth;

namespace TechStrap.Api.Tests.Kb;

/// <summary>The two PHASE-09c lists sit under the Public rate limit like every other anonymous route: a visitor past the limit gets a 429 that is never cached.</summary>
public sealed class PublicListRateLimitTests(TestPostgres postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/api/public/products")]
    [InlineData("/api/public/kb/orbitly/categories/account/articles")]
    public async Task A_visitor_past_the_public_limit_gets_a_429_on_the_new_lists(string path)
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings)
        {
            ["RateLimiting:Public:PermitLimit"] = "2",
            ["RateLimiting:Public:WindowSeconds"] = "60",
        };
        await using var factory = new ApiFactory(
            settings: settings,
            configureServices: services => services.AddSingleton<IStartupFilter>(new SetRemoteIpAddressStartupFilter(IPAddress.Parse("192.0.2.5"))));
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? last = null;
        for (var i = 0; i < 3; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.10");
            last?.Dispose();
            last = await client.SendAsync(request, Ct);
            statuses.Add(last.StatusCode);
        }

        statuses[2].ShouldBe(HttpStatusCode.TooManyRequests);
        statuses.Take(2).ShouldNotContain(HttpStatusCode.TooManyRequests);
        last!.Headers.CacheControl?.Public.ShouldNotBe(true);
        last.Dispose();
    }
}
```

`tests/TechStrap.Api.Tests/OpenApiSecurityTests.cs`

```diff
@@ -81,10 +81,12 @@ public sealed partial class OpenApiSecurityTests
 
     [Theory]
     [InlineData("/api/customer/access-link", "post")]
+    [InlineData("/api/public/products", "get")]
     [InlineData("/api/public/products/{productKey}", "get")]
     [InlineData("/api/public/products/{productKey}/tickets", "post")]
     [InlineData("/api/public/kb/{productKey}/search", "get")]
     [InlineData("/api/public/kb/{productKey}/categories", "get")]
+    [InlineData("/api/public/kb/{productKey}/categories/{categorySlug}/articles", "get")]
     [InlineData("/api/public/kb/{productKey}/articles/{categorySlug}/{slug}", "get")]
     [InlineData("/api/public/kb/{productKey}/sitemap", "get")]
     public async Task A_public_operation_names_no_scheme(string path, string method)
```

`tests/TechStrap.Application.Tests/Knowledge/ListPublicKbCategoryArticlesHandlerTests.cs` (new)

```csharp
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Tests.Knowledge;

/// <summary>PHASE-09c Review Focus 2 (content leaks): the category list is the same neutral 404 for everything that is not a visible category with a published article.</summary>
public sealed class ListPublicKbCategoryArticlesHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Updated = new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);

    private readonly KbFixture _kb = new();

    public ListPublicKbCategoryArticlesHandlerTests()
    {
        _kb.Products.GetByKeyAsync("orbitly", Arg.Any<CancellationToken>()).Returns(_kb.Orbitly);
        var dormant = Product.Create("dormant", "Dormant", "DOR", null, _kb.Clock).Value;
        dormant.SetActive(false);
        _kb.Products.GetByKeyAsync("dormant", Arg.Any<CancellationToken>()).Returns(dormant);
    }

    private ListPublicKbCategoryArticlesRequestHandler Handler() => new(_kb.Products, _kb.KnowledgeBase);

    [Theory]
    [InlineData(null, "account")]
    [InlineData("", "account")]
    [InlineData("  ", "account")]
    [InlineData("nobody", "account")]
    [InlineData("dormant", "account")]
    [InlineData("orbitly", null)]
    [InlineData("orbitly", "")]
    [InlineData("orbitly", "   ")]
    [InlineData("orbitly", "ACCOUNT")]
    [InlineData("orbitly", "ac\0count")]
    [InlineData("orbitly", "ac\ud800count")]
    [InlineData("orbitly", "two--hyphens")]
    [InlineData("orbitly", "-leading")]
    public async Task An_unknown_inactive_or_malformed_product_or_slug_is_the_category_404_and_never_reaches_the_repository(string? key, string? category)
    {
        var result = await Handler().HandleAsync(key, category, 1, 10, Ct);

        result.IsSuccess.ShouldBeFalse();
        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe("kb-category-not-found");
        error.Kind.ShouldBe(ResultErrorKind.NotFound);
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().ListPublicCategoryArticlesAsync(default, default!, default, default, Ct);
    }

    [Fact]
    public async Task A_slug_longer_than_the_article_slug_limit_is_the_category_404()
    {
        var result = await Handler().HandleAsync("orbitly", new string('a', 81), 1, 10, Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-not-found");
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().ListPublicCategoryArticlesAsync(default, default!, default, default, Ct);
    }

    [Fact]
    public async Task A_category_the_product_cannot_see_or_that_is_empty_is_the_same_404()
    {
        _kb.KnowledgeBase.ListPublicCategoryArticlesAsync(_kb.Orbitly.Id, "account", 1, 10, Arg.Any<CancellationToken>()).Returns((PagedResult<PublicKbCategoryArticle>?)null);

        var result = await Handler().HandleAsync("orbitly", "account", 1, 10, Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-not-found");
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
        _kb.KnowledgeBase.ListPublicCategoryArticlesAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<PublicKbCategoryArticle>([], 1, used, 1));

        await Handler().HandleAsync("orbitly", "account", 1, requested, Ct);

        await _kb.KnowledgeBase.Received(1).ListPublicCategoryArticlesAsync(_kb.Orbitly.Id, "account", 1, used, Ct);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public async Task The_page_number_is_normalised_before_the_query(int requested, int used)
    {
        _kb.KnowledgeBase.ListPublicCategoryArticlesAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<PublicKbCategoryArticle>([], used, 10, 100));

        await Handler().HandleAsync("orbitly", "account", requested, 10, Ct);

        await _kb.KnowledgeBase.Received(1).ListPublicCategoryArticlesAsync(_kb.Orbitly.Id, "account", used, 10, Ct);
    }

    [Fact]
    public async Task A_padded_slug_is_trimmed_and_the_rows_become_plain_summary_dtos_with_the_paging_the_repository_reports()
    {
        _kb.KnowledgeBase.ListPublicCategoryArticlesAsync(_kb.Orbitly.Id, "account", 2, 10, Arg.Any<CancellationToken>()).Returns(new PagedResult<PublicKbCategoryArticle>(
        [
            new PublicKbCategoryArticle("reset-password", "Reset your password", "How to reset", "account", "Account", "orbitly", Updated),
            new PublicKbCategoryArticle("shared-tips", "Shared tips", null, "account", "Account", null, Updated.AddDays(-1)),
        ], 2, 10, 12));

        var result = await Handler().HandleAsync(" orbitly ", " account ", 2, 10, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldSatisfyAllConditions(
            page => page.Page.ShouldBe(2),
            page => page.PageSize.ShouldBe(10),
            page => page.TotalCount.ShouldBe(12));
        result.Value.Items.ShouldBe(
        [
            new PublicKbArticleSummaryDto("reset-password", "Reset your password", "How to reset", "account", "Account", "orbitly", Updated),
            new PublicKbArticleSummaryDto("shared-tips", "Shared tips", null, "account", "Account", null, Updated.AddDays(-1)),
        ]);
    }

    [Fact]
    public void The_summary_dto_has_no_body_no_author_no_id_and_no_status()
    {
        typeof(PublicKbArticleSummaryDto).GetProperties().Select(property => property.Name).Order().ShouldBe(
            ["CategoryName", "CategorySlug", "ProductKey", "Slug", "Summary", "Title", "UpdatedAt"]);
    }
}
```

`tests/TechStrap.Application.Tests/Products/ListPublicProductsRequestHandlerTests.cs` (new)

```csharp
using NSubstitute;
using TechStrap.Application.Persistence;
using TechStrap.Application.Products;
using TechStrap.Contracts.Products;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Tests.Products;

/// <summary>PHASE-09c Review Focus 5 (product enumeration): the list holds the key and the display name of ACTIVE products and nothing else.</summary>
public sealed class ListPublicProductsRequestHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly IProductRepository _products = Substitute.For<IProductRepository>();

    private static Product Stored(string key, string displayName, bool isActive)
    {
        var branding = ProductBranding.Restore(displayName, "/brand/logo.png", "#7C3AED", null, null);
        return Product.Restore(Guid.CreateVersion7(), key, "Internal " + displayName, "PREFIX", branding, isActive, version: 1);
    }

    [Fact]
    public async Task The_list_asks_for_active_products_only_and_returns_key_and_display_name_ordered_by_key()
    {
        _products.ListAsync(true, Arg.Any<CancellationToken>()).Returns([Stored("paperplane", "Paperplane", true), Stored("acme", "Acme Corp", true), Stored("orbitly", "Orbitly", true)]);

        var result = await new ListPublicProductsRequestHandler(_products).HandleAsync(Ct);

        result.Value.ShouldBe([new PublicProductSummaryDto("acme", "Acme Corp"), new PublicProductSummaryDto("orbitly", "Orbitly"), new PublicProductSummaryDto("paperplane", "Paperplane")]);
        await _products.Received(1).ListAsync(true, Ct);
        await _products.DidNotReceive().ListAsync(false, Ct);
    }

    [Fact]
    public async Task An_inactive_product_never_appears_even_if_the_repository_returned_it()
    {
        _products.ListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns([Stored("dormant", "Dormant", false), Stored("orbitly", "Orbitly", true)]);

        var result = await new ListPublicProductsRequestHandler(_products).HandleAsync(Ct);

        result.Value.Select(product => product.Key).ShouldBe(["orbitly"]);
    }

    [Fact]
    public async Task The_list_is_capped_keeping_the_first_keys_in_order()
    {
        var many = Enumerable.Range(0, PublicProductLimits.MaxListed + 5).Select(i => Stored($"p{i:D5}", $"Product {i}", true)).Reverse().ToList();
        _products.ListAsync(true, Arg.Any<CancellationToken>()).Returns(many);

        var result = await new ListPublicProductsRequestHandler(_products).HandleAsync(Ct);

        result.Value.Count.ShouldBe(PublicProductLimits.MaxListed);
        result.Value[0].Key.ShouldBe("p00000");
        result.Value[^1].Key.ShouldBe($"p{PublicProductLimits.MaxListed - 1:D5}");
    }

    [Fact]
    public async Task No_active_product_is_an_empty_list_not_an_error()
    {
        _products.ListAsync(true, Arg.Any<CancellationToken>()).Returns([]);

        var result = await new ListPublicProductsRequestHandler(_products).HandleAsync(Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public void The_summary_dto_has_the_key_and_the_display_name_only()
    {
        typeof(PublicProductSummaryDto).GetProperties().Select(property => property.Name).Order().ShouldBe(["DisplayName", "Key"]);
        PublicProductLimits.MaxListed.ShouldBe(1_000);
    }
}
```

`tests/TechStrap.Infrastructure.IntegrationTests/PublicKbIntegrationTests.cs`

```diff
@@ -2,7 +2,9 @@ using Microsoft.Extensions.DependencyInjection;
 using SyntaxCircus.Common;
 using TechStrap.Application.Knowledge;
 using TechStrap.Application.Persistence;
+using TechStrap.Application.Products;
 using TechStrap.Contracts.Kb;
+using TechStrap.Contracts.Paging;
 using TechStrap.Domain.Knowledge;
 using TechStrap.Domain.Products;
 using TechStrap.Infrastructure.Content;
@@ -449,6 +451,134 @@ public sealed class PublicKbIntegrationTests(PostgresFixture postgres) : Postgre
         page.Value.Html.ShouldBe(preview.Value.Html);
     }
 
+    private static Task<Result<PagedResponse<PublicKbArticleSummaryDto>>> CategoryArticles(World world, string product, string category, int page = 1, int pageSize = 25) =>
+        WithHandlersAsync(world, sp => new ListPublicKbCategoryArticlesRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>())
+            .HandleAsync(product, category, page, pageSize, Ct));
+
+    [Fact]
+    public async Task A_category_list_holds_only_published_articles_of_the_product_and_the_shared_space_in_a_category_the_product_can_see()
+    {
+        await using var host = new PersistenceTestHost(Database);
+        var world = await SeedAsync(host);
+
+        var acme = await CategoryArticles(world, "acme", "acme-cat");
+        var shared = await CategoryArticles(world, "acme", "general");
+        var orbitlyShared = await CategoryArticles(world, "orbitly", "general");
+
+        // Left out of acme-cat: the draft, the archived article, Orbitly's article filed there and (not in this category anyway) Acme's article filed in an Orbitly category.
+        acme.Value.Items.Select(item => item.Slug).ShouldBe(["acme-published"]);
+        acme.Value.Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
+            item => item.Title.ShouldBe("Router reset guide"),
+            item => item.Summary.ShouldBe("How to reset the router"),
+            item => item.CategorySlug.ShouldBe("acme-cat"),
+            item => item.CategoryName.ShouldBe("Acme things"),
+            item => item.ProductKey.ShouldBe("acme"));
+        acme.Value.TotalCount.ShouldBe(1);
+        shared.Value.Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(item => item.Slug.ShouldBe("shared-published"), item => item.ProductKey.ShouldBeNull());
+        orbitlyShared.Value.Items.Select(item => item.Slug).ShouldBe(["shared-published"]);
+    }
+
+    [Theory]
+    [InlineData("acme", "orb-cat")]
+    [InlineData("orbitly", "acme-cat")]
+    [InlineData("acme", "empty-cat")]
+    [InlineData("acme", "no-such-category")]
+    [InlineData("acme", "search")]
+    [InlineData("nobody", "general")]
+    [InlineData("dormant", "general")]
+    [InlineData("acme", "ACME-CAT")]
+    [InlineData("acme", "acme-cat\0")]
+    public async Task Another_products_category_an_empty_one_an_unknown_one_and_an_inactive_product_are_all_the_same_404(string product, string category)
+    {
+        await using var host = new PersistenceTestHost(Database);
+        var world = await SeedAsync(host);
+        var dormant = Product.Create("dormant", "Dormant", "DOR", null, host.Clock).Value;
+        dormant.SetActive(false);
+        (await host.CommitAsync(sp => { sp.GetRequiredService<IProductRepository>().Add(dormant); return Task.CompletedTask; })).IsSuccess.ShouldBeTrue();
+
+        var result = await CategoryArticles(world, product, category);
+
+        var error = result.Errors.ShouldHaveSingleItem();
+        error.Code.ShouldBe("kb-category-not-found");
+    }
+
+    [Fact]
+    public async Task Archiving_the_last_published_article_of_a_category_makes_it_a_404_like_the_category_list_does()
+    {
+        await using var host = new PersistenceTestHost(Database);
+        var world = await SeedAsync(host);
+        (await CategoryArticles(world, "acme", "acme-cat")).IsSuccess.ShouldBeTrue();
+
+        var published = world.Articles["acme-published"];
+        (await host.CommitAsync(async sp =>
+        {
+            var kb = sp.GetRequiredService<IKbRepository>();
+            var loaded = (await kb.GetArticleAsync(published.Id, Ct))!;
+            loaded.Archive(host.Clock).IsSuccess.ShouldBeTrue();
+            kb.UpdateArticle(loaded);
+        })).IsSuccess.ShouldBeTrue();
+
+        (await CategoryArticles(world, "acme", "acme-cat")).Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-not-found");
+    }
+
+    [Fact]
+    public async Task A_category_list_is_newest_update_first_paged_and_a_page_past_the_end_is_empty_with_the_total()
+    {
+        await using var host = new PersistenceTestHost(Database);
+        var scenario = await TicketScenario.CreateAsync(host);
+        var category = KbCategory.Create(null, "general", "General", 1, host.Clock).Value;
+        var articles = new List<KbArticle>();
+        foreach (var slug in new[] { "a-oldest", "b-second", "c-third", "d-fourth", "e-newest" })
+        {
+            host.Clock.Advance(TimeSpan.FromMinutes(10));
+            var article = Article(scenario, host, null, category, slug, slug, "s", "body");
+            Publish(host, article);
+            articles.Add(article);
+        }
+
+        (await host.CommitAsync(sp =>
+        {
+            var kb = sp.GetRequiredService<IKbRepository>();
+            kb.AddCategory(category);
+            articles.ForEach(kb.AddArticle);
+            return Task.CompletedTask;
+        })).IsSuccess.ShouldBeTrue();
+        Task<Result<PagedResponse<PublicKbArticleSummaryDto>>> Page(int page, int size) =>
+            host.ReadAsync(sp => new ListPublicKbCategoryArticlesRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>()).HandleAsync("acme", "general", page, size, Ct));
+
+        var first = await Page(1, 2);
+        var second = await Page(2, 2);
+        var last = await Page(3, 2);
+        var past = await Page(4, 2);
+
+        first.Value.Items.Select(item => item.Slug).ShouldBe(["e-newest", "d-fourth"]);
+        second.Value.Items.Select(item => item.Slug).ShouldBe(["c-third", "b-second"]);
+        last.Value.Items.Select(item => item.Slug).ShouldBe(["a-oldest"]);
+        past.Value.Items.ShouldBeEmpty();
+        new[] { first, second, last, past }.ShouldAllBe(page => page.Value.TotalCount == 5 && page.Value.PageSize == 2);
+        past.Value.Page.ShouldBe(4);
+
+        // The repository caps the page size itself, whatever the handler passed.
+        var direct = await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().ListPublicCategoryArticlesAsync(scenario.Acme.Id, "general", 1, 1000, Ct));
+        direct!.PageSize.ShouldBe(KbLimits.MaxPublicSearchPageSize);
+        direct.Items.Count.ShouldBe(5);
+    }
+
+    [Fact]
+    public async Task The_public_product_list_is_the_active_products_key_and_display_name_only()
+    {
+        await using var host = new PersistenceTestHost(Database);
+        await TicketScenario.CreateAsync(host);
+        var dormant = Product.Create("dormant", "Dormant", "DOR", null, host.Clock).Value;
+        dormant.SetActive(false);
+        (await host.CommitAsync(sp => { sp.GetRequiredService<IProductRepository>().Add(dormant); return Task.CompletedTask; })).IsSuccess.ShouldBeTrue();
+
+        var result = await host.ReadAsync(sp => new ListPublicProductsRequestHandler(sp.GetRequiredService<IProductRepository>()).HandleAsync(Ct));
+
+        result.Value.Select(product => product.Key).ShouldBe(["acme", "orbitly"]);
+        result.Value.Select(product => product.DisplayName).ShouldBe(["Acme", "Orbitly"]);
+    }
+
     [Fact]
     public void The_public_article_dto_has_no_author_and_no_id()
     {
```


- [ ] **Step 3: Run the tests to verify they fail**

The handler tests and the Postgres tests do not compile yet (the DTOs, the handlers and the repository method are missing), and the Pester block fails for the missing addendum.

```bash
dotnet build tests/TechStrap.Application.Tests -c Release
pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal -Path scripts/tests/RepositoryDocs.Tests.ps1
```

Expected (recorded in the scratch copy with the source files set aside):

```text
error CS0246: The type or namespace name 'ListPublicKbCategoryArticlesRequestHandler' could not be found
error CS0246: The type or namespace name 'PublicKbArticleSummaryDto' could not be found
error CS0246: The type or namespace name 'PublicProductSummaryDto' could not be found
...
Tests Passed: 32, Failed: 3, Skipped: 0, Inconclusive: 0, NotRun: 0
```

- [ ] **Step 4: Write the implementation**

The contracts and the model, the repository method on the one `PublishedIn` predicate, the two handlers (registered by `ApplicationHandlerRegistration`, which scans for a class ending in `Handler` with a matching interface, so there is no DI edit), and the two controller actions.


`src/TechStrap.Api/Controllers/PublicKbController.cs`

```diff
@@ -44,6 +44,17 @@ public sealed class PublicKbController : ControllerBase
         string productKey, string categorySlug, string slug, [FromServices] IGetPublishedKbArticleRequestHandler handler, CancellationToken cancellationToken) =>
         CachedFor(HitMaxAgeSeconds, await handler.HandleAsync(productKey, categorySlug, slug, cancellationToken));
 
+    /// <summary>One page of a category's Published articles (the product's and the shared ones), newest update first, 10 a page (at most 25). An unknown, invisible or empty category is a 404.</summary>
+    [HttpGet("categories/{categorySlug}/articles")]
+    public async Task<IActionResult> CategoryArticles(
+        string productKey,
+        string categorySlug,
+        [FromServices] IListPublicKbCategoryArticlesRequestHandler handler,
+        CancellationToken cancellationToken,
+        [FromQuery] int page = 1,
+        [FromQuery] int pageSize = KbLimits.DefaultPublicSearchPageSize) =>
+        CachedFor(HitMaxAgeSeconds, await handler.HandleAsync(productKey, categorySlug, page, pageSize, cancellationToken));
+
     [HttpGet("sitemap")]
     public async Task<IActionResult> Sitemap(string productKey, [FromServices] IGetKbSitemapRequestHandler handler, CancellationToken cancellationToken) =>
         CachedFor(SitemapMaxAgeSeconds, await handler.HandleAsync(productKey, cancellationToken));
```

`src/TechStrap.Api/Controllers/PublicProductsController.cs`

```diff
@@ -14,6 +14,18 @@ namespace TechStrap.Api.Controllers;
 [EnableRateLimiting(PublicRateLimitOptions.PolicyName)]
 public sealed class PublicProductsController : ControllerBase
 {
+    /// <summary>The active products' key and display name for the portal's sitemap (D-045 addendum): cacheable for 300 seconds.</summary>
+    [HttpGet]
+    public async Task<IActionResult> List([FromServices] IListPublicProductsRequestHandler handler, CancellationToken cancellationToken)
+    {
+        Response.Headers.CacheControl = "no-store";
+        return (await handler.HandleAsync(cancellationToken)).ToActionResult(this, products =>
+        {
+            Response.Headers.CacheControl = "public, max-age=300";
+            return Ok(products);
+        });
+    }
+
     [HttpGet("{productKey}")]
     public async Task<IActionResult> Get(string productKey, [FromServices] IGetPublicProductRequestHandler handler, CancellationToken cancellationToken)
     {
```

`src/TechStrap.Application/Knowledge/ListPublicKbCategoryArticlesRequestHandler.cs` (new)

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Domain.Rules;

namespace TechStrap.Application.Knowledge;

public interface IListPublicKbCategoryArticlesRequestHandler
{
    Task<Result<PagedResponse<PublicKbArticleSummaryDto>>> HandleAsync(string? productKey, string? categorySlug, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>
/// GET /api/public/kb/{productKey}/categories/{categorySlug}/articles (anonymous, PHASE-09c, D-045 addendum). One page of the Published articles the
/// product can see in the category, newest update first, 10 a page and at most 25. Everything that is not a category with at least one such article (an
/// unknown or inactive product, a malformed or unknown slug, another product's category, an empty category) is the same 404, so a caller cannot tell
/// which keys or categories exist. A page past the end is a 200 with no items. Every text field of the DTO is plain text.
/// </summary>
public sealed class ListPublicKbCategoryArticlesRequestHandler(IProductRepository products, IKbRepository knowledgeBase) : IListPublicKbCategoryArticlesRequestHandler
{
    public async Task<Result<PagedResponse<PublicKbArticleSummaryDto>>> HandleAsync(
        string? productKey, string? categorySlug, int page, int pageSize, CancellationToken cancellationToken)
    {
        var size = pageSize < 1 ? KbLimits.DefaultPublicSearchPageSize : Math.Min(pageSize, KbLimits.MaxPublicSearchPageSize);
        var product = await PublicProductScope.ResolveAsync(products, productKey, cancellationToken);
        if (product is null || !PublicProductScope.IsSlug(categorySlug, DomainLimits.KbSlugMaxLength))
        {
            return Result<PagedResponse<PublicKbArticleSummaryDto>>.Failure(KbErrors.CategoryNotFound());
        }

        var found = await knowledgeBase.ListPublicCategoryArticlesAsync(product.Id, categorySlug!.Trim(), Paging.NormalizePage(page), size, cancellationToken);
        if (found is null)
        {
            return Result<PagedResponse<PublicKbArticleSummaryDto>>.Failure(KbErrors.CategoryNotFound());
        }

        return Result<PagedResponse<PublicKbArticleSummaryDto>>.Success(new PagedResponse<PublicKbArticleSummaryDto>(
            [.. found.Items.Select(item => new PublicKbArticleSummaryDto(item.Slug, item.Title, item.Summary, item.CategorySlug, item.CategoryName, item.ProductKey, item.UpdatedAt))],
            found.Page, found.PageSize, found.TotalCount));
    }
}
```

`src/TechStrap.Application/Knowledge/PublicKbModels.cs`

```diff
@@ -22,3 +22,6 @@ public sealed record PublicKbCategoryCount(KbCategory Category, int ArticleCount
 public sealed record PublicKbLinkTarget(Guid ArticleId, string CategorySlug, string Slug);
 
 public sealed record PublicKbSitemapRow(string? ProductKey, string CategorySlug, string Slug, DateTimeOffset UpdatedAt);
+
+/// <summary>One row of a category's article list: <see cref="Summary"/> is the author's plain-text summary (null when there is none), <see cref="ProductKey"/> is null for a shared article.</summary>
+public sealed record PublicKbCategoryArticle(string Slug, string Title, string? Summary, string CategorySlug, string CategoryName, string? ProductKey, DateTimeOffset UpdatedAt);
```

`src/TechStrap.Application/Persistence/IKbRepository.cs`

```diff
@@ -67,6 +67,13 @@ public interface IKbRepository
     /// <summary>Categories visible to the product (its own and the shared ones) with their Published article counts, empty ones left out, sort order then name.</summary>
     Task<IReadOnlyList<PublicKbCategoryCount>> ListPublicCategoriesAsync(Guid productId, CancellationToken cancellationToken);
 
+    /// <summary>
+    /// One page of the Published articles the product can see in the category with the given slug (its own and the shared ones, in a category the product can see),
+    /// newest update first. Null when the product can see no Published article in a category with that slug: the category does not exist, belongs to another
+    /// product or is empty (the category list leaves empty ones out). A page past the end is a non-null result with no items. The page size is capped at <c>KbLimits.MaxPublicSearchPageSize</c>.
+    /// </summary>
+    Task<PagedResult<PublicKbCategoryArticle>?> ListPublicCategoryArticlesAsync(Guid productId, string categorySlug, int page, int pageSize, CancellationToken cancellationToken);
+
     /// <summary>Published articles visible to the product, newest update first, at most <c>KbLimits.MaxSitemapEntries</c>.</summary>
     Task<IReadOnlyList<PublicKbSitemapRow>> ListPublicSitemapAsync(Guid productId, CancellationToken cancellationToken);
 
```

`src/TechStrap.Application/Products/ListPublicProductsRequestHandler.cs` (new)

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Products;

namespace TechStrap.Application.Products;

public interface IListPublicProductsRequestHandler
{
    Task<Result<IReadOnlyList<PublicProductSummaryDto>>> HandleAsync(CancellationToken cancellationToken);
}

/// <summary>
/// GET /api/public/products (anonymous, PHASE-09c, D-045 addendum). The ACTIVE products' key and display name, ordered by key and capped at
/// <see cref="PublicProductLimits.MaxListed"/>, for the portal's sitemap. An inactive product never appears. Listing the keys is accepted because the sitemap publishes them anyway.
/// </summary>
public sealed class ListPublicProductsRequestHandler(IProductRepository products) : IListPublicProductsRequestHandler
{
    public async Task<Result<IReadOnlyList<PublicProductSummaryDto>>> HandleAsync(CancellationToken cancellationToken)
    {
        var active = await products.ListAsync(activeOnly: true, cancellationToken);
        return Result<IReadOnlyList<PublicProductSummaryDto>>.Success(
            [.. active
                .Where(product => product.IsActive)
                .OrderBy(product => product.Key, StringComparer.Ordinal)
                .Take(PublicProductLimits.MaxListed)
                .Select(product => new PublicProductSummaryDto(product.Key, product.Branding.DisplayName))]);
    }
}
```

`src/TechStrap.Contracts/Kb/PublicKbDtos.cs`

```diff
@@ -22,3 +22,9 @@ public sealed record PublishedKbArticleDto(
 public sealed record PublicKbCategoryDto(string Slug, string Name, string? Description, int ArticleCount);
 
 public sealed record KbSitemapEntryDto(string? ProductKey, string CategorySlug, string Slug, DateTimeOffset UpdatedAt);
+
+/// <summary>
+/// One row of a category's article list (PHASE-09c). Every text field is plain text: the consumer must encode it. <paramref name="Summary"/> is the
+/// author's summary (not a search snippet) and may be null. <paramref name="ProductKey"/> is null for a shared article. There is no body, no author and no id.
+/// </summary>
+public sealed record PublicKbArticleSummaryDto(string Slug, string Title, string? Summary, string CategorySlug, string CategoryName, string? ProductKey, DateTimeOffset UpdatedAt);
```

`src/TechStrap.Contracts/Products/PublicProductSummaryDto.cs` (new)

```csharp
namespace TechStrap.Contracts.Products;

/// <summary>
/// One row of the public product list (PHASE-09c): the key and the display name, nothing else. No logo, no colors, no id, no email. The list holds
/// active products only and exists for the portal's sitemap; the portal's root page never shows it.
/// </summary>
public sealed record PublicProductSummaryDto(string Key, string DisplayName);

/// <summary>Limits of the public product list.</summary>
public static class PublicProductLimits
{
    /// <summary>The most products the public list returns: a defensive cap, far above any real install.</summary>
    public const int MaxListed = 1_000;
}
```

`src/TechStrap.Infrastructure/Persistence/Repositories/KbRepository.PublicReads.cs`

```diff
@@ -137,6 +137,34 @@ internal sealed partial class KbRepository
         return [.. categories.Select(category => new PublicKbCategoryCount(category.ToDomain(), countById[category.Id]))];
     }
 
+    public async Task<PagedResult<PublicKbCategoryArticle>?> ListPublicCategoryArticlesAsync(
+        Guid productId, string categorySlug, int page, int pageSize, CancellationToken cancellationToken)
+    {
+        var normalisedPage = Paging.NormalizePage(page);
+        var size = Math.Clamp(Paging.NormalizePageSize(pageSize), 1, KbLimits.MaxPublicSearchPageSize);
+        var inCategory =
+            from article in PublishedIn(productId)
+            join category in context.Set<KbCategoryRecord>().AsNoTracking() on article.CategoryId equals category.Id
+            where category.Slug == categorySlug
+            select new { Article = article, Category = category };
+        var total = await inCategory.CountAsync(cancellationToken);
+        if (total == 0)
+        {
+            return null;
+        }
+
+        var items = await (
+            from row in inCategory
+            join owner in context.Set<ProductRecord>().AsNoTracking() on row.Article.ProductId equals owner.Id into owners
+            from owner in owners.DefaultIfEmpty()
+            orderby row.Article.UpdatedAt descending, row.Article.Id descending
+            select new PublicKbCategoryArticle(
+                row.Article.Slug, row.Article.Title, row.Article.Summary, row.Category.Slug, row.Category.Name, owner == null ? null : owner.Key, row.Article.UpdatedAt))
+            .Skip(Paging.Offset(normalisedPage, size)).Take(size)
+            .ToListAsync(cancellationToken);
+        return new PagedResult<PublicKbCategoryArticle>(items, normalisedPage, size, total);
+    }
+
     public async Task<IReadOnlyList<PublicKbSitemapRow>> ListPublicSitemapAsync(Guid productId, CancellationToken cancellationToken) =>
         await (
             from article in PublishedIn(productId)
```


- [ ] **Step 5: Record the rulings in D-045 and the routes in the architecture tables**

The 09c addendum goes inside D-045 (after the 09b addendum, before "Approval"), with the spike's findings. The two routes and the new rate-limit wording go in 02-ARCHITECTURE.

`docs/architecture/02-ARCHITECTURE.md`

```diff
@@ -264,6 +264,7 @@ Conventions:
 | `POST /api/intake/tickets` (Trusted or Public API key) | `SubmitTicketRequestHandler` (same use case; channel `Api`; trust from key kind) | same as above, plus `ICurrentUserService` for key principal (product id, kind) and `IIntakeIdempotencyStore` (optional `Idempotency-Key`) | same as above, plus idempotency store | 201 `SubmitTicketResponse` (number, view URL; a repeated `Idempotency-Key` returns the original ticket number with a fresh link; the stored response never holds the link, D-033); 400; 401 uniform for a bad key; external ref from a Public key is dropped with a warning and its metadata flagged untrusted (rule fixed in PHASE-05) | H, C, I | D-001, D-016, D-020 |
 | Worker outbox loop (`EmailOutboxWorker` hosted service, resolves the scoped handler from a fresh DI scope per iteration via `IServiceScopeFactory`) | `DrainEmailOutboxHandler` (plain `Task` or `Result`; no caller branching other than loop delay) | `IEmailOutboxStore`, `IEmailTemplateRenderer`, `IProductRepository`, `IOutboundEmailSender`, `IOptions<EmailOutboxWorkerOptions>`, `ILogger` | Outbox store (`SKIP LOCKED`), SMTP sender | Loop: batch processed means poll again; empty or failure means delay; unexpected exception logged and loop continues | H, I, W | D-010, D-012 |
 | `GET /api/public/products/{key}` (anonymous, `public` limit, Cache-Control) | `GetPublicProductRequestHandler` | `IProductRepository` | EF repos | 200 `PublicProductDto` (name, logo, accent; no secrets) with `Cache-Control: public, max-age`; 404 `no-store` | H, C, I | D-002 |
+| `GET /api/public/products` (anonymous, `public` limit; for the Portal sitemap) | `ListPublicProductsRequestHandler` | `IProductRepository` | EF repos | 200 `PublicProductSummaryDto[]` (key and display name of the active products, by key, at most 1,000) with `Cache-Control: public, max-age=300` | H, C, I | D-045 |
 
 ### 7.3 Ticket operations (PHASE-06)
 
@@ -314,6 +315,7 @@ Conventions:
 | `GET /api/public/kb/{productKey}/search?q=&category=` (anonymous, `public` limit; also deflection) | `SearchPublicKbArticlesRequestHandler` | `IKbRepository`, `IProductRepository` | EF repos (FTS) | 200 `PagedResponse<PublicKbSearchResultDto>` with `Cache-Control: public, max-age=60` | H, C, I | D-011, D-044 |
 | `GET /api/public/kb/{productKey}/articles/{categorySlug}/{slug}` (anonymous, `public` limit) | `GetPublishedKbArticleRequestHandler` | `IKbRepository`, `IProductRepository`, `IKbContentRenderer` | EF repos, KB Markdig profile and sanitiser | 200 `PublishedKbArticleDto` (sanitized HTML) with `Cache-Control: public, max-age=60`; 404 `no-store` | H, C, I | D-014, D-044 |
 | `GET /api/public/kb/{productKey}/categories` (anonymous, `public` limit) | `ListPublicKbCategoriesRequestHandler` | `IKbRepository`, `IProductRepository` | EF repos | 200 `PublicKbCategoryDto[]` | H, C | none |
+| `GET /api/public/kb/{productKey}/categories/{categorySlug}/articles?page=&pageSize=` (anonymous, `public` limit) | `ListPublicKbCategoryArticlesRequestHandler` | `IKbRepository`, `IProductRepository` | EF repos | 200 `PagedResponse<PublicKbArticleSummaryDto>` (published only, newest update first) with `Cache-Control: public, max-age=60`; 404 `no-store` for an unknown, invisible or empty category | H, C, I | D-045 |
 | `GET /api/public/kb/{productKey}/sitemap` (anonymous, `public` limit) | `GetKbSitemapRequestHandler` | `IKbRepository`, `IProductRepository` | EF repos | 200 `KbSitemapEntryDto[]` | H, C | none |
 
 ### 7.5 Live updates (PHASE-10)
@@ -417,7 +419,7 @@ Per _template pattern CLIENT_IP_RATE_LIMITING.md (reverse proxy in front of Dock
 
 | Policy | Applies to | Partition | Default |
 | --- | --- | --- | --- |
-| `public` | `GET /api/public/products/{key}`, public KB endpoints, sitemap | client IP | 120 / min |
+| `public` | `GET /api/public/products`, `GET /api/public/products/{key}`, public KB endpoints, sitemap | client IP | 120 / min |
 | `public-submit` | `POST /api/public/products/{key}/tickets` | client IP | 5 / 10 min |
 | `intake-key` (Public key) | `POST /api/intake/tickets` with a Public key | key prefix + client IP | 10 / min |
 | `intake-key` (Trusted key) | `POST /api/intake/tickets` with a Trusted key | key prefix + client IP (a key id is impossible before authentication; the limiter reads the raw `X-Api-Key` prefix, so a spoofer who knows a prefix exhausts only their own IP's partition) | 120 / min |
```

`docs/architecture/04-DECISION-LOG.md`

```diff
@@ -1728,6 +1728,29 @@ The owner's rulings for PHASE-09b, and what the plan's spike proved. They extend
 - **Known in 09b: `TicketToken.Value` is internal** and read in two places only (`ApiConnection` for the header and `PortalRoutes` for a link).
 - **Known in 09b: `ProductKey` sits in the middle of the positional `CustomerTicketDto`** (after `Number`, before `Subject`). Anything that builds or deconstructs that record by position (the PHASE-11 SDK, a generated client) must follow the new order; a JSON reader by name is not affected.
 
+### Addendum (2026-10-06, PHASE-09c knowledge base pages, SEO and caching)
+The owner's rulings for PHASE-09c, and what the plan's spike proved. They extend D-045 and the 09b addendum; where they differ from the text above, they win. There is no new decision number.
+
+**Rulings**
+- **API: a category's articles.** `GET api/public/kb/{productKey}/categories/{categorySlug}/articles?page=&pageSize=` (anonymous, the Public rate limit, `public, max-age=60` on success and `no-store` on an error) returns `PagedResponse<PublicKbArticleSummaryDto>`: `Slug`, `Title`, `Summary`, `CategorySlug`, `CategoryName`, `ProductKey` (null for a shared article) and `UpdatedAt`, all plain text, newest update first, 10 a page and at most 25. It is built on the one `PublishedIn` predicate of `KbRepository` (Published, the product's plus the shared space, in a category the product can see). An unknown or inactive product, a malformed or unknown slug, another product's category and a category with no published article are the same 404 (`kb-category-not-found`), like the article route; a page past the end is a 200 with no items. The handler is `ListPublicKbCategoryArticlesRequestHandler`; the repository method is `ListPublicCategoryArticlesAsync`. There is no schema change.
+- **API: the active products.** `GET api/public/products` (anonymous, the Public rate limit, `public, max-age=300`) returns `PublicProductSummaryDto(Key, DisplayName)` for the ACTIVE products, ordered by key and capped at `PublicProductLimits.MaxListed` (1,000). Listing the keys is accepted because the sitemap publishes them anyway; it carries no logo, colour, id or address, and the Portal's root page still lists nothing.
+- **The Portal client.** `IPublicKbClient` gains `ListCategoriesAsync`, `ListCategoryArticlesAsync`, `GetArticleAsync`, `GetSitemapAsync` and a `SearchAsync` overload with a page; `IPublicProductClient` gains `ListAsync`. All are reads through the read client (retried, the visitor's address forwarded). A key or slug that is not a slug is the uniform not-found error and no call is made.
+- **Pages** (static SSR, each on `ProductPageBase`): `/p/{key}/kb` (the categories with their counts and descriptions), `/p/{key}/kb/{category}` (the article list, paged with `?page=`, which works without script), `/p/{key}/kb/search?q=&page=` (a GET form: an empty query shows a prompt, no result shows a contact link, a snippet is plain text and encoded, a query makes the page noindex, and it is never cached) and `/p/{key}/kb/{category}/{slug}` (the article). An unknown product, an unknown category and an unpublished article are the one neutral 404, byte for byte. The shared pieces are `KbArticleCard`, `KbBreadcrumbs`, `Pager` and `StateMessage`; the words are in `KbCopy`. `KbArticleBody` is the second and last place the Portal renders markup (the API sanitises it); `PortalRules.MarkupStringSites` lists exactly `CustomerMessageBody` and `KbArticleBody`. The `page` query value is read as text and parsed by the Portal (the framework's own binding to a number answers 500 for `?page=abc`).
+- **SEO.** `SeoHead` sets the title, the description (the article summary, else the first sentence of the body as plain text), the canonical address (the current product path, so a shared article is canonical under each product), Open Graph and `NoIndex`; the image is a Portal asset. The structured data is a `BreadcrumbList` and an `Article`, both Portal records.
+  - **Escaping.** `SyntaxCircus.Blazor.Seo` 0.1.4 renders `JsonLd` through a markup string with an encoder that leaves `<`, `>` and `&` alone, so a `</script>` in an article title ends the script block and injects markup (the spike reproduced it). Escaping the text before it reaches the package's schema records would be double-escaped by the serialiser (the JSON would then read back as the backslash text), so the Portal's own records carry every string as a `JsonLdText`, a value type whose converter writes the string with the strict encoder: `<`, `>`, `&`, `'`, `+` and every non-ASCII character become `\uXXXX`, and the JSON reads back as the original text. `JsonLdText.Safe(text)` is the only way to make one. The problem is to be reported upstream as a package issue.
+- **Sitemap.** `MapSeoSitemap` with a provider: the products list, then one sitemap call per product, giving the product home, the KB home, the categories (derived from the entries, with the newest update as `lastmod`) and the articles; a shared article is listed under each product. The URLs are absolute, built from `TECHSTRAP_PORTAL_PUBLIC_URL`. The provider is backed by one `IMemoryCache` entry for 15 minutes with single-flight: the build runs with its own cancellation token (a crawler that goes away cannot poison the cache), a failure is remembered for 1 minute and the last good value is served meanwhile, and at most 50,000 URLs are listed (a cut is logged). The build's API calls carry the address of the visitor whose request started it (accepted and recorded as a known gap). The static `/` entry is listed only when no default product is configured, because `/` then redirects.
+- **Output caching** with the framework's `AddOutputCache` (no new package): one base policy with a path predicate (`PortalCachePaths`), 60 seconds, varying by the `page` query value only and not by host. Cached: `/p/{key}/kb`, a category page and an article page. Never cached: the search page, `/t/*`, the form pages, the suggest adapter, `/not-found` and every response that is not a 200. `UseOutputCache` sits after the error pages and before the endpoints, so the security headers are applied on a hit; a small step before it restores the request's own `X-Correlation-Id`, because a stored copy replays the first request's. Browsers get `Cache-Control: public, max-age=60` on a KB page from a new success-only Hosting rule, `PathHeaderRule.SetOnSuccess`, and `no-store` on the search page; a 404, 429 or 503 never gets a public header.
+- **KB images.** A plain-http image is blocked in Production by `img-src 'self' https: data:`. That is the correct posture; it is documented, not changed.
+- **Docs.** PORTAL-APP.md, the PHASE-09 ticks (T02, T04, T12 to T15 and the deliverables), the roadmap and discovery rows ("09c complete (pending merge); 09d not started") and the as-built notes below.
+
+**Spike findings (proven in a scratch copy before the plan was written)**
+- **The cache policy.** `AddBasePolicy(p => p.With(predicate).Expire(60 s).SetVaryByQuery("page").SetVaryByHost(false))` caches only the KB paths: the same request twice makes one API call, and the product home and any other path make two. A 404, a 429 and a 503 are never stored (an unknown product asked twice reaches the API twice).
+- **The default key.** Without the explicit rules the key holds the whole query string and the host: six requests with different `utm` or `page` values made five API calls, and two other `Host` values made two more. With `SetVaryByQuery("page")` and `SetVaryByHost(false)` the same requests made three and none. The Portal also caches a request only when its `page` value is absent or one to four digits, so `?page=<garbage>` cannot fill the store.
+- **A hit is not fully fresh.** The security headers and a rule's headers are right on a hit, but the stored copy replays the first request's `X-Correlation-Id` (a request that sent `cid-two` got `cid-one`). A step before `UseOutputCache` that sets the header again when the response starts (from `HttpContext.Items`) fixes it; the spike proved it.
+- **Query binding.** The framework binds `[SupplyParameterFromQuery]` case-insensitively and takes the first of two values, but `?page=abc` and `?page=99999999999` answer 500 for an `int?`; the pages bind text.
+- **Head and JSON-LD.** `SeoHead` works in static SSR (the layout already has `<HeadOutlet />`). The ld+json block is rendered as `type="application/ld&#x2B;json"`, which a browser reads as `application/ld+json`, so the tests parse the page with an HTML parser. The block is data, not script, so the CSP is unchanged (`script-src 'self'`). The package's `JsonLd` lets a hostile title inject an element (`<img src=x onerror=...>` became part of the page); the converter-based `JsonLdText` keeps the same title inside the string, and the Razor-encoded meta tags and title were already safe. A Razor file cannot hold the text `</script` in a string (the Razor parser reads it as a tag), so the hostile texts live in C#.
+- **The sitemap.** `MapSeoSitemap` accepts a provider that reads an `IMemoryCache` with single-flight: twenty concurrent requests made one build, the result is `application/xml` with each `<loc>` XML-escaped, and `cacheDuration` only sets the client's `Cache-Control`. `MemoryCacheOptions` has no `TimeProvider` (only the obsolete `ISystemClock`), so the cache takes its two lifetimes as constructor values and the tests use short real ones.
+
 ### Approval
 - **Approved by:** Jon Seeley (owner, PHASE-09 planning)
 - **Approved on:** 2026-10-05
```


- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet build TechStrap.slnx -c Release
dotnet test --project tests/TechStrap.Application.Tests -c Release --filter-query "/*/*/ListPublic*/*"
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter-query "/*/*/PublicKbIntegrationTests/*"
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/*/KbEndpointTests/*"
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/*/PublicProductEndpointTests/*"
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/*/PublicListRateLimitTests/*"
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/*/OpenApi*/*"
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/*Controllers*/*/*"
pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal -Path scripts/tests/RepositoryDocs.Tests.ps1
```

Expected: 0 warnings; `total: 32 succeeded: 32`; `total: 41 succeeded: 41` (Docker must be running); `total: 44 succeeded: 44`; `total: 4 succeeded: 4`; `total: 2 succeeded: 2`; `total: 32 succeeded: 32`; `total: 205 succeeded: 205`; `Tests Passed: 35, Failed: 0`.

The rate-limit test passes because both new routes sit under the `Public` policy; the host tests of the existing `RoutePolicyCoverageTests` (every route declares exactly one known policy and every public route is rate limited) pass without a new entry.

- [ ] **Step 7: Prove each pin with a recorded mutation**

Save `$T\specs\m1.py`:

```python
APPT = ["dotnet", "test", "--project", "tests/TechStrap.Application.Tests", "-c", "Release", "--filter-query"]
INTT = ["dotnet", "test", "--project", "tests/TechStrap.Infrastructure.IntegrationTests", "-c", "Release", "--filter-query", "/*/*/PublicKbIntegrationTests/*"]
APIT = ["dotnet", "test", "--project", "tests/TechStrap.Api.Tests", "-c", "Release", "--filter-query"]
H = "src/TechStrap.Application/Knowledge/ListPublicKbCategoryArticlesRequestHandler.cs"
P = "src/TechStrap.Application/Products/ListPublicProductsRequestHandler.cs"
R = "src/TechStrap.Infrastructure/Persistence/Repositories/KbRepository.PublicReads.cs"
CT = "src/TechStrap.Api/Controllers/PublicKbController.cs"
PC = "src/TechStrap.Api/Controllers/PublicProductsController.cs"
HT = APPT + ["/*/*/ListPublicKbCategoryArticlesHandlerTests/*"]
PT = APPT + ["/*/*/ListPublicProductsRequestHandlerTests/*"]

MUTATIONS = [
    ("1 handler: no slug check", H, [(" || !PublicProductScope.IsSlug(categorySlug, DomainLimits.KbSlugMaxLength))", ")")], HT),
    ("2 handler: no page size cap", H, [("Math.Min(pageSize, KbLimits.MaxPublicSearchPageSize)", "pageSize")], HT),
    ("3 handler: default page size 25", H, [("? KbLimits.DefaultPublicSearchPageSize :", "? KbLimits.MaxPublicSearchPageSize :")], HT),
    ("4 handler: page not normalized", H, [("Paging.NormalizePage(page)", "page")], HT),
    ("5 handler: slug not trimmed", H, [("categorySlug!.Trim()", "categorySlug!")], HT),
    ("6 handler: a null page is success", H, [("if (found is null)\n        {\n            return Result<PagedResponse<PublicKbArticleSummaryDto>>.Failure(KbErrors.CategoryNotFound());\n        }\n\n        return", "found ??= new PagedResult<PublicKbCategoryArticle>([], 1, size, 0);\n\n        return")], HT),
    ("7 repo: category slug filter removed", R, [("            where category.Slug == categorySlug\n", "")], INTT),
    ("8 repo: oldest first", R, [("orderby row.Article.UpdatedAt descending, row.Article.Id descending", "orderby row.Article.UpdatedAt ascending, row.Article.Id descending")], INTT),
    ("9 repo: not limited to published", R, [("from article in PublishedIn(productId)\n            join category in context.Set<KbCategoryRecord>().AsNoTracking() on article.CategoryId equals category.Id\n            where category.Slug == categorySlug", "from article in context.Set<KbArticleRecord>().AsNoTracking()\n            join category in context.Set<KbCategoryRecord>().AsNoTracking() on article.CategoryId equals category.Id\n            where category.Slug == categorySlug")], INTT),
    ("10 repo: empty category is a page", R, [("        if (total == 0)\n        {\n            return null;\n        }\n\n        var items", "        var items")], INTT),
    ("11 repo: no page size clamp", R, [("Math.Clamp(Paging.NormalizePageSize(pageSize), 1, KbLimits.MaxPublicSearchPageSize);\n        var inCategory", "Paging.NormalizePageSize(pageSize);\n        var inCategory")], INTT),
    ("12 products: inactive kept", P, [("                .Where(product => product.IsActive)\n", "")], PT),
    ("13 products: not ordered by key", P, [("                .OrderBy(product => product.Key, StringComparer.Ordinal)\n", "")], PT),
    ("14 products: no cap", P, [("                .Take(PublicProductLimits.MaxListed)\n", "")], PT),
    ("15 products: asks for every product", P, [("ListAsync(activeOnly: true,", "ListAsync(activeOnly: false,")], PT),
    ("16 controller: category list cached 300 s", CT, [("CachedFor(HitMaxAgeSeconds, await handler.HandleAsync(productKey, categorySlug, page, pageSize, cancellationToken))", "CachedFor(SitemapMaxAgeSeconds, await handler.HandleAsync(productKey, categorySlug, page, pageSize, cancellationToken))")], APIT + ["/*/*/KbEndpointTests/*"]),
    ("17 controller: product list cached 60 s", PC, [("Response.Headers.CacheControl = \"public, max-age=300\";\n            return Ok(products);", "Response.Headers.CacheControl = \"public, max-age=60\";\n            return Ok(products);")], APIT + ["/*/*/PublicProductEndpointTests/*"]),
    ("18 controller: product list not rate limited", PC, [("[EnableRateLimiting(PublicRateLimitOptions.PolicyName)]\n", "")], APIT + ["/*/*/PublicListRateLimitTests/*"]),
]

PESTER = ["pwsh", "-NoProfile", "-File", "scripts/Invoke-ScriptTests.ps1", "-Output", "Minimal", "-Path", "scripts/tests/RepositoryDocs.Tests.ps1"]
MUTATIONS += [
    ("19 dto: the product summary grows a logo", "src/TechStrap.Contracts/Products/PublicProductSummaryDto.cs", [("(string Key, string DisplayName);", "(string Key, string DisplayName, string? LogoPath = null);")], PT),
    ("20 docs: the architecture row loses its words", "docs/architecture/02-ARCHITECTURE.md", [("(anonymous, `public` limit; for the Portal sitemap)", "(anonymous, `public` limit; for the Portal map)")], PESTER),
]
```

Run them (in three foreground batches; each takes about a minute) and record the results:

| # | The mutation | File | Result |
| --- | --- | --- | --- |
| 1 | handler: no slug check | `ListPublicKbCategoryArticlesRequestHandler.cs` | KILLED (9 failing) |
| 2 | handler: no page size cap | `ListPublicKbCategoryArticlesRequestHandler.cs` | KILLED (2 failing) |
| 3 | handler: default page size 25 | `ListPublicKbCategoryArticlesRequestHandler.cs` | KILLED (2 failing) |
| 4 | handler: page not normalized | `ListPublicKbCategoryArticlesRequestHandler.cs` | KILLED (2 failing) |
| 5 | handler: slug not trimmed | `ListPublicKbCategoryArticlesRequestHandler.cs` | KILLED (1 failing) |
| 6 | handler: a null page is success | `ListPublicKbCategoryArticlesRequestHandler.cs` | KILLED (1 failing) |
| 7 | repo: category slug filter removed | `KbRepository.PublicReads.cs` | KILLED (7 failing) |
| 8 | repo: oldest first | `KbRepository.PublicReads.cs` | KILLED (1 failing) |
| 9 | repo: not limited to published | `KbRepository.PublicReads.cs` | KILLED (4 failing) |
| 10 | repo: empty category is a page | `KbRepository.PublicReads.cs` | KILLED (6 failing) |
| 11 | repo: no page size clamp | `KbRepository.PublicReads.cs` | KILLED (1 failing) |
| 12 | products: inactive kept | `ListPublicProductsRequestHandler.cs` | KILLED (1 failing) |
| 13 | products: not ordered by key | `ListPublicProductsRequestHandler.cs` | KILLED (2 failing) |
| 14 | products: no cap | `ListPublicProductsRequestHandler.cs` | KILLED (1 failing) |
| 15 | products: asks for every product | `ListPublicProductsRequestHandler.cs` | KILLED (2 failing) |
| 16 | controller: category list cached 300 s | `PublicKbController.cs` | KILLED (1 failing) |
| 17 | controller: product list cached 60 s | `PublicProductsController.cs` | KILLED (1 failing) |
| 18 | controller: product list not rate limited | `PublicProductsController.cs` | KILLED (1 failing) |
| 19 | dto: the product summary grows a logo | `PublicProductSummaryDto.cs` | KILLED (1 failing) |
| 20 | docs: the architecture row loses its words | `02-ARCHITECTURE.md` | KILLED |

Every mutation is killed; there are no survivors in this task.

- [ ] **Step 8: Run the full suites, the EF check and commit**

```bash
dotnet test --project tests/TechStrap.Application.Tests -c Release
dotnet test --project tests/TechStrap.Architecture.Tests -c Release
dotnet test --project tests/TechStrap.Api.Tests -c Release
dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api --configuration Release --no-build
```

Expected: 760, 294 and 917 tests pass; `No changes have been made to the model since the last migration.` (this task adds no migration).

```bash
git add -A
git diff --cached --stat
git commit -m "feat: PHASE-09c API lists for a category's articles and the active products (D-045)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

### Task 2: Portal plumbing: the KB and product list clients, the output cache, the success-only header rule and the sitemap provider

**Review Focus pin:** 3 (cache safety: what is kept, what never is, that a hit carries the request's own headers and correlation id, and that a 404, a 429 and a 503 are never stored or marked public) and 4 (sitemap robustness: single-flight, a build no crawler can cancel, a failure remembered for a minute with the last good sitemap served, absolute addresses, the 50,000 cap).

**Files:**

- Modify: `src/TechStrap.Hosting/Wiring/PathHeaderRule.cs`
- Create: `src/TechStrap.Portal/Caching/PortalCachePaths.cs`
- Create: `src/TechStrap.Portal/Caching/PortalOutputCache.cs`
- Modify: `src/TechStrap.Portal/Clients/IPublicKbClient.cs`
- Modify: `src/TechStrap.Portal/Clients/IPublicProductClient.cs`
- Modify: `src/TechStrap.Portal/Clients/PublicKbClient.cs`
- Modify: `src/TechStrap.Portal/Clients/PublicProductClient.cs`
- Modify: `src/TechStrap.Portal/Headers/PortalHeaderRules.cs`
- Modify: `src/TechStrap.Portal/Program.cs`
- Create: `src/TechStrap.Portal/Routing/KbSlugShape.cs`
- Modify: `src/TechStrap.Portal/Routing/PortalRoutes.cs`
- Modify: `src/TechStrap.Portal/Seo/PortalSeoRegistration.cs`
- Create: `src/TechStrap.Portal/Seo/PortalSitemap.cs`
- Create: `src/TechStrap.Portal/Seo/PortalSitemapBuilder.cs`
- Create: `src/TechStrap.Portal/Seo/PortalSitemapCache.cs`
- Test (create): `tests/TechStrap.Admin.Tests/PublicCacheHeaderPinTests.cs`
- Test (modify): `tests/TechStrap.Api.Tests/Hosting/PathHeaderRuleHostTests.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/Api/ApiHarness.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Caching/OutputCachePipelineTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Caching/PortalCachePathsTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Caching/ProgramOrderTests.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/Clients/PublicKbClientTests.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/Clients/PublicProductClientTests.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/Headers/FormPageHeaderHostTests.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/Headers/PortalHeaderRulesTests.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/Headers/TicketHeaderHostTests.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/Products/ProductPageBaseTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Routing/KbSlugShapeTests.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/Routing/PortalRoutesTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Seo/PortalSitemapBuilderTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Seo/PortalSitemapCacheTests.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/Seo/SeoHostTests.cs`

**Interfaces:**
- Consumes: Task 1's endpoints and DTOs (`PublicKbCategoryDto`, `PublicKbArticleSummaryDto`, `PublishedKbArticleDto`, `KbSitemapEntryDto`, `PublicProductSummaryDto`), `ApiConnection.GetAsync<T>`, `ApiQuery.Build`, `ProductKeyShape`, `ProblemMapping.NotFound()`, `PortalRoutes` (`ProductHome`, `KbHome`, `KbCategory`, `KbArticle`), `PortalHeaderRules`, `PortalOptions` (`PublicBaseUrl`, `DefaultProductKeyOrNull`), `UseTechStrapWebHost`, `UseTechStrapErrorPages`, `CorrelationIdOptions` (SyntaxCircus.AspNetCore.Common), `SeoEndpointExtensions.MapSeoSitemap`, `SitemapEntry`, and the test seams `PortalFactory`, `FormTestKit`, `StubApiHandler`, `ApiHarness`.
- Produces:
  - Hosting: `PathHeaderRule.SetOnSuccess(Func<PathString, bool> matches, params (string Name, string Value)[] headers)`: sets the headers on a 2xx answer for a matching path only.
  - Clients: `IPublicKbClient.SearchAsync(string productKey, string text, int page, int pageSize, ct)` (the old three-argument overload is unchanged), `ListCategoriesAsync(productKey, ct)`, `ListCategoryArticlesAsync(productKey, categorySlug, page, pageSize, ct)`, `GetArticleAsync(productKey, categorySlug, slug, ct)`, `GetSitemapAsync(productKey, ct)`; `IPublicProductClient.ListAsync(ct) : Task<Result<IReadOnlyList<PublicProductSummaryDto>>>`. All are reads through the read client; a product key or slug that is not a slug is `ProblemMapping.NotFound()` with no call.
  - Routing: `KbSlugShape.IsWellFormed(string?)` and `KbSlugShape.MaxLength` (80); `PortalRoutes.KbSegment` ("kb"), `KbSearchSegment` ("search"), `PageParameter` ("page"), `QueryParameter` ("q").
  - Caching (`TechStrap.Portal.Caching`): `PortalCachePaths.Lifetime` (60 s), `BrowserCacheControl` ("public, max-age=60"), `IsKbPage(PathString)`, `IsKbSearchPath(PathString)` and `IsCacheable(HttpRequest)` (a KB page whose `page` value is absent or one to four digits); `PortalOutputCache.AddPortalOutputCache(this IServiceCollection)` and `UsePortalOutputCache(this WebApplication)` (the correlation-id step, then `UseOutputCache`). `PortalHeaderRules.Rules` has five rules: the two new ones are index 3 (`SetOnSuccess`, the browser header on a KB page) and index 4 (`no-store` on the search page).
  - Sitemap (`TechStrap.Portal.Seo`): `PortalSitemapBuilder(IPublicProductClient, IPublicKbClient, IOptions<PortalOptions>, ILogger<PortalSitemapBuilder>)` with `BuildAsync(int reservedForStatic, CancellationToken)`, `MaxUrls` (50,000) and `EntriesOf(string baseUrl, string productKey, IReadOnlyList<KbSitemapEntryDto>)`; `SitemapBuildException`; `PortalSitemapCache(IMemoryCache, ILogger<PortalSitemapCache>, TimeSpan ttl, TimeSpan failureTtl, TimeSpan buildTimeout)` with `GetAsync(Func<CancellationToken, Task<IReadOnlyList<SitemapEntry>>> build, CancellationToken requestAborted)` and the defaults `DefaultTtl` (15 min), `DefaultFailureTtl` (1 min), `BuildTimeout` (2 min); `SitemapUnavailableException`; `PortalSitemap.AddPortalSitemap(this IServiceCollection)`, `StaticEntries(PortalOptions)`, `ProviderAsync(IServiceProvider, CancellationToken)` and `ClientCacheDuration` (5 min). `MapPortalSeo` now maps `/sitemap.xml`.
  - Test seams: `ApiHarness.Create(clientIp, time, configure)` registers the sitemap services; `OutputCachePipelineTests` shows how to build a small host with the real wiring.

- [ ] **Step 1: Write the failing tests**

New and changed test files. The ones to read first are `OutputCachePipelineTests` (the cache mechanism on a small host with the real wiring in the order `Program.cs` uses, and a counting endpoint at each path), `PortalCachePathsTests` (every path and every `page` value), `PortalSitemapCacheTests` and `PortalSitemapBuilderTests` (the content and the robustness), `PathHeaderRuleHostTests` (the new Hosting rule, and the Admin's exact call shape) and `PublicKbClientTests` (every new call, the slug guards and the failure mapping). Two 09a/09b tests change meaning: the sitemap is now served (`SeoHostTests`), and the search page is now `no-store` (`TicketHeaderHostTests`, `FormPageHeaderHostTests`).

`tests/TechStrap.Admin.Tests/PublicCacheHeaderPinTests.cs` (new)

```csharp
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

/// <summary>
/// PHASE-09c adds a success-only <c>Cache-Control</c> rule to the shared header wiring for the Portal's help center. The Admin shares that wiring, so this pins that nothing it serves, an ordinary page, an error page or
/// a download, ever gets a public cache header from it: an agent's page must never be kept by a shared cache.
/// </summary>
public sealed class PublicCacheHeaderPinTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task No_admin_page_error_page_or_download_is_given_a_public_cache_header()
    {
        await using var factory = new AdminFactory();
        var id = Guid.NewGuid();
        factory.Api.On(HttpMethod.Get, $"/api/attachments/{id}", _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        using var anonymous = factory.CreateClient();
        using var signedIn = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        foreach (var path in new[] { "/not-found", "/error", "/no-such-page", "/health/live" })
        {
            using var response = await anonymous.GetAsync(path, Ct);
            response.Headers.CacheControl?.Public.ShouldNotBe(true, path);
        }

        using var download = await signedIn.GetAsync($"/attachments/{id}", Ct);
        download.Headers.CacheControl!.NoStore.ShouldBeTrue();
        download.Headers.CacheControl.Public.ShouldNotBe(true);
    }
}
```

`tests/TechStrap.Api.Tests/Hosting/PathHeaderRuleHostTests.cs`

```diff
@@ -201,6 +201,126 @@ public sealed class PathHeaderRuleHostTests
         Policy(other).ShouldNotContain("sandbox");
     }
 
+    private static bool UnderKb(PathString path) => path.StartsWithSegments("/kb");
+
+    private static void SuccessPipeline(WebApplication app, params PathHeaderRule[] rules)
+    {
+        app.UseTechStrapWebHost(rules);
+        app.UseTechStrapErrorPages();
+        app.MapGet("/kb/page", (HttpContext context) =>
+        {
+            // What a page might set for itself: the rule must win over it on a delivered page.
+            context.Response.Headers.CacheControl = "private";
+            return "page";
+        });
+        app.MapGet("/kb/gone", (HttpContext context) =>
+        {
+            context.Response.Headers.CacheControl = "no-store";
+            return Results.NotFound();
+        });
+        app.MapGet("/kb/busy", () => Results.StatusCode(StatusCodes.Status429TooManyRequests));
+        app.MapGet("/kb/down", () => Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
+        app.MapGet("/kb/moved", () => Results.Redirect("/kb/page"));
+        app.MapGet("/kb/created", () => Results.StatusCode(StatusCodes.Status201Created));
+        app.MapGet("/other", () => "other");
+        app.MapGet("/not-found", () => "the not-found page");
+    }
+
+    [Theory]
+    [InlineData("/kb/page", HttpStatusCode.OK, true)]
+    [InlineData("/kb/created", HttpStatusCode.Created, true)]
+    [InlineData("/kb/gone", HttpStatusCode.NotFound, false)]
+    [InlineData("/kb/busy", HttpStatusCode.TooManyRequests, false)]
+    [InlineData("/kb/down", HttpStatusCode.ServiceUnavailable, false)]
+    [InlineData("/kb/moved", HttpStatusCode.Redirect, false)]
+    [InlineData("/other", HttpStatusCode.OK, false)]
+    public async Task A_success_only_rule_sets_its_header_on_a_2xx_answer_for_a_matching_path_and_on_nothing_else(string path, HttpStatusCode status, bool applied)
+    {
+        await using var app = await StartAsync(a => SuccessPipeline(a, PathHeaderRule.SetOnSuccess(UnderKb, ("Cache-Control", "public, max-age=60"), ("X-Probe", "kb"))));
+        using var client = app.GetTestClient();
+
+        using var response = await client.GetAsync(path, Ct);
+
+        response.StatusCode.ShouldBe(status);
+        Header(response, "X-Probe").ShouldBe(applied ? ["kb"] : []);
+        if (applied)
+        {
+            Header(response, "Cache-Control").ShouldBe(["public, max-age=60"], "the rule wins over the value the endpoint set itself");
+        }
+        else
+        {
+            Header(response, "Cache-Control").ShouldNotContain("public, max-age=60");
+        }
+    }
+
+    [Fact]
+    public async Task A_success_only_rule_leaves_what_an_error_answer_set_for_itself_and_the_page_that_replaces_a_404_carries_no_public_header()
+    {
+        await using var app = await StartAsync(a => SuccessPipeline(a, PathHeaderRule.SetOnSuccess(UnderKb, ("Cache-Control", "public, max-age=60"))));
+        using var client = app.GetTestClient();
+
+        using var gone = await client.GetAsync("/kb/gone", Ct);
+
+        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
+        (await gone.Content.ReadAsStringAsync(Ct)).ShouldBe("the not-found page");
+        Header(gone, "Cache-Control").ShouldBe(["no-store"]);
+    }
+
+    [Fact]
+    public async Task A_success_only_rule_and_an_every_status_rule_apply_in_the_order_given()
+    {
+        await using var app = await StartAsync(a => SuccessPipeline(
+            a,
+            PathHeaderRule.Set(UnderKb, ("X-Robots-Tag", "noindex"), ("Cache-Control", "no-store")),
+            PathHeaderRule.SetOnSuccess(UnderKb, ("Cache-Control", "public, max-age=60"))));
+        using var client = app.GetTestClient();
+
+        using var page = await client.GetAsync("/kb/page", Ct);
+        using var gone = await client.GetAsync("/kb/gone", Ct);
+
+        Header(page, "Cache-Control").ShouldBe(["public, max-age=60"]);
+        Header(page, "X-Robots-Tag").ShouldBe(["noindex"]);
+        Header(gone, "Cache-Control").ShouldBe(["no-store"]);
+        Header(gone, "X-Robots-Tag").ShouldBe(["noindex"]);
+    }
+
+    // The Admin calls UseTechStrapWebHost with a download prefix and no rule. Adding SetOnSuccess must change nothing for it: no header on an ordinary page or an error, and a sandbox on a delivered download only.
+    [Fact]
+    public async Task The_admins_signature_with_only_a_download_prefix_sets_no_cache_header_and_sandboxes_a_delivered_download_only()
+    {
+        await using var app = await StartAsync(a =>
+        {
+            a.UseTechStrapWebHost("/attachments");
+            a.UseTechStrapErrorPages();
+            a.MapGet("/attachments/{id}", (string id) => id == "missing" ? Results.NotFound() : Results.Text("file"));
+            a.MapGet("/queue", () => "page");
+            a.MapGet("/not-found", () => "the not-found page");
+        });
+        using var client = app.GetTestClient();
+
+        using var file = await client.GetAsync("/attachments/1", Ct);
+        using var missing = await client.GetAsync("/attachments/missing", Ct);
+        using var page = await client.GetAsync("/queue", Ct);
+
+        Policy(file).Count(d => d == "sandbox").ShouldBe(1);
+        Policy(missing).ShouldNotContain("sandbox");
+        Policy(page).ShouldNotContain("sandbox");
+        foreach (var response in new[] { file, missing, page })
+        {
+            Header(response, "Cache-Control").ShouldBeEmpty();
+            Header(response, "X-Robots-Tag").ShouldBeEmpty();
+            Header(response, "Referrer-Policy").ShouldBe(["strict-origin-when-cross-origin"]);
+        }
+    }
+
+    [Fact]
+    public void A_success_only_rule_needs_a_predicate_and_at_least_one_named_header()
+    {
+        Should.Throw<ArgumentNullException>(() => PathHeaderRule.SetOnSuccess(null!, ("A", "b")));
+        Should.Throw<ArgumentException>(() => PathHeaderRule.SetOnSuccess(UnderKb));
+        Should.Throw<ArgumentException>(() => PathHeaderRule.SetOnSuccess(UnderKb, (" ", "b")));
+    }
+
     [Fact]
     public void A_rule_needs_a_predicate_and_at_least_one_header()
     {
```

`tests/TechStrap.Portal.Tests/Api/ApiHarness.cs`

```diff
@@ -3,6 +3,7 @@ using Microsoft.AspNetCore.Http;
 using Microsoft.Extensions.DependencyInjection;
 using Microsoft.Extensions.Options;
 using TechStrap.Portal.Clients;
+using TechStrap.Portal.Seo;
 using TechStrap.Portal.Settings;
 
 namespace TechStrap.Portal.Tests.Api;
@@ -29,7 +30,7 @@ internal sealed class ApiHarness : IDisposable
 
     public IServiceProvider Services => _scope.ServiceProvider;
 
-    public static ApiHarness Create(string? clientIp = DefaultClientIp, TimeProvider? time = null)
+    public static ApiHarness Create(string? clientIp = DefaultClientIp, TimeProvider? time = null, Action<IServiceCollection>? configure = null)
     {
         var stub = new StubApiHandler();
         var services = new ServiceCollection();
@@ -42,7 +43,9 @@ internal sealed class ApiHarness : IDisposable
 
         services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new PortalOptions { ApiBaseUrl = "http://api.test/", PublicUrl = "https://portal.test" }));
         services.AddPortalApiClients();
+        services.AddPortalSitemap();
         services.AddStubApi(stub);
+        configure?.Invoke(services);
 
         var provider = services.BuildServiceProvider();
         var harness = new ApiHarness(provider, stub);
```

`tests/TechStrap.Portal.Tests/Caching/OutputCachePipelineTests.cs` (new)

```csharp
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Hosting.Wiring;
using TechStrap.Portal.Caching;
using TechStrap.Portal.Headers;

namespace TechStrap.Portal.Tests.Caching;

/// <summary>
/// PHASE-09c Review Focus 3 (cache safety), the mechanism: a small host (TestServer) with the real wiring in the order <c>Program.cs</c> uses (the shared headers and the Portal's header rules, the error pages, then the output
/// cache) and a counting endpoint at each path. It proves what the cache keeps, what it never keeps and that a hit still carries the request's own headers, without depending on a page; the real pages are pinned in
/// <c>KbPageCacheHostTests</c>. The spike (see the plan) found that a stored copy replays the first request's <c>X-Correlation-Id</c>; <see cref="PortalOutputCache.UsePortalOutputCache"/> sets the right one again.
/// </summary>
public sealed class OutputCachePipelineTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed class Counter
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public int Next() => Interlocked.Increment(ref _calls);
    }

    private static async Task<WebApplication> StartAsync(Counter counter)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Services.AddTechStrapWebHost(builder.Configuration, "default-src 'self'; frame-ancestors 'none'");
        builder.Services.AddPortalOutputCache();
        var app = builder.Build();
        app.UseTechStrapWebHost(PortalHeaderRules.Rules);
        app.UseTechStrapErrorPages();
        app.UsePortalOutputCache();
        app.Map("/{**path}", (HttpContext context) =>
        {
            var call = counter.Next();
            var path = context.Request.Path.Value!;
            context.Response.Headers["X-Probe-Call"] = call.ToString();
            if (path.EndsWith("/gone", StringComparison.Ordinal))
            {
                return Results.NotFound($"gone {call}");
            }

            if (path.EndsWith("/busy", StringComparison.Ordinal))
            {
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            if (path.EndsWith("/down", StringComparison.Ordinal))
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            if (path.EndsWith("/cookie", StringComparison.Ordinal))
            {
                context.Response.Cookies.Append("probe", "1");
            }

            return Results.Text($"page {call}", "text/html");
        });
        await app.StartAsync(Ct);
        return app;
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? host = null, string? correlationId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (host is not null)
        {
            request.Headers.Host = host;
        }

        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-Id", correlationId);
        }

        return await client.SendAsync(request, Ct);
    }

    private static string[] Header(HttpResponseMessage response, string name) => response.Headers.TryGetValues(name, out var values) ? [.. values] : [];

    [Theory]
    [InlineData("/p/paperplane/kb")]
    [InlineData("/p/paperplane/kb/accounts")]
    [InlineData("/p/paperplane/kb/accounts/reset-password")]
    public async Task A_kb_page_asked_twice_is_made_once_and_the_second_answer_is_the_stored_one(string path)
    {
        var counter = new Counter();
        await using var app = await StartAsync(counter);
        using var client = app.GetTestClient();

        using var first = await GetAsync(client, path);
        using var second = await GetAsync(client, path);

        counter.Calls.ShouldBe(1);
        (await first.Content.ReadAsStringAsync(Ct)).ShouldBe("page 1");
        (await second.Content.ReadAsStringAsync(Ct)).ShouldBe("page 1");
        Header(first, "Age").ShouldBeEmpty();
        Header(second, "Age").ShouldNotBeEmpty("the second answer came from the cache");
    }

    [Fact]
    public async Task A_cached_answer_carries_the_security_headers_the_browser_rule_and_the_requests_own_correlation_id()
    {
        var counter = new Counter();
        await using var app = await StartAsync(counter);
        using var client = app.GetTestClient();

        using var first = await GetAsync(client, "/p/paperplane/kb", correlationId: "cid-one");
        using var second = await GetAsync(client, "/p/paperplane/kb", correlationId: "cid-two");
        using var third = await GetAsync(client, "/p/paperplane/kb");

        counter.Calls.ShouldBe(1);
        Header(first, "X-Correlation-Id").ShouldBe(["cid-one"]);
        Header(second, "X-Correlation-Id").ShouldBe(["cid-two"], "a stored copy replays the first request's id unless it is set again");
        Header(third, "X-Correlation-Id").ShouldHaveSingleItem().ShouldNotBe("cid-one");
        foreach (var response in new[] { first, second, third })
        {
            Header(response, "Content-Security-Policy").ShouldHaveSingleItem().ShouldContain("default-src 'self'");
            Header(response, "X-Content-Type-Options").ShouldBe(["nosniff"]);
            Header(response, "Referrer-Policy").ShouldBe(["strict-origin-when-cross-origin"]);
            Header(response, "X-Frame-Options").ShouldBe(["DENY"]);
            response.Headers.CacheControl!.ToString().ShouldBe(PortalCachePaths.BrowserCacheControl);
            Header(response, "Set-Cookie").ShouldBeEmpty();
        }
    }

    [Theory]
    [InlineData("/p/paperplane/kb/gone", HttpStatusCode.NotFound)]
    [InlineData("/p/paperplane/kb/busy", HttpStatusCode.TooManyRequests)]
    [InlineData("/p/paperplane/kb/down", HttpStatusCode.ServiceUnavailable)]
    public async Task A_404_a_429_and_a_503_are_never_stored_and_never_get_a_public_header(string path, HttpStatusCode status)
    {
        var counter = new Counter();
        await using var app = await StartAsync(counter);
        using var client = app.GetTestClient();

        using var first = await GetAsync(client, path);
        using var second = await GetAsync(client, path);

        first.StatusCode.ShouldBe(status);
        second.StatusCode.ShouldBe(status);
        counter.Calls.ShouldBe(2, "an error answer is made again every time");
        first.Headers.CacheControl?.Public.ShouldNotBe(true);
        second.Headers.CacheControl?.Public.ShouldNotBe(true);
        Header(second, "Age").ShouldBeEmpty();
    }

    [Fact]
    public async Task An_answer_that_sets_a_cookie_is_never_stored()
    {
        var counter = new Counter();
        await using var app = await StartAsync(counter);
        using var client = app.GetTestClient();

        using var first = await GetAsync(client, "/p/paperplane/kb/cookie");
        using var second = await GetAsync(client, "/p/paperplane/kb/cookie");

        counter.Calls.ShouldBe(2);
        Header(first, "Set-Cookie").ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("/p/paperplane/kb/search?q=router")]
    [InlineData("/p/paperplane/kb/search")]
    [InlineData("/p/paperplane/contact")]
    [InlineData("/p/paperplane/contact/received")]
    [InlineData("/p/paperplane/lost-link")]
    [InlineData("/p/paperplane/suggest?q=a")]
    [InlineData("/p/paperplane")]
    [InlineData("/t/abc")]
    [InlineData("/t/abc/attachments/11111111-2222-3333-4444-555555555555")]
    [InlineData("/not-found")]
    [InlineData("/sitemap.xml")]
    [InlineData("/robots.txt")]
    public async Task The_search_page_the_forms_the_suggest_adapter_tickets_and_every_other_page_are_never_kept(string path)
    {
        var counter = new Counter();
        await using var app = await StartAsync(counter);
        using var client = app.GetTestClient();

        using var first = await GetAsync(client, path);
        using var second = await GetAsync(client, path);

        counter.Calls.ShouldBe(2, path);
        Header(second, "Age").ShouldBeEmpty();
        first.Headers.CacheControl?.Public.ShouldNotBe(true, path);
    }

    [Fact]
    public async Task The_search_page_and_the_ticket_and_form_pages_tell_the_browser_not_to_keep_them_either()
    {
        var counter = new Counter();
        await using var app = await StartAsync(counter);
        using var client = app.GetTestClient();

        foreach (var path in new[] { "/p/paperplane/kb/search?q=x", "/t/abc", "/p/paperplane/contact", "/p/paperplane/suggest" })
        {
            using var response = await GetAsync(client, path);
            response.Headers.CacheControl!.NoStore.ShouldBeTrue(path);
        }
    }

    [Fact]
    public async Task Only_the_page_value_changes_the_key_other_query_values_and_the_host_do_not()
    {
        var counter = new Counter();
        await using var app = await StartAsync(counter);
        using var client = app.GetTestClient();

        (await (await GetAsync(client, "/p/paperplane/kb/accounts")).Content.ReadAsStringAsync(Ct)).ShouldBe("page 1");
        (await (await GetAsync(client, "/p/paperplane/kb/accounts?utm=1")).Content.ReadAsStringAsync(Ct)).ShouldBe("page 1");
        (await (await GetAsync(client, "/p/paperplane/kb/accounts?utm=2&other=x")).Content.ReadAsStringAsync(Ct)).ShouldBe("page 1");
        (await (await GetAsync(client, "/p/paperplane/kb/accounts", host: "other.example")).Content.ReadAsStringAsync(Ct)).ShouldBe("page 1");
        counter.Calls.ShouldBe(1, "query values other than page and the host are not part of the key");

        (await (await GetAsync(client, "/p/paperplane/kb/accounts?page=2")).Content.ReadAsStringAsync(Ct)).ShouldBe("page 2");
        (await (await GetAsync(client, "/p/paperplane/kb/accounts?page=2&utm=1")).Content.ReadAsStringAsync(Ct)).ShouldBe("page 2");
        (await (await GetAsync(client, "/p/paperplane/kb/accounts?page=3")).Content.ReadAsStringAsync(Ct)).ShouldBe("page 3");
        counter.Calls.ShouldBe(3, "each page number is its own key");
    }

    [Fact]
    public async Task A_cache_key_is_never_shared_between_products_categories_or_articles()
    {
        var counter = new Counter();
        await using var app = await StartAsync(counter);
        using var client = app.GetTestClient();

        var bodies = new List<string>();
        foreach (var path in new[] { "/p/acme/kb", "/p/orbitly/kb", "/p/acme/kb/accounts", "/p/acme/kb/billing", "/p/acme/kb/accounts/reset", "/p/orbitly/kb/accounts/reset" })
        {
            bodies.Add(await (await GetAsync(client, path)).Content.ReadAsStringAsync(Ct));
        }

        counter.Calls.ShouldBe(6);
        bodies.Distinct().Count().ShouldBe(6, "six different addresses, six different stored pages");
    }

    [Theory]
    [InlineData("?page=abc")]
    [InlineData("?page=0")]
    [InlineData("?page=99999999999")]
    [InlineData("?page=1&page=2")]
    public async Task A_page_value_that_is_not_a_plain_page_number_is_answered_but_never_stored(string query)
    {
        var counter = new Counter();
        await using var app = await StartAsync(counter);
        using var client = app.GetTestClient();

        using var first = await GetAsync(client, "/p/paperplane/kb/accounts" + query);
        using var second = await GetAsync(client, "/p/paperplane/kb/accounts" + query);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        counter.Calls.ShouldBe(2, query);
    }

    [Fact]
    public async Task A_post_is_never_stored()
    {
        var counter = new Counter();
        await using var app = await StartAsync(counter);
        using var client = app.GetTestClient();

        using var first = await client.PostAsync("/p/paperplane/kb/accounts", new StringContent("x"), Ct);
        using var second = await client.PostAsync("/p/paperplane/kb/accounts", new StringContent("x"), Ct);

        counter.Calls.ShouldBe(2);
    }
}
```

`tests/TechStrap.Portal.Tests/Caching/PortalCachePathsTests.cs` (new)

```csharp
using Microsoft.AspNetCore.Http;
using TechStrap.Portal.Caching;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Tests.Caching;

/// <summary>
/// PHASE-09c Review Focus 3 (cache safety): the one predicate that decides what the Portal keeps. Only the three kinds of help-center page; never the search page, the form pages, the suggest adapter, <c>/t/*</c> or a
/// request whose <c>page</c> value could fill the store.
/// </summary>
public sealed class PortalCachePathsTests
{
    [Theory]
    [InlineData("/p/paperplane/kb")]
    [InlineData("/p/paperplane/kb/")]
    [InlineData("/P/Paperplane/KB")]
    [InlineData("/p/paperplane/kb/accounts")]
    [InlineData("/p/paperplane/kb/accounts/")]
    [InlineData("/p/paperplane/kb/accounts/reset-password")]
    [InlineData("/p/paperplane/KB/Accounts/Reset-Password/")]
    [InlineData("/p//paperplane/kb")]
    [InlineData("/p/paperplane/kb/searching")]
    [InlineData("/p/paperplane/kb/accounts/search")]
    public void The_help_centre_home_a_category_and_an_article_are_kb_pages(string path) => PortalCachePaths.IsKbPage(path).ShouldBeTrue(path);

    [Theory]
    [InlineData("/")]
    [InlineData("/p")]
    [InlineData("/p/paperplane")]
    [InlineData("/p/paperplane/kb/search")]
    [InlineData("/p/paperplane/kb/SEARCH")]
    [InlineData("/p/paperplane/kb/search/")]
    [InlineData("/p/paperplane/kb/search/anything")]
    [InlineData("/p/paperplane/kb/accounts/reset-password/extra")]
    [InlineData("/p/paperplane/contact")]
    [InlineData("/p/paperplane/contact/received")]
    [InlineData("/p/paperplane/lost-link")]
    [InlineData("/p/paperplane/suggest")]
    [InlineData("/p/paperplane/kbx")]
    [InlineData("/p/kb")]
    [InlineData("/t/x")]
    [InlineData("/t/kb/accounts")]
    [InlineData("/kb")]
    [InlineData("/pp/paperplane/kb")]
    [InlineData("/not-found")]
    [InlineData("/error")]
    [InlineData("/robots.txt")]
    [InlineData("/sitemap.xml")]
    [InlineData("")]
    public void The_product_home_the_search_page_the_forms_tickets_and_every_other_path_are_not(string path) => PortalCachePaths.IsKbPage(path).ShouldBeFalse(path);

    [Theory]
    [InlineData("/p/paperplane/kb/search")]
    [InlineData("/P/Paperplane/KB/Search/")]
    public void Exactly_the_search_page_is_the_search_path(string path) => PortalCachePaths.IsKbSearchPath(path).ShouldBeTrue(path);

    [Theory]
    [InlineData("/p/paperplane/kb")]
    [InlineData("/p/paperplane/kb/search/more")]
    [InlineData("/p/paperplane/kb/accounts")]
    [InlineData("/p/paperplane/search")]
    [InlineData("/t/kb/search")]
    public void Nothing_else_is_the_search_path(string path) => PortalCachePaths.IsKbSearchPath(path).ShouldBeFalse(path);

    private static bool Cacheable(string path, string query = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        return PortalCachePaths.IsCacheable(context.Request);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?")]
    [InlineData("?page=1")]
    [InlineData("?page=2")]
    [InlineData("?page=9999")]
    [InlineData("?PAGE=7")]
    [InlineData("?utm_source=mail&utm_campaign=x")]
    [InlineData("?page=3&utm=1")]
    public void A_kb_page_with_no_page_value_or_a_plain_page_number_is_kept(string query) => Cacheable("/p/paperplane/kb/accounts", query).ShouldBeTrue(query);

    [Theory]
    [InlineData("?page=")]
    [InlineData("?page=abc")]
    [InlineData("?page=0")]
    [InlineData("?page=01")]
    [InlineData("?page=-1")]
    [InlineData("?page=+1")]
    [InlineData("?page=10000")]
    [InlineData("?page=99999999999")]
    [InlineData("?page=1.5")]
    [InlineData("?page=1%200")]
    [InlineData("?page=1&page=2")]
    [InlineData("?page=1&PAGE=1")]
    [InlineData("?page")]
    public void A_page_value_that_is_not_one_to_four_digits_is_never_kept_so_it_cannot_fill_the_store(string query) => Cacheable("/p/paperplane/kb/accounts", query).ShouldBeFalse(query);

    [Theory]
    [InlineData("/p/paperplane/kb/search", "?q=router")]
    [InlineData("/p/paperplane/contact", "")]
    [InlineData("/p/paperplane/suggest", "?q=a")]
    [InlineData("/t/abc", "")]
    [InlineData("/p/paperplane", "")]
    public void A_path_that_is_not_a_kb_page_is_never_kept_whatever_its_query(string path, string query) => Cacheable(path, query).ShouldBeFalse(path + query);

    [Fact]
    public void The_server_and_the_browser_keep_a_page_for_the_same_minute()
    {
        PortalCachePaths.Lifetime.ShouldBe(TimeSpan.FromSeconds(60));
        PortalCachePaths.BrowserCacheControl.ShouldBe($"public, max-age={(int)PortalCachePaths.Lifetime.TotalSeconds}");
    }

    [Fact]
    public void The_segments_are_the_ones_the_route_templates_use()
    {
        PortalRoutes.KbHomeTemplate.ShouldEndWith("/" + PortalRoutes.KbSegment);
        PortalRoutes.KbSearchTemplate.ShouldEndWith("/" + PortalRoutes.KbSegment + "/" + PortalRoutes.KbSearchSegment);
        PortalRoutes.KbSearchSegment.ShouldBe(TechStrap.Contracts.Kb.KbLimits.ReservedCategorySlug, "the API reserves the slug the search page uses, so no category can shadow it");
    }
}
```

`tests/TechStrap.Portal.Tests/Caching/ProgramOrderTests.cs` (new)

```csharp
using TechStrap.Tests.Shared;

namespace TechStrap.Portal.Tests.Caching;

/// <summary>
/// The order of the Portal's pipeline is part of its safety (PHASE-09c Review Focus 3): the shared headers and the per-path rules before the cache (so a cached answer is sent with the request's own headers), the error pages
/// before the cache (so a 404 is re-executed at <c>/not-found</c>, which is never kept), and the cache before the endpoints (so it can answer instead of them). This pins the lines of <c>Program.cs</c> in that order.
/// </summary>
public sealed class ProgramOrderTests
{
    private static int Index(string program, string call)
    {
        var index = program.IndexOf(call, StringComparison.Ordinal);
        index.ShouldBeGreaterThanOrEqualTo(0, $"Program.cs must call {call}");
        return index;
    }

    [Fact]
    public void The_output_cache_runs_after_the_headers_and_the_error_pages_and_before_the_form_limit_and_the_endpoints()
    {
        var program = File.ReadAllText(RepositoryRoot.Combine("src", "TechStrap.Portal", "Program.cs"));

        var order = new[]
        {
            Index(program, "app.UseTechStrapWebHost(PortalHeaderRules.Rules);"),
            Index(program, "app.UsePortalSeo();"),
            Index(program, "app.UseTechStrapErrorPages();"),
            Index(program, "app.UsePortalOutputCache();"),
            Index(program, "app.UseMiddleware<RequestTooLargeMiddleware>();"),
            Index(program, "app.UseAntiforgery();"),
            Index(program, "app.MapPortalSeo();"),
            Index(program, "app.MapRazorComponentsWithStaticAssets<App>();"),
        };

        order.ShouldBe(order.Order());
        program.Split("UsePortalOutputCache", StringSplitOptions.None).Length.ShouldBe(2, "the cache is added once");
        program.ShouldContain("builder.Services.AddPortalOutputCache();");
    }
}
```

`tests/TechStrap.Portal.Tests/Clients/PublicKbClientTests.cs`

```diff
@@ -75,6 +75,173 @@ public sealed class PublicKbClientTests
         api.Stub.Count(HttpMethod.Get, Path).ShouldBe(1 + 1 + ApiClientRegistration.ReadRetryCount);
     }
 
+    private static readonly DateTimeOffset Updated = new(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);
+
+    [Fact]
+    public async Task A_search_for_a_page_sends_the_page_and_the_old_overload_still_sends_none()
+    {
+        using var api = ApiHarness.Create();
+        api.Stub.OnJson(HttpMethod.Get, Path, Page());
+
+        await api.Get<IPublicKbClient>().SearchAsync("paperplane", "reset", 3, 10, Ct);
+        await api.Get<IPublicKbClient>().SearchAsync("paperplane", "reset", 10, Ct);
+
+        api.Stub.Requests.Select(request => request.Query).ShouldBe(["?q=reset&page=3&pageSize=10", "?q=reset&pageSize=10"]);
+        api.Stub.Requests.ShouldAllBe(request => request.Client == ApiClientNames.Read && request.TicketToken == null);
+        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
+    }
+
+    [Fact]
+    public async Task The_categories_are_a_read_of_the_categories_route_and_keep_their_counts()
+    {
+        using var api = ApiHarness.Create();
+        PublicKbCategoryDto[] categories = [new("accounts", "Accounts", "Sign-in and passwords", 4), new("billing", "Billing", null, 1)];
+        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/paperplane/categories", categories);
+
+        var result = await api.Get<IPublicKbClient>().ListCategoriesAsync("paperplane", Ct);
+
+        result.Value.ShouldBe(categories);
+        var sent = api.Stub.Requests.ShouldHaveSingleItem();
+        sent.Client.ShouldBe(ApiClientNames.Read);
+        sent.Query.ShouldBeEmpty();
+        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
+    }
+
+    [Fact]
+    public async Task A_category_page_is_a_read_with_the_page_and_the_page_size_in_the_query()
+    {
+        using var api = ApiHarness.Create();
+        var page = new PagedResponse<PublicKbArticleSummaryDto>([new("reset-password", "Reset your password", "How to reset", "accounts", "Accounts", "paperplane", Updated)], 2, 10, 11);
+        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/paperplane/categories/accounts/articles", page);
+
+        var result = await api.Get<IPublicKbClient>().ListCategoryArticlesAsync("paperplane", "accounts", 2, 10, Ct);
+
+        result.Value.Items.ShouldBe(page.Items);
+        (result.Value.Page, result.Value.PageSize, result.Value.TotalCount).ShouldBe((2, 10, 11));
+        var sent = api.Stub.Requests.ShouldHaveSingleItem();
+        sent.Query.ShouldBe("?page=2&pageSize=10");
+        sent.Client.ShouldBe(ApiClientNames.Read);
+        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
+    }
+
+    [Fact]
+    public async Task An_article_is_a_read_of_its_own_route_and_its_html_comes_back_untouched()
+    {
+        using var api = ApiHarness.Create();
+        var article = new PublishedKbArticleDto("paperplane", "accounts", "Accounts", "reset-password", "Reset your password", "How to reset", "<h2>Steps</h2>\n<p>Open <a href=\"https://x.test\">settings</a>.</p>", Updated, Updated);
+        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/paperplane/articles/accounts/reset-password", article);
+
+        var result = await api.Get<IPublicKbClient>().GetArticleAsync("paperplane", "accounts", "reset-password", Ct);
+
+        result.Value.ShouldBe(article);
+        result.Value.Html.ShouldBe(article.Html, "the Portal never rewrites what the API sanitized");
+        api.Stub.Requests.ShouldHaveSingleItem().Client.ShouldBe(ApiClientNames.Read);
+        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
+    }
+
+    [Fact]
+    public async Task The_sitemap_entries_are_a_read_and_a_shared_article_has_no_product_key()
+    {
+        using var api = ApiHarness.Create();
+        KbSitemapEntryDto[] entries = [new(null, "general", "shared-tips", Updated), new("paperplane", "accounts", "reset-password", Updated)];
+        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/paperplane/sitemap", entries);
+
+        var result = await api.Get<IPublicKbClient>().GetSitemapAsync("paperplane", Ct);
+
+        result.Value.ShouldBe(entries);
+        api.Stub.Requests.ShouldHaveSingleItem().Client.ShouldBe(ApiClientNames.Read);
+    }
+
+    [Theory]
+    [InlineData(null, "accounts", "reset")]
+    [InlineData("", "accounts", "reset")]
+    [InlineData("Paperplane", "accounts", "reset")]
+    [InlineData("../admin", "accounts", "reset")]
+    [InlineData("paperplane", null, "reset")]
+    [InlineData("paperplane", "", "reset")]
+    [InlineData("paperplane", "Accounts", "reset")]
+    [InlineData("paperplane", "a/b", "reset")]
+    [InlineData("paperplane", "a?b", "reset")]
+    [InlineData("paperplane", "a#b", "reset")]
+    [InlineData("paperplane", "..", "reset")]
+    [InlineData("paperplane", "accounts", null)]
+    [InlineData("paperplane", "accounts", "")]
+    [InlineData("paperplane", "accounts", "Reset")]
+    [InlineData("paperplane", "accounts", "re set")]
+    [InlineData("paperplane", "accounts", "re%2fset")]
+    [InlineData("paperplane", "accounts", "reset-")]
+    [InlineData("paperplane", "accounts", "-reset")]
+    [InlineData("paperplane", "accounts", "re--set")]
+    public async Task An_article_with_a_key_or_slug_that_is_not_a_slug_is_the_uniform_not_found_and_no_call_is_made(string? product, string? category, string? slug)
+    {
+        using var api = ApiHarness.Create();
+
+        var article = await api.Get<IPublicKbClient>().GetArticleAsync(product!, category!, slug!, Ct);
+
+        article.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound));
+        api.Stub.Requests.ShouldBeEmpty();
+    }
+
+    [Theory]
+    [InlineData(null, "accounts")]
+    [InlineData("", "accounts")]
+    [InlineData("Paperplane", "accounts")]
+    [InlineData("paperplane", null)]
+    [InlineData("paperplane", "")]
+    [InlineData("paperplane", "Accounts")]
+    [InlineData("paperplane", "a/b")]
+    [InlineData("paperplane", "a?page=9")]
+    [InlineData("paperplane", "a#b")]
+    [InlineData("paperplane", "..")]
+    [InlineData("paperplane", "accounts-")]
+    public async Task A_category_page_with_a_key_or_slug_that_is_not_a_slug_is_the_uniform_not_found_and_no_call_is_made(string? product, string? category)
+    {
+        using var api = ApiHarness.Create();
+
+        var page = await api.Get<IPublicKbClient>().ListCategoryArticlesAsync(product!, category!, 1, 10, Ct);
+
+        page.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound));
+        api.Stub.Requests.ShouldBeEmpty();
+    }
+
+    [Theory]
+    [InlineData(null)]
+    [InlineData("")]
+    [InlineData("Paperplane")]
+    [InlineData("../admin")]
+    [InlineData("paperplane/kb")]
+    public async Task The_categories_the_sitemap_and_a_paged_search_refuse_a_key_that_is_not_a_slug_without_a_call(string? product)
+    {
+        using var api = ApiHarness.Create();
+        var expected = new ResultError(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound);
+
+        (await api.Get<IPublicKbClient>().ListCategoriesAsync(product!, Ct)).Errors.ShouldHaveSingleItem().ShouldBe(expected);
+        (await api.Get<IPublicKbClient>().GetSitemapAsync(product!, Ct)).Errors.ShouldHaveSingleItem().ShouldBe(expected);
+        (await api.Get<IPublicKbClient>().SearchAsync(product!, "x", 1, 10, Ct)).Errors.ShouldHaveSingleItem().ShouldBe(expected);
+        api.Stub.Requests.ShouldBeEmpty();
+    }
+
+    [Fact]
+    public async Task A_404_is_the_uniform_not_found_a_429_is_rate_limited_and_a_503_is_retried_then_unavailable_on_every_new_read()
+    {
+        using var api = ApiHarness.Create();
+        api.Stub.OnProblem(HttpMethod.Get, "/api/public/kb/paperplane/articles/accounts/gone", HttpStatusCode.NotFound, "kb-article-not-found", "That article does not exist.");
+        api.Stub.OnProblem(HttpMethod.Get, "/api/public/kb/paperplane/categories/gone/articles", HttpStatusCode.NotFound, "kb-category-not-found", "That category does not exist.");
+        api.Stub.OnStatus(HttpMethod.Get, "/api/public/kb/paperplane/categories", HttpStatusCode.TooManyRequests);
+        api.Stub.OnStatus(HttpMethod.Get, "/api/public/kb/paperplane/sitemap", HttpStatusCode.ServiceUnavailable);
+
+        var article = await api.Get<IPublicKbClient>().GetArticleAsync("paperplane", "accounts", "gone", Ct);
+        var category = await api.Get<IPublicKbClient>().ListCategoryArticlesAsync("paperplane", "gone", 1, 10, Ct);
+        var limited = await api.Get<IPublicKbClient>().ListCategoriesAsync("paperplane", Ct);
+        var down = await api.Get<IPublicKbClient>().GetSitemapAsync("paperplane", Ct);
+
+        article.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound));
+        category.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound));
+        limited.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.RateLimited);
+        down.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
+        api.Stub.Count(HttpMethod.Get, "/api/public/kb/paperplane/sitemap").ShouldBe(1 + ApiClientRegistration.ReadRetryCount);
+    }
+
     private sealed class PagedComparer : IEqualityComparer<PagedResponse<PublicKbSearchResultDto>>
     {
         public bool Equals(PagedResponse<PublicKbSearchResultDto>? x, PagedResponse<PublicKbSearchResultDto>? y) =>
```

`tests/TechStrap.Portal.Tests/Clients/PublicProductClientTests.cs`

```diff
@@ -25,6 +25,35 @@ public sealed class PublicProductClientTests
         api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
     }
 
+    [Fact]
+    public async Task The_product_list_is_a_read_of_the_list_route_with_the_key_and_display_name_only()
+    {
+        using var api = ApiHarness.Create();
+        PublicProductSummaryDto[] list = [new("acme", "Acme"), new("paperplane", "Paperplane")];
+        api.Stub.OnJson(HttpMethod.Get, "/api/public/products", list);
+
+        var result = await api.Get<IPublicProductClient>().ListAsync(Ct);
+
+        result.Value.ShouldBe(list);
+        var sent = api.Stub.Requests.ShouldHaveSingleItem();
+        sent.Client.ShouldBe(ApiClientNames.Read);
+        sent.Path.ShouldBe("/api/public/products");
+        sent.TicketToken.ShouldBeNull();
+        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
+    }
+
+    [Fact]
+    public async Task A_failing_product_list_is_unavailable_after_the_read_retries()
+    {
+        using var api = ApiHarness.Create();
+        api.Stub.OnStatus(HttpMethod.Get, "/api/public/products", HttpStatusCode.ServiceUnavailable);
+
+        var result = await api.Get<IPublicProductClient>().ListAsync(Ct);
+
+        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
+        api.Stub.Count(HttpMethod.Get, "/api/public/products").ShouldBe(1 + ApiClientRegistration.ReadRetryCount);
+    }
+
     [Fact]
     public async Task An_unknown_or_inactive_product_is_the_uniform_not_found_error()
     {
```

`tests/TechStrap.Portal.Tests/Headers/FormPageHeaderHostTests.cs`

```diff
@@ -36,7 +36,6 @@ public sealed class FormPageHeaderHostTests
     [Theory]
     [InlineData("/p/probe")]
     [InlineData("/p/probe/kb")]
-    [InlineData("/p/probe/kb/search?q=x")]
     [InlineData("/not-found")]
     [InlineData("/error")]
     public async Task Other_pages_are_not_marked_noindex_by_the_form_page_rule(string path)
@@ -50,4 +49,20 @@ public sealed class FormPageHeaderHostTests
         Header(response, "Cache-Control").ShouldNotContain("no-store", path);
         response.StatusCode.ShouldNotBe(HttpStatusCode.InternalServerError, path);
     }
+
+    // PHASE-09c: the help center's search page is never stored by a browser or a cache (any text can be asked and shown), but it is not a form page: it is not marked noindex by a header (the page does that itself when it has a query).
+    [Theory]
+    [InlineData("/p/probe/kb/search")]
+    [InlineData("/p/probe/kb/search?q=x")]
+    [InlineData("/P/Probe/KB/SEARCH/")]
+    public async Task The_search_page_is_never_stored_but_is_not_marked_noindex_by_a_header_rule(string path)
+    {
+        await using var factory = new PortalFactory();
+        using var client = factory.CreateClient();
+
+        using var response = await client.GetAsync(path, Ct);
+
+        Header(response, "Cache-Control").ShouldBe(["no-store"], path);
+        Header(response, "X-Robots-Tag").ShouldBeEmpty(path);
+    }
 }
```

`tests/TechStrap.Portal.Tests/Headers/PortalHeaderRulesTests.cs`

```diff
@@ -99,9 +99,15 @@ public sealed class PortalHeaderRulesTests
     }
 
     [Fact]
-    public void The_rules_are_the_ticket_headers_the_attachment_sandbox_and_the_form_page_headers_and_nothing_else()
+    public void The_rules_are_the_ticket_headers_the_attachment_sandbox_the_form_page_headers_and_the_two_help_centre_rules_and_nothing_else()
     {
-        PortalHeaderRules.Rules.Count.ShouldBe(3);
+        PortalHeaderRules.Rules.Count.ShouldBe(5);
+        PortalHeaderRules.Rules[3].Matches(new PathString("/p/x/kb/accounts")).ShouldBeTrue();
+        PortalHeaderRules.Rules[3].Matches(new PathString("/p/x/kb/search")).ShouldBeFalse();
+        PortalHeaderRules.Rules[3].Matches(new PathString("/p/x/contact")).ShouldBeFalse();
+        PortalHeaderRules.Rules[3].Matches(new PathString("/t/x")).ShouldBeFalse();
+        PortalHeaderRules.Rules[4].Matches(new PathString("/p/x/kb/search")).ShouldBeTrue();
+        PortalHeaderRules.Rules[4].Matches(new PathString("/p/x/kb")).ShouldBeFalse();
         PortalHeaderRules.Rules[2].Matches(new PathString("/p/x/contact")).ShouldBeTrue();
         PortalHeaderRules.Rules[2].Matches(new PathString("/p/x")).ShouldBeFalse();
         PortalHeaderRules.Rules[2].Matches(new PathString("/t/x")).ShouldBeFalse();
```

`tests/TechStrap.Portal.Tests/Headers/TicketHeaderHostTests.cs`

```diff
@@ -102,7 +102,6 @@ public sealed class TicketHeaderHostTests
     }
 
     [Theory]
-    [InlineData("/p/paperplane/kb/search", HttpStatusCode.NotFound)]
     [InlineData("/p/paperplane/kb/guides/dark-mode", HttpStatusCode.NotFound)]
     [InlineData("/no-such-page", HttpStatusCode.NotFound)]
     [InlineData("/not-found", HttpStatusCode.OK)]
```

`tests/TechStrap.Portal.Tests/Products/ProductPageBaseTests.cs`

```diff
@@ -24,6 +24,8 @@ public sealed class ProductPageBaseTests : BunitContext
             Calls.Add(key);
             return Task.FromResult(answer);
         }
+
+        public Task<Result<IReadOnlyList<PublicProductSummaryDto>>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException("a page never lists the products");
     }
 
     private sealed class Environment : IHostEnvironment
```

`tests/TechStrap.Portal.Tests/Routing/KbSlugShapeTests.cs` (new)

```csharp
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Tests.Routing;

/// <summary>The shape every category and article slug has (the Domain's slug rule, at most 80 characters). A slug that does not have it is never sent to the API.</summary>
public sealed class KbSlugShapeTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("accounts")]
    [InlineData("reset-password")]
    [InlineData("how-to-2fa-1")]
    [InlineData("1")]
    [InlineData("search")]
    public void A_slug_is_well_formed(string slug) => KbSlugShape.IsWellFormed(slug).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Accounts")]
    [InlineData("reset_password")]
    [InlineData("reset password")]
    [InlineData("reset/password")]
    [InlineData("-reset")]
    [InlineData("reset-")]
    [InlineData("reset--password")]
    [InlineData("reset\n")]
    [InlineData("..")]
    [InlineData("a?b")]
    [InlineData("a#b")]
    [InlineData("a%2Fb")]
    public void Anything_else_is_not(string? slug) => KbSlugShape.IsWellFormed(slug).ShouldBeFalse();

    [Fact]
    public void Eighty_characters_is_the_longest_and_it_is_the_domains_limit_for_a_kb_slug()
    {
        KbSlugShape.IsWellFormed(new string('a', KbSlugShape.MaxLength)).ShouldBeTrue();
        KbSlugShape.IsWellFormed(new string('a', KbSlugShape.MaxLength + 1)).ShouldBeFalse();
        KbSlugShape.MaxLength.ShouldBe(80);
    }

    [Fact]
    public void A_non_ascii_letter_or_digit_is_not_well_formed()
    {
        KbSlugShape.IsWellFormed("caf" + char.ConvertFromUtf32(0xE9)).ShouldBeFalse();
        KbSlugShape.IsWellFormed("guide" + char.ConvertFromUtf32(0x0663)).ShouldBeFalse();
    }
}
```

`tests/TechStrap.Portal.Tests/Routing/PortalRoutesTests.cs`

```diff
@@ -22,6 +22,17 @@ public sealed class PortalRoutesTests
         templates.Select(t => t.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(templates.Count);
     }
 
+    [Fact]
+    public void The_help_centre_segments_and_query_names_are_the_ones_the_cache_and_the_pages_share()
+    {
+        PortalRoutes.KbSegment.ShouldBe("kb");
+        PortalRoutes.KbSearchSegment.ShouldBe("search");
+        PortalRoutes.PageParameter.ShouldBe("page");
+        PortalRoutes.QueryParameter.ShouldBe("q");
+        PortalRoutes.KbHome("paperplane").ShouldBe("/p/paperplane/kb");
+        PortalRoutes.KbSearch("paperplane").ShouldBe("/p/paperplane/kb/search");
+    }
+
     [Fact]
     public void The_templates_match_the_routes_of_the_spec_exactly()
     {
```

`tests/TechStrap.Portal.Tests/Seo/PortalSitemapBuilderTests.cs` (new)

```csharp
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Seo;
using TechStrap.Portal.Settings;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Seo;

/// <summary>
/// PHASE-09c Review Focus 4 (sitemap robustness), the content of the sitemap: absolute addresses built from the public URL, a shared article under each product, the categories derived from the articles, the 50,000
/// cap, and a build that fails whole rather than listing half a site. The API only ever returns published articles, so the build lists exactly what it is given.
/// </summary>
public sealed class PortalSitemapBuilderTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Newer = new(2026, 10, 5, 23, 59, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Older = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    private static KbSitemapEntryDto Article(string? product, string category, string slug, DateTimeOffset updated) => new(product, category, slug, updated);

    private static async Task<IReadOnlyList<SitemapEntry>> BuildAsync(ApiHarness api, int reservedForStatic = 0) =>
        await api.Get<PortalSitemapBuilder>().BuildAsync(reservedForStatic, Ct);

    private static ApiHarness Harness()
    {
        var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/products", new[] { new PublicProductSummaryDto("acme", "Acme"), new PublicProductSummaryDto("orbitly", "Orbitly") });
        return api;
    }

    [Fact]
    public async Task Each_products_home_help_centre_categories_and_articles_are_listed_with_absolute_addresses()
    {
        using var api = Harness();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/acme/sitemap", new[]
        {
            Article("acme", "accounts", "reset-password", Newer),
            Article("acme", "billing", "invoices", Older),
            Article("acme", "accounts", "change-email", Older),
        });
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/orbitly/sitemap", Array.Empty<KbSitemapEntryDto>());

        var entries = await BuildAsync(api);

        entries.Select(entry => entry.Url).ShouldBe(
        [
            "https://portal.test/p/acme",
            "https://portal.test/p/acme/kb",
            "https://portal.test/p/acme/kb/accounts",
            "https://portal.test/p/acme/kb/billing",
            "https://portal.test/p/acme/kb/accounts/reset-password",
            "https://portal.test/p/acme/kb/billing/invoices",
            "https://portal.test/p/acme/kb/accounts/change-email",
            "https://portal.test/p/orbitly",
        ]);
        entries.ShouldAllBe(entry => entry.Url.StartsWith("https://portal.test/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_category_and_the_help_centre_home_are_as_new_as_their_newest_article_and_an_article_has_its_own_day()
    {
        using var api = Harness();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/acme/sitemap", new[]
        {
            Article("acme", "accounts", "old-one", Older),
            Article("acme", "accounts", "new-one", Newer),
            Article("acme", "billing", "invoices", Older),
        });
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/orbitly/sitemap", Array.Empty<KbSitemapEntryDto>());

        var entries = (await BuildAsync(api)).ToDictionary(entry => entry.Url, entry => entry.LastModified);

        entries["https://portal.test/p/acme"].ShouldBeNull("a product home has no date of its own");
        entries["https://portal.test/p/acme/kb"].ShouldBe(new DateOnly(2026, 10, 5));
        entries["https://portal.test/p/acme/kb/accounts"].ShouldBe(new DateOnly(2026, 10, 5));
        entries["https://portal.test/p/acme/kb/billing"].ShouldBe(new DateOnly(2026, 9, 1));
        entries["https://portal.test/p/acme/kb/accounts/old-one"].ShouldBe(new DateOnly(2026, 9, 1));
        entries["https://portal.test/p/acme/kb/accounts/new-one"].ShouldBe(new DateOnly(2026, 10, 5));
    }

    [Fact]
    public async Task A_shared_article_is_listed_under_each_product_it_is_visible_to_and_never_under_a_path_of_its_own()
    {
        using var api = Harness();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/acme/sitemap", new[] { Article(null, "general", "shared-tips", Newer), Article("acme", "general", "acme-only", Older) });
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/orbitly/sitemap", new[] { Article(null, "general", "shared-tips", Newer) });

        var urls = (await BuildAsync(api)).Select(entry => entry.Url).ToList();

        urls.ShouldContain("https://portal.test/p/acme/kb/general/shared-tips");
        urls.ShouldContain("https://portal.test/p/orbitly/kb/general/shared-tips");
        urls.ShouldContain("https://portal.test/p/acme/kb/general/acme-only");
        urls.ShouldNotContain(url => url.Contains("/p//", StringComparison.Ordinal) || url.Contains("/p/kb", StringComparison.Ordinal));
        urls.Count(url => url.EndsWith("/shared-tips", StringComparison.Ordinal)).ShouldBe(2);
    }

    [Fact]
    public async Task A_product_with_no_article_has_a_home_and_no_help_centre_entries()
    {
        using var api = Harness();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/acme/sitemap", Array.Empty<KbSitemapEntryDto>());
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/orbitly/sitemap", Array.Empty<KbSitemapEntryDto>());

        (await BuildAsync(api)).Select(entry => entry.Url).ShouldBe(["https://portal.test/p/acme", "https://portal.test/p/orbitly"]);
    }

    [Fact]
    public async Task No_product_at_all_is_an_empty_list_and_one_call()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/products", Array.Empty<PublicProductSummaryDto>());

        (await BuildAsync(api)).ShouldBeEmpty();
        api.Stub.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/public/products");
    }

    [Fact]
    public async Task The_build_asks_once_for_the_products_and_once_per_product_forwarding_the_visitors_address()
    {
        using var api = Harness();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/acme/sitemap", Array.Empty<KbSitemapEntryDto>());
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/orbitly/sitemap", Array.Empty<KbSitemapEntryDto>());

        await BuildAsync(api);

        api.Stub.Requests.Select(request => request.Path).ShouldBe(["/api/public/products", "/api/public/kb/acme/sitemap", "/api/public/kb/orbitly/sitemap"]);
        api.Stub.Requests.ShouldAllBe(request => request.Client == ApiClientNames.Read);
        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
    }

    [Fact]
    public async Task A_slug_with_characters_outside_a_path_segment_is_escaped_so_it_cannot_add_a_segment_a_query_or_a_fragment()
    {
        using var api = Harness();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/acme/sitemap", new[] { Article("acme", "a/b?c#d", "x y&z", Newer) });
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/orbitly/sitemap", Array.Empty<KbSitemapEntryDto>());

        var urls = (await BuildAsync(api)).Select(entry => entry.Url).ToList();

        urls.ShouldContain("https://portal.test/p/acme/kb/a%2Fb%3Fc%23d/x%20y%26z");
        urls.ShouldContain("https://portal.test/p/acme/kb/a%2Fb%3Fc%23d");
    }

    [Fact]
    public async Task The_same_address_twice_is_listed_once()
    {
        using var api = Harness();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/acme/sitemap", new[] { Article("acme", "accounts", "reset", Newer), Article(null, "accounts", "reset", Older) });
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/orbitly/sitemap", Array.Empty<KbSitemapEntryDto>());

        var urls = (await BuildAsync(api)).Select(entry => entry.Url).ToList();

        urls.Count(url => url.EndsWith("/accounts/reset", StringComparison.Ordinal)).ShouldBe(1);
        urls.Distinct().Count().ShouldBe(urls.Count);
    }

    [Fact]
    public async Task At_most_fifty_thousand_addresses_are_listed_the_first_ones_and_the_cut_is_logged()
    {
        var logs = new List<string>();
        using var api = ApiHarness.Create(configure: services => services.AddLogging(logging => logging.AddProvider(new ListLoggerProvider(logs))));
        api.Stub.OnJson(HttpMethod.Get, "/api/public/products", new[] { new PublicProductSummaryDto("acme", "Acme") });
        api.Stub.OnJson(
            HttpMethod.Get,
            "/api/public/kb/acme/sitemap",
            Enumerable.Range(0, PortalSitemapBuilder.MaxUrls + 10).Select(i => Article("acme", "accounts", $"article-{i}", Newer)).ToArray());

        var entries = await BuildAsync(api);

        entries.Count.ShouldBe(PortalSitemapBuilder.MaxUrls);
        entries[0].Url.ShouldBe("https://portal.test/p/acme");
        entries.ShouldNotContain(entry => entry.Url.EndsWith("/article-" + (PortalSitemapBuilder.MaxUrls + 5), StringComparison.Ordinal));
        logs.ShouldContain(line => line.Contains("only the first 50000 are listed", StringComparison.Ordinal));
        PortalSitemapBuilder.MaxUrls.ShouldBe(50_000);
    }

    [Fact]
    public async Task The_static_entries_count_against_the_limit_so_the_whole_file_never_goes_over_it()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/products", new[] { new PublicProductSummaryDto("acme", "Acme") });
        api.Stub.OnJson(
            HttpMethod.Get,
            "/api/public/kb/acme/sitemap",
            Enumerable.Range(0, PortalSitemapBuilder.MaxUrls + 10).Select(i => Article("acme", "accounts", $"article-{i}", Newer)).ToArray());

        var entries = await BuildAsync(api, reservedForStatic: 1);

        entries.Count.ShouldBe(PortalSitemapBuilder.MaxUrls - 1);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_failing_product_list_fails_the_build_and_nothing_is_listed(HttpStatusCode status)
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Get, "/api/public/products", status);

        var error = await Should.ThrowAsync<SitemapBuildException>(() => BuildAsync(api));

        error.Message.ShouldStartWith("The product list failed");
    }

    [Fact]
    public async Task One_products_failing_sitemap_fails_the_whole_build_rather_than_leaving_it_out_for_fifteen_minutes()
    {
        using var api = Harness();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/acme/sitemap", new[] { Article("acme", "accounts", "reset", Newer) });
        api.Stub.OnStatus(HttpMethod.Get, "/api/public/kb/orbitly/sitemap", HttpStatusCode.TooManyRequests);

        var error = await Should.ThrowAsync<SitemapBuildException>(() => BuildAsync(api));

        error.Message.ShouldStartWith("The sitemap of a product failed");
        error.Message.ShouldNotContain("orbitly", Case.Insensitive);
    }

    [Theory]
    [InlineData(null, "https://portal.test/")]
    [InlineData("", "https://portal.test/")]
    [InlineData("   ", "https://portal.test/")]
    [InlineData("paperplane", null)]
    public void The_root_page_is_a_static_entry_only_when_no_default_product_is_configured_because_then_it_is_a_redirect(string? defaultProduct, string? expected)
    {
        var options = new PortalOptions { ApiBaseUrl = "http://api.test/", PublicUrl = "https://portal.test/", DefaultProduct = defaultProduct };

        var entries = PortalSitemap.StaticEntries(options).Select(entry => entry.Url).ToList();

        entries.ShouldBe(expected is null ? [] : [expected]);
    }

    [Fact]
    public void The_entries_of_a_product_are_relative_when_there_is_no_public_address()
    {
        var entries = PortalSitemapBuilder.EntriesOf(string.Empty, "acme", [Article("acme", "accounts", "reset", Newer)]).Select(entry => entry.Url).ToList();

        entries.ShouldBe(["/p/acme", "/p/acme/kb", "/p/acme/kb/accounts", "/p/acme/kb/accounts/reset"]);
    }

    private sealed class ListLoggerProvider(List<string> lines) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ListLogger(lines);

        public void Dispose()
        {
        }

        private sealed class ListLogger(List<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (lines)
                {
                    lines.Add(formatter(state, exception));
                }
            }
        }
    }
}
```

`tests/TechStrap.Portal.Tests/Seo/PortalSitemapCacheTests.cs` (new)

```csharp
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Portal.Seo;

namespace TechStrap.Portal.Tests.Seo;

/// <summary>
/// PHASE-09c Review Focus 4 (sitemap robustness), the cache: single-flight, a build that no crawler can cancel, a failure remembered briefly and the last good sitemap served meanwhile. The lifetimes are short real ones
/// (a <see cref="MemoryCache"/> has no <see cref="TimeProvider"/>), so every test that waits for one waits for a few hundred milliseconds.
/// </summary>
public sealed class PortalSitemapCacheTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan Long = TimeSpan.FromMinutes(5);

    private static IReadOnlyList<SitemapEntry> Entries(string name) => [new SitemapEntry($"https://portal.test/{name}")];

    private static PortalSitemapCache Cache(TimeSpan? ttl = null, TimeSpan? failureTtl = null, TimeSpan? buildTimeout = null) =>
        new(new MemoryCache(new MemoryCacheOptions()), NullLogger<PortalSitemapCache>.Instance, ttl ?? Long, failureTtl ?? Long, buildTimeout ?? Long);

    private sealed class Builds
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public Func<CancellationToken, Task<IReadOnlyList<SitemapEntry>>> Succeeding(string name, TimeSpan? delay = null) => async token =>
        {
            Interlocked.Increment(ref _count);
            if (delay is { } wait)
            {
                await Task.Delay(wait, token);
            }

            return Entries(name);
        };

        public Func<CancellationToken, Task<IReadOnlyList<SitemapEntry>>> Failing() => token =>
        {
            Interlocked.Increment(ref _count);
            return Task.FromException<IReadOnlyList<SitemapEntry>>(new SitemapBuildException("The product list failed (api-unavailable)."));
        };
    }

    [Fact]
    public async Task Twenty_concurrent_requests_make_one_build_and_all_get_its_result()
    {
        var cache = Cache();
        var builds = new Builds();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => cache.GetAsync(builds.Succeeding("one", TimeSpan.FromMilliseconds(150)), Ct)));

        builds.Count.ShouldBe(1);
        results.ShouldAllBe(result => result.Count == 1 && result[0].Url == "https://portal.test/one");
    }

    [Fact]
    public async Task A_second_request_within_the_lifetime_is_served_from_the_cache_without_a_build()
    {
        var cache = Cache();
        var builds = new Builds();

        await cache.GetAsync(builds.Succeeding("one"), Ct);
        var again = await cache.GetAsync(builds.Succeeding("two"), Ct);

        builds.Count.ShouldBe(1);
        again[0].Url.ShouldBe("https://portal.test/one");
    }

    [Fact]
    public async Task After_the_lifetime_the_next_request_builds_again()
    {
        var cache = Cache(ttl: Short);
        var builds = new Builds();
        await cache.GetAsync(builds.Succeeding("one"), Ct);

        await Task.Delay(Short + TimeSpan.FromMilliseconds(600), Ct);
        var again = await cache.GetAsync(builds.Succeeding("two"), Ct);

        builds.Count.ShouldBe(2);
        again[0].Url.ShouldBe("https://portal.test/two");
    }

    [Fact]
    public async Task A_crawler_that_goes_away_stops_waiting_but_the_build_finishes_and_the_next_request_is_served_from_it()
    {
        var cache = Cache();
        var builds = new Builds();
        using var crawler = new CancellationTokenSource();

        var aborted = cache.GetAsync(builds.Succeeding("one", TimeSpan.FromMilliseconds(300)), crawler.Token);
        await crawler.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => aborted);
        var next = await cache.GetAsync(builds.Succeeding("never-used"), Ct);

        builds.Count.ShouldBe(1, "the build the aborted crawler started is the one everyone else used");
        next[0].Url.ShouldBe("https://portal.test/one");
    }

    [Fact]
    public async Task The_token_the_build_gets_is_not_the_requests_and_the_build_does_not_run_in_the_requests_execution_context()
    {
        var cache = Cache();
        using var crawler = new CancellationTokenSource();
        var requestLocal = new AsyncLocal<string?> { Value = "the request" };
        CancellationToken? seen = null;
        string? localInBuild = "unset";

        await cache.GetAsync(
            token =>
            {
                seen = token;
                localInBuild = requestLocal.Value;
                return Task.FromResult(Entries("one"));
            },
            crawler.Token);

        seen.ShouldNotBeNull();
        seen.Value.ShouldNotBe(crawler.Token);
        await crawler.CancelAsync();
        seen.Value.IsCancellationRequested.ShouldBeFalse("canceling the request never cancels the build");
        localInBuild.ShouldBeNull("the request's HttpContext holder lives in its execution context, which the build must not inherit");
    }

    [Fact]
    public async Task A_failed_build_with_nothing_earlier_fails_the_request_and_is_not_repeated_for_the_failure_lifetime()
    {
        var cache = Cache(failureTtl: Long);
        var builds = new Builds();

        await Should.ThrowAsync<SitemapBuildException>(() => cache.GetAsync(builds.Failing(), Ct));
        await Should.ThrowAsync<SitemapUnavailableException>(() => cache.GetAsync(builds.Failing(), Ct));
        await Should.ThrowAsync<SitemapUnavailableException>(() => cache.GetAsync(builds.Succeeding("one"), Ct));

        builds.Count.ShouldBe(1, "the API is not asked again while the failure is remembered");
    }

    [Fact]
    public async Task After_the_failure_lifetime_the_build_is_tried_again_and_a_success_is_cached()
    {
        var cache = Cache(failureTtl: Short);
        var builds = new Builds();
        await Should.ThrowAsync<SitemapBuildException>(() => cache.GetAsync(builds.Failing(), Ct));

        await Task.Delay(Short + TimeSpan.FromMilliseconds(600), Ct);
        var recovered = await cache.GetAsync(builds.Succeeding("one"), Ct);
        var cached = await cache.GetAsync(builds.Succeeding("two"), Ct);

        builds.Count.ShouldBe(2);
        recovered[0].Url.ShouldBe("https://portal.test/one");
        cached[0].Url.ShouldBe("https://portal.test/one");
    }

    [Fact]
    public async Task When_a_rebuild_fails_the_last_good_sitemap_is_served_during_the_failure_lifetime()
    {
        var cache = Cache(ttl: Short, failureTtl: Long);
        var builds = new Builds();
        await cache.GetAsync(builds.Succeeding("good"), Ct);
        await Task.Delay(Short + TimeSpan.FromMilliseconds(600), Ct);

        var failing = await cache.GetAsync(builds.Failing(), Ct);
        var meanwhile = await cache.GetAsync(builds.Succeeding("not-asked"), Ct);

        failing[0].Url.ShouldBe("https://portal.test/good", "the request whose rebuild failed still gets the last good one");
        meanwhile[0].Url.ShouldBe("https://portal.test/good");
        builds.Count.ShouldBe(2, "one good build and one failed rebuild; nothing else while the failure is remembered");
    }

    [Fact(Timeout = 20_000)]
    public async Task A_build_that_never_finishes_ends_after_the_timeout_and_counts_as_a_failure()
    {
        var cache = Cache(buildTimeout: TimeSpan.FromMilliseconds(200));
        var started = 0;

        await Should.ThrowAsync<OperationCanceledException>(() => cache.GetAsync(
            async token =>
            {
                Interlocked.Increment(ref started);
                await Task.Delay(Timeout.Infinite, token);
                return Entries("never");
            },
            TestContext.Current.CancellationToken));
        await Should.ThrowAsync<SitemapUnavailableException>(() => cache.GetAsync(token => Task.FromResult(Entries("not-asked")), TestContext.Current.CancellationToken));

        started.ShouldBe(1);
    }

    [Fact]
    public async Task A_new_build_can_start_after_a_failure_is_forgotten_even_when_the_first_flight_threw()
    {
        var cache = Cache(failureTtl: TimeSpan.FromMilliseconds(100));
        var builds = new Builds();
        for (var i = 0; i < 3; i++)
        {
            await Should.ThrowAsync<SitemapBuildException>(() => cache.GetAsync(builds.Failing(), Ct));
            await Task.Delay(TimeSpan.FromMilliseconds(500), Ct);
        }

        builds.Count.ShouldBe(3, "a failed flight is cleared, so it never blocks the next try");
    }

    [Fact]
    public void The_production_lifetimes_are_fifteen_minutes_one_minute_and_two_minutes()
    {
        PortalSitemapCache.DefaultTtl.ShouldBe(TimeSpan.FromMinutes(15));
        PortalSitemapCache.DefaultFailureTtl.ShouldBe(TimeSpan.FromMinutes(1));
        PortalSitemapCache.BuildTimeout.ShouldBe(TimeSpan.FromMinutes(2));
        PortalSitemap.ClientCacheDuration.ShouldBe(TimeSpan.FromMinutes(5));
    }
}
```

`tests/TechStrap.Portal.Tests/Seo/SeoHostTests.cs`

```diff
@@ -1,14 +1,17 @@
 using System.Net;
+using System.Xml.Linq;
 using Microsoft.Extensions.DependencyInjection;
 using Microsoft.Extensions.Options;
 using SyntaxCircus.Blazor.Seo;
+using TechStrap.Contracts.Products;
 using TechStrap.Portal.Settings;
+using TechStrap.Portal.Tests.Api;
 
 namespace TechStrap.Portal.Tests.Seo;
 
 /// <summary>
 /// P09-T04 (09a part): <c>SyntaxCircus.Blazor.Seo</c> with its real API. <c>Seo:BaseUrl</c> is derived from <c>TECHSTRAP_PORTAL_PUBLIC_URL</c> (one setting for one value), robots.txt disallows the ticket
-/// pages, and the canonical-host redirect is an allow-list that does nothing while it is not configured. The sitemap is not mapped until PHASE-09c (it needs the products endpoint), which a test pins.
+/// pages, and the canonical-host redirect is an allow-list that does nothing while it is not configured. PHASE-09c maps the sitemap (its content is pinned in <c>SitemapHostTests</c>).
 /// </summary>
 public sealed class SeoHostTests
 {
@@ -44,15 +47,41 @@ public sealed class SeoHostTests
     }
 
     [Fact]
-    public async Task The_sitemap_is_not_mapped_in_09a()
+    public async Task The_sitemap_is_served_as_xml_and_with_no_product_it_lists_the_root_page_only()
     {
         await using var factory = new PortalFactory();
+        factory.Api.OnJson(HttpMethod.Get, "/api/public/products", Array.Empty<PublicProductSummaryDto>());
         using var client = factory.CreateClient();
 
         using var response = await client.GetAsync("/sitemap.xml", Ct);
 
-        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, "09c maps the sitemap once the products endpoint exists; until then robots.txt names a sitemap that answers 404");
-        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
+        response.StatusCode.ShouldBe(HttpStatusCode.OK, "robots.txt names the sitemap, so it must be there (09a left it unmapped; PHASE-09c maps it)");
+        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/xml");
+        var locations = XDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).Descendants().Where(element => element.Name.LocalName == "loc").Select(element => element.Value).ToList();
+        locations.ShouldBe([PortalFactory.PublicUrl + "/"]);
+    }
+
+    [Fact]
+    public async Task The_sitemap_build_calls_the_api_with_the_address_of_the_visitor_whose_request_started_it_and_lists_what_it_found()
+    {
+        await using var factory = TechStrap.Portal.Tests.Forms.FormTestKit.Factory(product: false);
+        factory.Api.OnJson(HttpMethod.Get, "/api/public/products", new[] { new PublicProductSummaryDto("paperplane", "Paperplane") });
+        factory.Api.OnJson(HttpMethod.Get, "/api/public/kb/paperplane/sitemap", new[] { new TechStrap.Contracts.Kb.KbSitemapEntryDto(null, "general", "shared-tips", new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero)) });
+        using var client = TechStrap.Portal.Tests.Forms.FormTestKit.Client(factory);
+
+        using var response = await client.GetAsync("/sitemap.xml", Ct);
+        var locations = XDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).Descendants().Where(element => element.Name.LocalName == "loc").Select(element => element.Value).ToList();
+
+        locations.ShouldBe(
+        [
+            PortalFactory.PublicUrl + "/",
+            PortalFactory.PublicUrl + "/p/paperplane",
+            PortalFactory.PublicUrl + "/p/paperplane/kb",
+            PortalFactory.PublicUrl + "/p/paperplane/kb/general",
+            PortalFactory.PublicUrl + "/p/paperplane/kb/general/shared-tips",
+        ]);
+        factory.Api.Requests.Select(request => request.Path).ShouldBe(["/api/public/products", "/api/public/kb/paperplane/sitemap"]);
+        factory.Api.AssertEveryCallBore(TechStrap.Portal.Tests.Forms.FormTestKit.Visitor);
     }
 
     [Theory]
```


- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build tests/TechStrap.Portal.Tests -c Release
dotnet build tests/TechStrap.Api.Tests -c Release
```

Expected (recorded in the scratch copy with the source files set aside):

```text
error CS0234: The type or namespace name 'Caching' does not exist in the namespace 'TechStrap.Portal'
error CS0246: The type or namespace name 'PortalSitemapCache' could not be found
error CS0117: 'PathHeaderRule' does not contain a definition for 'SetOnSuccess'
```

- [ ] **Step 3: Write the implementation**

The Hosting rule, the client methods and the slug shape, the cache paths and the policy with its pipeline step, the two header rules, the sitemap builder, cache and provider, and the wiring in `Program.cs` and `PortalSeoRegistration`. The comments in these files carry the reasoning (the spike's findings are in the class summaries of `PortalOutputCache` and `PortalSitemapCache`).

`src/TechStrap.Hosting/Wiring/PathHeaderRule.cs`

```diff
@@ -27,7 +27,15 @@ public sealed class PathHeaderRule
     internal bool SuccessOnly { get; }
 
     /// <summary>Sets each header to the given value on every response for a matching path, whatever status it has.</summary>
-    public static PathHeaderRule Set(Func<PathString, bool> matches, params (string Name, string Value)[] headers)
+    public static PathHeaderRule Set(Func<PathString, bool> matches, params (string Name, string Value)[] headers) => Create(matches, headers, successOnly: false);
+
+    /// <summary>
+    /// Sets each header to the given value on a successful (2xx) response for a matching path only; a 404, a 429, a 503 or a redirect keeps whatever it had. It is for a header that is only true of a delivered page,
+    /// such as <c>Cache-Control: public, max-age=60</c>, which must never be put on an error answer (a browser or a proxy would keep it).
+    /// </summary>
+    public static PathHeaderRule SetOnSuccess(Func<PathString, bool> matches, params (string Name, string Value)[] headers) => Create(matches, headers, successOnly: true);
+
+    private static PathHeaderRule Create(Func<PathString, bool> matches, (string Name, string Value)[] headers, bool successOnly)
     {
         ArgumentNullException.ThrowIfNull(matches);
         ArgumentNullException.ThrowIfNull(headers);
@@ -36,7 +44,7 @@ public sealed class PathHeaderRule
             throw new ArgumentException("A rule sets at least one header, and every header has a name.", nameof(headers));
         }
 
-        return new PathHeaderRule(matches, [.. headers.Select(h => (h.Name, (Func<string, string>)(_ => h.Value)))], successOnly: false);
+        return new PathHeaderRule(matches, [.. headers.Select(h => (h.Name, (Func<string, string>)(_ => h.Value)))], successOnly);
     }
 
     /// <summary>
```

`src/TechStrap.Portal/Caching/PortalCachePaths.cs` (new)

```csharp
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Caching;

/// <summary>
/// Which requests the Portal may keep (D-045 addendum, PHASE-09c). Only the three kinds of help-center page that show the same thing to every visitor: <c>/p/{key}/kb</c>, <c>/p/{key}/kb/{category}</c> and
/// <c>/p/{key}/kb/{category}/{slug}</c>. The search page (any text can be asked), the form pages, the suggest adapter, <c>/t/*</c> and every other path are never kept; a 404, a 429 and a 503 never are either
/// (the output cache stores a 200 only). The predicates are written like <c>PortalHeaderRules.IsFormPagePath</c>: without regard to case or a trailing slash, because routing matches that way.
/// </summary>
internal static partial class PortalCachePaths
{
    /// <summary>How long the server keeps a page: the API's own <c>max-age</c> for the same data, so a cached page is never staler than the API's answer could have been.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    /// <summary>What a browser is told about a delivered KB page (the same minute).</summary>
    public const string BrowserCacheControl = "public, max-age=60";

    // A page number of one to four digits, no sign and no leading zero: the only value a cached request may carry. Anything else (text, a huge number, two values) is still answered, but never stored,
    // because each distinct value would be a new key and a visitor could fill the store with them.
    [GeneratedRegex(@"\A[1-9][0-9]{0,3}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PageNumber();

    /// <summary>A help-center page of the three kinds above (the search page is not one).</summary>
    public static bool IsKbPage(PathString path) =>
        path.StartsWithSegments(PortalRoutes.ProductPrefix, out var rest)
        && Segments(rest) is [_, var kb, .. var tail]
        && Is(kb, PortalRoutes.KbSegment)
        && tail.Length <= 2
        && (tail.Length == 0 || !Is(tail[0], PortalRoutes.KbSearchSegment));

    /// <summary>Exactly <c>/p/{key}/kb/search</c>.</summary>
    public static bool IsKbSearchPath(PathString path) =>
        path.StartsWithSegments(PortalRoutes.ProductPrefix, out var rest)
        && Segments(rest) is [_, var kb, var search]
        && Is(kb, PortalRoutes.KbSegment)
        && Is(search, PortalRoutes.KbSearchSegment);

    /// <summary>A KB page whose <c>page</c> query value is absent or a plain page number (other query values do not change the page, so they do not change the key).</summary>
    public static bool IsCacheable(HttpRequest request) =>
        IsKbPage(request.Path)
        && (!request.Query.TryGetValue(PortalRoutes.PageParameter, out var pages) || (pages.Count == 1 && pages[0] is { } page && PageNumber().IsMatch(page)));

    private static string[] Segments(PathString rest) => rest.Value!.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static bool Is(string segment, string expected) => segment.Equals(expected, StringComparison.OrdinalIgnoreCase);
}
```

`src/TechStrap.Portal/Caching/PortalOutputCache.cs` (new)

```csharp
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Options;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Caching;

/// <summary>
/// The framework's output cache for the help center (D-045 addendum, PHASE-09c): one base policy, not an attribute on a page, so the rule for what is kept is in one place (<see cref="PortalCachePaths"/>) and a form or
/// ticket page can never be kept by forgetting to leave something off. Kept for <see cref="PortalCachePaths.Lifetime"/>, varying by the <c>page</c> query value only and not by host: the framework's default key holds the whole
/// query string and the host, so a visitor could fill the store with <c>?utm=1</c>, <c>?utm=2</c> and so on, and the page does not depend on either (the canonical address comes from <c>Seo:BaseUrl</c>, never the Host header).
/// </summary>
internal static class PortalOutputCache
{
    public static IServiceCollection AddPortalOutputCache(this IServiceCollection services)
    {
        services.AddOutputCache(options => options.AddBasePolicy(policy => policy
            .With(context => PortalCachePaths.IsCacheable(context.HttpContext.Request))
            .Expire(PortalCachePaths.Lifetime)
            .SetVaryByQuery(PortalRoutes.PageParameter)
            .SetVaryByHost(false)));
        return services;
    }

    /// <summary>
    /// Place it after the error pages and before the endpoints. The shared security headers and the per-path rules are applied by steps that sit before it, when the response starts, so they are right on a cached answer
    /// too. One thing is not: the correlation id middleware puts the request's id on the response before the cache sees it, so the cache stores the first request's id and replays it on every hit (the spike sent
    /// <c>cid-two</c> and got <c>cid-one</c>). The step below sets the request's own id again when the response starts, which is after the cache has written a stored copy.
    /// </summary>
    public static WebApplication UsePortalOutputCache(this WebApplication app)
    {
        var headerName = app.Services.GetRequiredService<IOptions<CorrelationIdOptions>>().Value.HeaderName;
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                if (context.Items[headerName] is string correlationId)
                {
                    context.Response.Headers[headerName] = correlationId;
                }

                return Task.CompletedTask;
            });
            return next(context);
        });
        app.UseOutputCache();
        return app;
    }
}
```

`src/TechStrap.Portal/Clients/IPublicKbClient.cs`

```diff
@@ -4,12 +4,30 @@ using TechStrap.Contracts.Paging;
 
 namespace TechStrap.Portal.Clients;
 
-/// <summary>The public knowledge base (P09-T02). 09b needs the search only, for the contact page's suggestions; 09c extends this interface with the categories and the articles.</summary>
+/// <summary>
+/// The public knowledge base (P09-T02). Every call is a read: retried like every read, anonymous, and forwarding the visitor's address. Every text field of a result is plain text, a consumer encodes it; only
+/// <see cref="PublishedKbArticleDto.Html"/> is HTML (the API sanitized it). A product key, a category slug or an article slug that is not a slug is the uniform not-found error and no call is made.
+/// </summary>
 public interface IPublicKbClient
 {
     /// <summary>
-    /// The published articles of the product (and the shared ones) that match the text, best first, <paramref name="pageSize"/> at most. Every text field of a hit is plain text: a consumer encodes it. A key that
-    /// is not a slug is the uniform not-found error and no call is made. The API cuts a longer text at <see cref="KbLimits.MaxSearchTextChars"/> and answers a blank text or an unknown product with an empty page.
+    /// The first page of the published articles of the product (and the shared ones) that match the text, best first, <paramref name="pageSize"/> at most. The API cuts a longer text at
+    /// <see cref="KbLimits.MaxSearchTextChars"/> and answers a blank text or an unknown product with an empty page.
     /// </summary>
     Task<Result<PagedResponse<PublicKbSearchResultDto>>> SearchAsync(string productKey, string text, int pageSize, CancellationToken cancellationToken);
+
+    /// <summary>The same for page <paramref name="page"/> (the API normalizes a page below one).</summary>
+    Task<Result<PagedResponse<PublicKbSearchResultDto>>> SearchAsync(string productKey, string text, int page, int pageSize, CancellationToken cancellationToken);
+
+    /// <summary>The categories the product can see (its own and the shared ones) with their published article counts; an empty category is left out, and an unknown product gives an empty list.</summary>
+    Task<Result<IReadOnlyList<PublicKbCategoryDto>>> ListCategoriesAsync(string productKey, CancellationToken cancellationToken);
+
+    /// <summary>One page of a category's published articles, newest update first. An unknown, invisible or empty category (and an unknown product) is the not-found error.</summary>
+    Task<Result<PagedResponse<PublicKbArticleSummaryDto>>> ListCategoryArticlesAsync(string productKey, string categorySlug, int page, int pageSize, CancellationToken cancellationToken);
+
+    /// <summary>A published article. A draft, an archived article, another product's article, a wrong category and an unknown product are the same not-found error.</summary>
+    Task<Result<PublishedKbArticleDto>> GetArticleAsync(string productKey, string categorySlug, string slug, CancellationToken cancellationToken);
+
+    /// <summary>One entry per published article the product can see, newest update first (a shared article has no product key). It feeds the sitemap.</summary>
+    Task<Result<IReadOnlyList<KbSitemapEntryDto>>> GetSitemapAsync(string productKey, CancellationToken cancellationToken);
 }
```

`src/TechStrap.Portal/Clients/IPublicProductClient.cs`

```diff
@@ -11,4 +11,7 @@ public interface IPublicProductClient
     /// and a malformed key is answered without calling the API.
     /// </summary>
     Task<Result<PublicProductDto>> GetAsync(string key, CancellationToken cancellationToken);
+
+    /// <summary>The key and display name of every active product, by key (at most <see cref="PublicProductLimits.MaxListed"/>). Only the sitemap asks for it: no page lists the products (D-045).</summary>
+    Task<Result<IReadOnlyList<PublicProductSummaryDto>>> ListAsync(CancellationToken cancellationToken);
 }
```

`src/TechStrap.Portal/Clients/PublicKbClient.cs`

```diff
@@ -11,7 +11,41 @@ internal sealed class PublicKbClient(ApiConnection api) : IPublicKbClient
     public Task<Result<PagedResponse<PublicKbSearchResultDto>>> SearchAsync(string productKey, string text, int pageSize, CancellationToken cancellationToken) =>
         ProductKeyShape.IsWellFormed(productKey)
             ? api.GetAsync<PagedResponse<PublicKbSearchResultDto>>(
-                ApiQuery.Build($"api/public/kb/{productKey}/search", ("q", text), ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture))),
+                ApiQuery.Build($"api/public/kb/{productKey}/search", ("q", text), ("pageSize", Number(pageSize))),
                 cancellationToken)
-            : Task.FromResult(Result<PagedResponse<PublicKbSearchResultDto>>.Failure(ProblemMapping.NotFound()));
+            : NotFound<PagedResponse<PublicKbSearchResultDto>>();
+
+    public Task<Result<PagedResponse<PublicKbSearchResultDto>>> SearchAsync(string productKey, string text, int page, int pageSize, CancellationToken cancellationToken) =>
+        ProductKeyShape.IsWellFormed(productKey)
+            ? api.GetAsync<PagedResponse<PublicKbSearchResultDto>>(
+                ApiQuery.Build($"api/public/kb/{productKey}/search", ("q", text), ("page", Number(page)), ("pageSize", Number(pageSize))),
+                cancellationToken)
+            : NotFound<PagedResponse<PublicKbSearchResultDto>>();
+
+    public Task<Result<IReadOnlyList<PublicKbCategoryDto>>> ListCategoriesAsync(string productKey, CancellationToken cancellationToken) =>
+        ProductKeyShape.IsWellFormed(productKey)
+            ? api.GetAsync<IReadOnlyList<PublicKbCategoryDto>>($"api/public/kb/{productKey}/categories", cancellationToken)
+            : NotFound<IReadOnlyList<PublicKbCategoryDto>>();
+
+    public Task<Result<PagedResponse<PublicKbArticleSummaryDto>>> ListCategoryArticlesAsync(
+        string productKey, string categorySlug, int page, int pageSize, CancellationToken cancellationToken) =>
+        ProductKeyShape.IsWellFormed(productKey) && KbSlugShape.IsWellFormed(categorySlug)
+            ? api.GetAsync<PagedResponse<PublicKbArticleSummaryDto>>(
+                ApiQuery.Build($"api/public/kb/{productKey}/categories/{categorySlug}/articles", ("page", Number(page)), ("pageSize", Number(pageSize))),
+                cancellationToken)
+            : NotFound<PagedResponse<PublicKbArticleSummaryDto>>();
+
+    public Task<Result<PublishedKbArticleDto>> GetArticleAsync(string productKey, string categorySlug, string slug, CancellationToken cancellationToken) =>
+        ProductKeyShape.IsWellFormed(productKey) && KbSlugShape.IsWellFormed(categorySlug) && KbSlugShape.IsWellFormed(slug)
+            ? api.GetAsync<PublishedKbArticleDto>($"api/public/kb/{productKey}/articles/{categorySlug}/{slug}", cancellationToken)
+            : NotFound<PublishedKbArticleDto>();
+
+    public Task<Result<IReadOnlyList<KbSitemapEntryDto>>> GetSitemapAsync(string productKey, CancellationToken cancellationToken) =>
+        ProductKeyShape.IsWellFormed(productKey)
+            ? api.GetAsync<IReadOnlyList<KbSitemapEntryDto>>($"api/public/kb/{productKey}/sitemap", cancellationToken)
+            : NotFound<IReadOnlyList<KbSitemapEntryDto>>();
+
+    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
+
+    private static Task<Result<T>> NotFound<T>() => Task.FromResult(Result<T>.Failure(ProblemMapping.NotFound()));
 }
```

`src/TechStrap.Portal/Clients/PublicProductClient.cs`

```diff
@@ -10,4 +10,7 @@ internal sealed class PublicProductClient(ApiConnection api) : IPublicProductCli
         ProductKeyShape.IsWellFormed(key)
             ? api.GetAsync<PublicProductDto>($"api/public/products/{key}", cancellationToken)
             : Task.FromResult(Result<PublicProductDto>.Failure(ProblemMapping.NotFound()));
+
+    public Task<Result<IReadOnlyList<PublicProductSummaryDto>>> ListAsync(CancellationToken cancellationToken) =>
+        api.GetAsync<IReadOnlyList<PublicProductSummaryDto>>("api/public/products", cancellationToken);
 }
```

`src/TechStrap.Portal/Headers/PortalHeaderRules.cs`

```diff
@@ -1,5 +1,6 @@
 using Microsoft.AspNetCore.Http;
 using TechStrap.Hosting.Wiring;
+using TechStrap.Portal.Caching;
 using TechStrap.Portal.Routing;
 
 namespace TechStrap.Portal.Headers;
@@ -15,7 +16,7 @@ internal static class PortalHeaderRules
     public const string CacheControl = "no-store";
     public const string RobotsTag = "noindex";
 
-    /// <summary>The rules for <c>UseTechStrapWebHost</c>: the ticket headers, the attachment sandbox, then the form pages' headers.</summary>
+    /// <summary>The rules for <c>UseTechStrapWebHost</c>: the ticket headers, the attachment sandbox, the form pages' headers, then the help center's cache headers.</summary>
     public static IReadOnlyList<PathHeaderRule> Rules { get; } =
     [
         PathHeaderRule.Set(IsTicketPath, ("Referrer-Policy", ReferrerPolicy), ("Cache-Control", CacheControl), ("X-Robots-Tag", RobotsTag)),
@@ -23,6 +24,10 @@ internal static class PortalHeaderRules
 
         // The form pages keep the shared referrer policy (a visitor's own address is no secret from the same site) but are never indexed and never stored: their address can carry a name, an address and a subject.
         PathHeaderRule.Set(IsFormPagePath, ("Cache-Control", CacheControl), ("X-Robots-Tag", RobotsTag)),
+
+        // The help center (PHASE-09c): a delivered page may be kept by a browser for the minute the server keeps it, but only a delivered one: a 404, a 429 or a 503 never gets a public header. The search page is never kept anywhere.
+        PathHeaderRule.SetOnSuccess(PortalCachePaths.IsKbPage, ("Cache-Control", PortalCachePaths.BrowserCacheControl)),
+        PathHeaderRule.Set(PortalCachePaths.IsKbSearchPath, ("Cache-Control", CacheControl)),
     ];
 
     /// <summary><c>/t</c> and everything under it, without regard to case or a trailing slash.</summary>
```

`src/TechStrap.Portal/Program.cs`

```diff
@@ -3,6 +3,7 @@ using SyntaxCircus.AspNetCore.Common;
 using SyntaxCircus.DotEnv;
 using TechStrap.Hosting.Security;
 using TechStrap.Hosting.Wiring;
+using TechStrap.Portal.Caching;
 using TechStrap.Portal.Clients;
 using TechStrap.Portal.Components;
 using TechStrap.Portal.Components.Ui;
@@ -33,6 +34,9 @@ builder.Services.AddTechStrapWebHost(builder.Configuration, TechStrapCsp.ForBlaz
 
 builder.Services.AddRazorComponents();
 
+// The framework's output cache, with one policy for the help-center pages (PortalCachePaths): no form, ticket or search page is ever kept.
+builder.Services.AddPortalOutputCache();
+
 // The API address, the Portal's public address and the optional default product: validated at start (D-043, D-045).
 builder.Services.AddPortalOptions();
 
@@ -68,6 +72,9 @@ app.UseTechStrapWebHost(PortalHeaderRules.Rules);
 app.UsePortalSeo();
 app.UseTechStrapErrorPages();
 
+// The help-center pages are kept for a minute (D-045 addendum, PHASE-09c): after the error pages, before the endpoints, so the security headers and the correlation id are the request's own on a cached answer too.
+app.UsePortalOutputCache();
+
 // Before the antiforgery check, which reads the form: a post over the endpoint's size limit is a plain 413, not the framework's 400 about a token.
 app.UseMiddleware<RequestTooLargeMiddleware>();
 app.UseAntiforgery();
```

`src/TechStrap.Portal/Routing/KbSlugShape.cs` (new)

```csharp
using System.Text.RegularExpressions;

namespace TechStrap.Portal.Routing;

/// <summary>
/// The shape of a knowledge-base category or article slug: the same lowercase letters, digits and single hyphens as a product key, at most 80 characters (the Domain's <c>DomainLimits.KbSlugMaxLength</c>; the Portal
/// cannot reference the Domain). A slug that does not have it is never sent to the API, so a crafted route segment cannot reach a request path, and it is answered as an unknown page.
/// </summary>
public static partial class KbSlugShape
{
    /// <summary>The Domain's <c>DomainLimits.KbSlugMaxLength</c>.</summary>
    public const int MaxLength = 80;

    [GeneratedRegex(@"\A[a-z0-9]+(?:-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Slug();

    public static bool IsWellFormed(string? slug) => slug is { Length: > 0 and <= MaxLength } && Slug().IsMatch(slug);
}
```

`src/TechStrap.Portal/Routing/PortalRoutes.cs`

```diff
@@ -20,6 +20,16 @@ public static class PortalRoutes
     public const string LostLinkSegment = "lost-link";
     public const string SuggestSegment = "suggest";
 
+    // The help center's own segments and query parameters. The output cache and the header rules match on them (PortalCachePaths), so they are named once.
+    public const string KbSegment = "kb";
+    public const string KbSearchSegment = "search";
+
+    /// <summary>The page number of a paged KB list (a category page or the search results).</summary>
+    public const string PageParameter = "page";
+
+    /// <summary>The search text of the KB search page.</summary>
+    public const string QueryParameter = "q";
+
     /// <summary>The query parameter that makes the lost-link page show its confirmation.</summary>
     public const string SentParameter = "sent";
 
@@ -62,13 +72,13 @@ public static class PortalRoutes
     /// <summary>The lost-link page as it is shown after a request: the same address for every request, whatever the address was.</summary>
     public static string LostLinkSent(string key) => $"{LostLink(key)}?{SentParameter}=1";
 
-    public static string KbHome(string key) => $"{ProductHome(key)}/kb";
+    public static string KbHome(string key) => $"{ProductHome(key)}/{KbSegment}";
 
     public static string KbCategory(string key, string category) => $"{KbHome(key)}/{Escape(category)}";
 
     public static string KbArticle(string key, string category, string slug) => $"{KbCategory(key, category)}/{Escape(slug)}";
 
-    public static string KbSearch(string key) => $"{KbHome(key)}/search";
+    public static string KbSearch(string key) => $"{KbHome(key)}/{KbSearchSegment}";
 
     public static string Suggest(string key) => $"{ProductHome(key)}/suggest";
 
```

`src/TechStrap.Portal/Seo/PortalSeoRegistration.cs`

```diff
@@ -7,7 +7,7 @@ namespace TechStrap.Portal.Seo;
 
 /// <summary>
 /// <c>SyntaxCircus.Blazor.Seo</c> in the Portal (D-045). <c>Seo:BaseUrl</c> is derived from <c>TECHSTRAP_PORTAL_PUBLIC_URL</c>, always, so there is one setting for one value and a stray
-/// <c>Seo__BaseUrl</c> can never disagree with it. robots.txt disallows the ticket pages. The sitemap is not mapped in 09a: it needs the products endpoint PHASE-09c adds.
+/// <c>Seo__BaseUrl</c> can never disagree with it. robots.txt disallows the ticket pages and names the sitemap, which lists every active product's help center (PHASE-09c).
 /// </summary>
 public static class PortalSeoRegistration
 {
@@ -18,6 +18,7 @@ public static class PortalSeoRegistration
     {
         services.AddSyntaxCircusSeo(configuration);
         services.AddOptions<SeoOptions>().PostConfigure<IOptions<PortalOptions>>((seo, portal) => seo.BaseUrl = portal.Value.PublicBaseUrl);
+        services.AddPortalSitemap();
         return services;
     }
 
@@ -28,10 +29,11 @@ public static class PortalSeoRegistration
         return app;
     }
 
-    /// <summary>robots.txt (and, from 09c, the sitemap).</summary>
+    /// <summary>robots.txt and the sitemap (<c>/sitemap.xml</c>, which robots.txt names): the static entries, then the products and articles from the cache that <see cref="PortalSitemap"/> feeds.</summary>
     public static WebApplication MapPortalSeo(this WebApplication app)
     {
         app.MapSeoRobotsTxt(extraDirectives: [DisallowTickets]);
+        app.MapSeoSitemap(PortalSitemap.StaticEntries(app.Services.GetRequiredService<IOptions<PortalOptions>>().Value), PortalSitemap.ProviderAsync, PortalSitemap.ClientCacheDuration);
         return app;
     }
 }
```

`src/TechStrap.Portal/Seo/PortalSitemap.cs` (new)

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Seo;

/// <summary>
/// The glue between <c>MapSeoSitemap</c> and the Portal's sitemap cache (D-045 addendum, PHASE-09c). The provider is called with the request's services; the build it hands the cache runs later, on its own task, so it
/// gets a scope of its own and a stand-in <c>HttpContext</c> that carries only the address of the visitor whose request started it: the typed clients read that to set <c>X-Forwarded-For</c>, so the API rate-limits the
/// crawler and not the Portal. Every other request that waits for the same build is served from it without a call of its own; that the build's calls carry the first crawler's address is accepted and recorded as a known gap.
/// </summary>
internal static class PortalSitemap
{
    /// <summary>What a crawler or a proxy may keep: a short while, so a new article shows within minutes (the server cache is 15).</summary>
    public static readonly TimeSpan ClientCacheDuration = TimeSpan.FromMinutes(5);

    public static IServiceCollection AddPortalSitemap(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton(provider => new PortalSitemapCache(
            provider.GetRequiredService<IMemoryCache>(),
            provider.GetRequiredService<ILogger<PortalSitemapCache>>(),
            PortalSitemapCache.DefaultTtl,
            PortalSitemapCache.DefaultFailureTtl,
            PortalSitemapCache.BuildTimeout));
        services.AddScoped<PortalSitemapBuilder>();
        return services;
    }

    /// <summary>The static entries: the root page, unless a default product is configured (then <c>/</c> is a redirect, which a sitemap must not list).</summary>
    public static IReadOnlyList<SitemapEntry> StaticEntries(PortalOptions options) =>
        options.DefaultProductKeyOrNull is null ? [new SitemapEntry(options.PublicBaseUrl + "/")] : [];

    public static Task<IReadOnlyList<SitemapEntry>> ProviderAsync(IServiceProvider requestServices, CancellationToken requestAborted)
    {
        var visitor = requestServices.GetRequiredService<IHttpContextAccessor>().HttpContext?.Connection.RemoteIpAddress;
        var reserved = StaticEntries(requestServices.GetRequiredService<IOptions<PortalOptions>>().Value).Count;
        var scopes = requestServices.GetRequiredService<IServiceScopeFactory>();
        return requestServices.GetRequiredService<PortalSitemapCache>().GetAsync(
            async buildToken =>
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { Connection = { RemoteIpAddress = visitor } };
                return await scope.ServiceProvider.GetRequiredService<PortalSitemapBuilder>().BuildAsync(reserved, buildToken);
            },
            requestAborted);
    }
}
```

`src/TechStrap.Portal/Seo/PortalSitemapBuilder.cs` (new)

```csharp
using Microsoft.Extensions.Options;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Routing;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Seo;

/// <summary>A sitemap build that could not finish: the API said no or could not be reached. The message carries the failing call and its error code, never a visitor's text.</summary>
internal sealed class SitemapBuildException(string message) : Exception(message);

/// <summary>
/// Builds the Portal's sitemap entries from the API (D-045, PHASE-09c): one call for the active products, then one call per product for its published articles. For each product the entries are its home, its help
/// center home (only when it has an article), each category (found from the articles, last changed when its newest article was) and each article; a shared article (no product key) is listed under every product, because each
/// product's help center is its own site. Every address is absolute, built from <c>TECHSTRAP_PORTAL_PUBLIC_URL</c> by <see cref="PortalRoutes"/> (which escapes each segment); with no public address (Development only)
/// they are root-relative. At most <see cref="MaxUrls"/> addresses are listed in all, the limit of one sitemap file, the static entries (the root page) included: the caller says how many of those there are, and the build keeps
/// the rest of the room for the products; a cut is logged. The build is scoped: it uses the typed clients.
/// </summary>
internal sealed class PortalSitemapBuilder(IPublicProductClient products, IPublicKbClient kb, IOptions<PortalOptions> options, ILogger<PortalSitemapBuilder> logger)
{
    /// <summary>The most addresses one sitemap file may hold (sitemaps.org).</summary>
    public const int MaxUrls = 50_000;

    /// <param name="reservedForStatic">How many addresses the sitemap holds besides these (the static entries), so the whole file stays within <see cref="MaxUrls"/>.</param>
    public async Task<IReadOnlyList<SitemapEntry>> BuildAsync(int reservedForStatic, CancellationToken cancellationToken)
    {
        var baseUrl = options.Value.PublicBaseUrl;
        var listed = await products.ListAsync(cancellationToken);
        if (listed.IsFailure)
        {
            throw new SitemapBuildException($"The product list failed ({listed.Errors[0].Code}).");
        }

        var entries = new List<SitemapEntry>();
        foreach (var product in listed.Value)
        {
            var articles = await kb.GetSitemapAsync(product.Key, cancellationToken);
            if (articles.IsFailure)
            {
                throw new SitemapBuildException($"The sitemap of a product failed ({articles.Errors[0].Code}).");
            }

            entries.AddRange(EntriesOf(baseUrl, product.Key, articles.Value));
        }

        var distinct = entries.DistinctBy(entry => entry.Url).ToList();
        var room = Math.Max(0, MaxUrls - reservedForStatic);
        if (distinct.Count > room)
        {
            logger.LogWarning("The sitemap would hold {Count} addresses; only the first {Max} are listed.", distinct.Count, room);
            distinct = distinct.Take(room).ToList();
        }

        return distinct;
    }

    /// <summary>The entries of one product: its home, its help center home, its categories and its articles (see the class summary).</summary>
    internal static IEnumerable<SitemapEntry> EntriesOf(string baseUrl, string productKey, IReadOnlyList<KbSitemapEntryDto> articles)
    {
        yield return new SitemapEntry(baseUrl + PortalRoutes.ProductHome(productKey));
        if (articles.Count == 0)
        {
            yield break;
        }

        yield return new SitemapEntry(baseUrl + PortalRoutes.KbHome(productKey), Day(articles.Max(article => article.UpdatedAt)));
        foreach (var category in articles.GroupBy(article => article.CategorySlug, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            yield return new SitemapEntry(baseUrl + PortalRoutes.KbCategory(productKey, category.Key), Day(category.Max(article => article.UpdatedAt)));
        }

        foreach (var article in articles)
        {
            yield return new SitemapEntry(baseUrl + PortalRoutes.KbArticle(productKey, article.CategorySlug, article.Slug), Day(article.UpdatedAt));
        }
    }

    private static DateOnly Day(DateTimeOffset moment) => DateOnly.FromDateTime(moment.UtcDateTime);
}
```

`src/TechStrap.Portal/Seo/PortalSitemapCache.cs` (new)

```csharp
using Microsoft.Extensions.Caching.Memory;
using SyntaxCircus.AspNetCore.Common;

namespace TechStrap.Portal.Seo;

/// <summary>No sitemap could be built and there is no earlier one to serve.</summary>
internal sealed class SitemapUnavailableException() : Exception("The sitemap could not be built and there is no earlier one to serve.");

/// <summary>
/// The Portal's sitemap, kept for 15 minutes in one <see cref="IMemoryCache"/> entry (D-045 addendum, PHASE-09c). <c>MapSeoSitemap</c> asks its provider on every request and keeps nothing, and a build costs one API call
/// for the products and one per product, so a crawler must never start a build of its own:
/// <list type="bullet">
/// <item><b>Single-flight.</b> While one build runs, every other request waits for it; twenty concurrent requests make one build.</item>
/// <item><b>Its own cancellation token.</b> The build runs on its own task, with no link to any request (and without the request's execution context, so it never touches that request's <c>HttpContext</c>). A crawler that goes
/// away stops waiting (its own token) but cannot cancel the build, so it cannot leave the cache empty or poisoned for the others. A build that never finishes ends after <see cref="BuildTimeout"/>.</item>
/// <item><b>A failure is remembered for a minute.</b> The API is not asked again for that minute. The last good sitemap, however old, is served meanwhile; with none (the very first build failed) the request fails.</item>
/// </list>
/// The lifetimes are constructor values because <see cref="MemoryCacheOptions"/> has no <see cref="TimeProvider"/> (only an obsolete clock): production passes the defaults, a test passes short real ones.
/// </summary>
internal sealed class PortalSitemapCache(IMemoryCache cache, ILogger<PortalSitemapCache> logger, TimeSpan ttl, TimeSpan failureTtl, TimeSpan buildTimeout)
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan DefaultFailureTtl = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(2);

    private const string FreshKey = "portal-sitemap";
    private const string FailedKey = "portal-sitemap-failed";

    private readonly Lock _gate = new();
    private Task<IReadOnlyList<SitemapEntry>>? _flight;
    private IReadOnlyList<SitemapEntry>? _lastGood;

    /// <summary>The sitemap entries: the cached ones, or the result of <paramref name="build"/> when none are cached and no build is running (only the first caller's <paramref name="build"/> runs).</summary>
    public async Task<IReadOnlyList<SitemapEntry>> GetAsync(Func<CancellationToken, Task<IReadOnlyList<SitemapEntry>>> build, CancellationToken requestAborted)
    {
        Task<IReadOnlyList<SitemapEntry>> flight;
        lock (_gate)
        {
            if (cache.TryGetValue(FreshKey, out IReadOnlyList<SitemapEntry>? fresh) && fresh is not null)
            {
                return fresh;
            }

            if (cache.TryGetValue(FailedKey, out _))
            {
                return _lastGood ?? throw new SitemapUnavailableException();
            }

            flight = _flight ??= StartBuild(build);
        }

        return await flight.WaitAsync(requestAborted);
    }

    private Task<IReadOnlyList<SitemapEntry>> StartBuild(Func<CancellationToken, Task<IReadOnlyList<SitemapEntry>>> build)
    {
        // The request's HttpContext lives in an AsyncLocal that the new task would inherit and the build must not touch (see PortalSitemap), so the flow is suppressed for the start only.
        using (ExecutionContext.SuppressFlow())
        {
            return Task.Run(() => RunAsync(build));
        }
    }

    private async Task<IReadOnlyList<SitemapEntry>> RunAsync(Func<CancellationToken, Task<IReadOnlyList<SitemapEntry>>> build)
    {
        using var timeout = new CancellationTokenSource(buildTimeout);
        try
        {
            var entries = await build(timeout.Token);
            lock (_gate)
            {
                _lastGood = entries;
                cache.Set(FreshKey, entries, ttl);
                _flight = null;
            }

            return entries;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The sitemap could not be built; the last good one is served for {Seconds} seconds.", failureTtl.TotalSeconds);
            lock (_gate)
            {
                cache.Set(FailedKey, true, failureTtl);
                _flight = null;
                if (_lastGood is { } stale)
                {
                    return stale;
                }
            }

            throw;
        }
    }
}
```


- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet build TechStrap.slnx -c Release
dotnet test --project tests/TechStrap.Portal.Tests -c Release
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/*/PathHeaderRuleHostTests/*"
dotnet test --project tests/TechStrap.Admin.Tests -c Release
dotnet test --project tests/TechStrap.Architecture.Tests -c Release
```

Expected: 0 warnings; Portal `total: 1189 succeeded: 1189`; `PathHeaderRuleHostTests` 23; Admin 1961; Architecture 294. Run the `Seo` namespace of the Portal tests a few times (`--filter-query "/*/TechStrap.Portal.Tests.Seo/*/*"`) to see that the lifetime tests are not flaky; they wait for a few hundred milliseconds of real time, so on a very slow machine raise the waits in `PortalSitemapCacheTests` together.

- [ ] **Step 5: Prove each pin with a recorded mutation**

Save `$T\specs\m2.py`:

```python
def PT(namespace):
    return ["dotnet", "test", "--project", "tests/TechStrap.Portal.Tests", "-c", "Release", "--filter-query", f"/*/TechStrap.Portal.Tests.{namespace}/*/*"]


CACHING, SEO, CLIENTS, ROUTING, HEADERS = PT("Caching"), PT("Seo"), PT("Clients"), PT("Routing"), PT("Headers")
HOSTING = ["dotnet", "test", "--project", "tests/TechStrap.Api.Tests", "-c", "Release", "--filter-query", "/*/*/PathHeaderRuleHostTests/*"]
PATHS = "src/TechStrap.Portal/Caching/PortalCachePaths.cs"
OC = "src/TechStrap.Portal/Caching/PortalOutputCache.cs"
HR = "src/TechStrap.Portal/Headers/PortalHeaderRules.cs"
KC = "src/TechStrap.Portal/Clients/PublicKbClient.cs"
SB = "src/TechStrap.Portal/Seo/PortalSitemapBuilder.cs"
SC = "src/TechStrap.Portal/Seo/PortalSitemapCache.cs"
SM = "src/TechStrap.Portal/Seo/PortalSitemap.cs"

MUTATIONS = [
    ("1 paths: the search page is a kb page", PATHS, [("        && tail.Length <= 2\n        && (tail.Length == 0 || !Is(tail[0], PortalRoutes.KbSearchSegment));", "        && tail.Length <= 2;")], CACHING),
    ("2 paths: an article's extra segment is a kb page", PATHS, [("&& tail.Length <= 2", "&& tail.Length <= 3")], CACHING),
    ("3 paths: any page value is kept", PATHS, [("        IsKbPage(request.Path)\n        && (!request.Query.TryGetValue(PortalRoutes.PageParameter, out var pages) || (pages.Count == 1 && pages[0] is { } page && PageNumber().IsMatch(page)));", "        IsKbPage(request.Path);")], CACHING),
    ("4 paths: a page number may have ten digits", PATHS, [("[1-9][0-9]{0,3}", "[1-9][0-9]{0,9}")], CACHING),
    ("5 paths: segments are case sensitive", PATHS, [("segment.Equals(expected, StringComparison.OrdinalIgnoreCase)", "segment.Equals(expected, StringComparison.Ordinal)")], CACHING),
    ("6 paths: the server keeps a page for ten minutes", PATHS, [("TimeSpan.FromSeconds(60)", "TimeSpan.FromSeconds(600)")], CACHING),
    ("7 paths: the browser is told ten minutes", PATHS, [('"public, max-age=60"', '"public, max-age=600"')], CACHING),
    ("8 cache: the key varies by host", OC, [("            .SetVaryByHost(false)));", "            ));")], CACHING),
    ("9 cache: the key holds the whole query", OC, [("            .SetVaryByQuery(PortalRoutes.PageParameter)\n", "")], CACHING),
    ("10 cache: every request is kept", OC, [(".With(context => PortalCachePaths.IsCacheable(context.HttpContext.Request))", ".With(context => true)")], CACHING),
    ("11 cache: a hit replays the first correlation id", OC, [("                    context.Response.Headers[headerName] = correlationId;", "                    _ = correlationId;")], CACHING),
    ("12 cache: the middleware is not added", OC, [("        app.UseOutputCache();\n", "")], CACHING),
    ("13 program: the cache runs after the form limit", "src/TechStrap.Portal/Program.cs", [("app.UsePortalOutputCache();\n", ""), ("app.UseMiddleware<RequestTooLargeMiddleware>();\n", "app.UseMiddleware<RequestTooLargeMiddleware>();\napp.UsePortalOutputCache();\n")], CACHING),
    ("14 rules: the browser header is on every status", HR, [("PathHeaderRule.SetOnSuccess(PortalCachePaths.IsKbPage,", "PathHeaderRule.Set(PortalCachePaths.IsKbPage,")], CACHING),
    ("15 rules: the search page has no no-store rule", HR, [('        PathHeaderRule.Set(PortalCachePaths.IsKbSearchPath, ("Cache-Control", CacheControl)),\n', "")], HEADERS),
    ("16 hosting: SetOnSuccess applies to every status", "src/TechStrap.Hosting/Wiring/PathHeaderRule.cs", [("(string Name, string Value)[] headers) => Create(matches, headers, successOnly: true);", "(string Name, string Value)[] headers) => Create(matches, headers, successOnly: false);")], HOSTING),
    ("17 client: a category slug is not checked", KC, [("ProductKeyShape.IsWellFormed(productKey) && KbSlugShape.IsWellFormed(categorySlug)\n            ? api.GetAsync<PagedResponse<PublicKbArticleSummaryDto>>(", "ProductKeyShape.IsWellFormed(productKey)\n            ? api.GetAsync<PagedResponse<PublicKbArticleSummaryDto>>(")], CLIENTS),
    ("18 client: an article slug is not checked", KC, [(" && KbSlugShape.IsWellFormed(slug)", "")], CLIENTS),
    ("19 client: the category route is misspelt", KC, [("/categories/{categorySlug}/articles", "/category/{categorySlug}/articles")], CLIENTS),
    ("20 client: the search page is not sent", KC, [('("q", text), ("page", Number(page)), ("pageSize", Number(pageSize))', '("q", text), ("pageSize", Number(pageSize))')], CLIENTS),
    ("21 routing: a kb slug may have 81 characters", "src/TechStrap.Portal/Routing/KbSlugShape.cs", [("MaxLength = 80;", "MaxLength = 81;")], ROUTING),
    ("22 builder: a duplicate address is listed twice", SB, [("entries.DistinctBy(entry => entry.Url).ToList()", "entries.ToList()")], SEO),
    ("23 builder: no cap", SB, [("distinct = distinct.Take(room).ToList();", "distinct = distinct.ToList();")], SEO),
    ("24 builder: the cap is 50,001", SB, [("MaxUrls = 50_000;", "MaxUrls = 50_001;")], SEO),
    ("25 builder: the help center is as old as its oldest article", SB, [("Day(articles.Max(article => article.UpdatedAt))", "Day(articles.Min(article => article.UpdatedAt))")], SEO),
    ("26 builder: a category is as old as its oldest article", SB, [("Day(category.Max(article => article.UpdatedAt))", "Day(category.Min(article => article.UpdatedAt))")], SEO),
    ("27 builder: a failing product is skipped", SB, [('throw new SitemapBuildException($"The sitemap of a product failed ({articles.Errors[0].Code}).");', "continue;")], SEO),
    ("28 builder: a failing product list is an empty sitemap", SB, [('throw new SitemapBuildException($"The product list failed ({listed.Errors[0].Code}).");', "return [];")], SEO),
    ("29 builder: an empty product gets a help center", SB, [("        if (articles.Count == 0)\n        {\n            yield break;\n        }\n\n", "")], SEO),
    ("30 cache: no single-flight", SC, [("flight = _flight ??= StartBuild(build);", "flight = _flight ?? StartBuild(build);")], SEO),
    ("31 cache: the build inherits the request's context", SC, [("        using (ExecutionContext.SuppressFlow())\n        {\n            return Task.Run(() => RunAsync(build));\n        }", "        return Task.Run(() => RunAsync(build));")], SEO),
    ("32 cache: the build has no timeout token", SC, [("await build(timeout.Token);", "await build(CancellationToken.None);")], SEO),
    ("33 cache: a failure is forgotten at once", SC, [("                cache.Set(FailedKey, true, failureTtl);", "                _ = failureTtl;")], SEO),
    ("34 cache: the last good sitemap is not served after a failed rebuild", SC, [("                if (_lastGood is { } stale)\n                {\n                    return stale;\n                }\n", "")], SEO),
    ("35 cache: the last good sitemap is not served during the failure lifetime", SC, [("                return _lastGood ?? throw new SitemapUnavailableException();", "                throw new SitemapUnavailableException();")], SEO),
    ("36 cache: a success is not kept", SC, [("                cache.Set(FreshKey, entries, ttl);", "                _ = ttl;")], SEO),
    ("37 cache: a finished flight stays", SC, [("                cache.Set(FreshKey, entries, ttl);\n                _flight = null;", "                cache.Set(FreshKey, entries, ttl);")], SEO),
    ("38 provider: the root is always listed", SM, [("options.DefaultProductKeyOrNull is null ?", "options.DefaultProductKeyOrNull is not null ?")], SEO),
    ("39 provider: the build has no visitor address", SM, [("                scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { Connection = { RemoteIpAddress = visitor } };\n", "")], SEO),
    ("40 provider: the client cache is an hour", SM, [("TimeSpan.FromMinutes(5);", "TimeSpan.FromMinutes(60);")], SEO),
    ("41 cache: the default lifetime is an hour", SC, [("DefaultTtl = TimeSpan.FromMinutes(15)", "DefaultTtl = TimeSpan.FromMinutes(60)")], SEO),
]

MUTATIONS += [
    ("42 cache: no explicit expiry", OC, [("            .Expire(PortalCachePaths.Lifetime)\n", "")], CACHING),
]
```

Run them in foreground batches (about a minute each) and record the results:

| # | The mutation | File | Result |
| --- | --- | --- | --- |
| 1 | paths: the search page is a kb page | `PortalCachePaths.cs` | KILLED (7 failing) |
| 2 | paths: an article's extra segment is a kb page | `PortalCachePaths.cs` | KILLED (1 failing) |
| 3 | paths: any page value is kept | `PortalCachePaths.cs` | KILLED (17 failing) |
| 4 | paths: a page number may have ten digits | `PortalCachePaths.cs` | KILLED (1 failing) |
| 5 | paths: segments are case sensitive | `PortalCachePaths.cs` | KILLED (4 failing) |
| 6 | paths: the server keeps a page for ten minutes | `PortalCachePaths.cs` | KILLED (1 failing) |
| 7 | paths: the browser is told ten minutes | `PortalCachePaths.cs` | KILLED (1 failing) |
| 8 | cache: the key varies by host | `PortalOutputCache.cs` | KILLED (1 failing) |
| 9 | cache: the key holds the whole query | `PortalOutputCache.cs` | KILLED (1 failing) |
| 10 | cache: every request is kept | `PortalOutputCache.cs` | KILLED (16 failing) |
| 11 | cache: a hit replays the first correlation id | `PortalOutputCache.cs` | KILLED (1 failing) |
| 12 | cache: the middleware is not added | `PortalOutputCache.cs` | KILLED (5 failing) |
| 13 | program: the cache runs after the form limit | `Program.cs` | KILLED (1 failing) |
| 14 | rules: the browser header is on every status | `PortalHeaderRules.cs` | KILLED (3 failing) |
| 15 | rules: the search page has no no-store rule | `PortalHeaderRules.cs` | KILLED (4 failing) |
| 16 | hosting: SetOnSuccess applies to every status | `PathHeaderRule.cs` | KILLED (6 failing) |
| 17 | client: a category slug is not checked | `PublicKbClient.cs` | KILLED (8 failing) |
| 18 | client: an article slug is not checked | `PublicKbClient.cs` | KILLED (8 failing) |
| 19 | client: the category route is misspelt | `PublicKbClient.cs` | KILLED (1 failing) |
| 20 | client: the search page is not sent | `PublicKbClient.cs` | KILLED (1 failing) |
| 21 | routing: a kb slug may have 81 characters | `KbSlugShape.cs` | KILLED (1 failing) |
| 22 | builder: a duplicate address is listed twice | `PortalSitemapBuilder.cs` | KILLED (1 failing) |
| 23 | builder: no cap | `PortalSitemapBuilder.cs` | KILLED (3 failing) |
| 24 | builder: the cap is 50,001 | `PortalSitemapBuilder.cs` | KILLED (1 failing) |
| 25 | builder: the help center is as old as its oldest article | `PortalSitemapBuilder.cs` | KILLED (1 failing) |
| 26 | builder: a category is as old as its oldest article | `PortalSitemapBuilder.cs` | KILLED (1 failing) |
| 27 | builder: a failing product is skipped | `PortalSitemapBuilder.cs` | KILLED (1 failing) |
| 28 | builder: a failing product list is an empty sitemap | `PortalSitemapBuilder.cs` | KILLED (4 failing) |
| 29 | builder: an empty product gets a help center | `PortalSitemapBuilder.cs` | KILLED (6 failing) |
| 30 | cache: no single-flight | `PortalSitemapCache.cs` | KILLED (2 failing) |
| 31 | cache: the build inherits the request's context | `PortalSitemapCache.cs` | KILLED (1 failing) |
| 32 | cache: the build has no timeout token | `PortalSitemapCache.cs` | KILLED (1 failing) |
| 33 | cache: a failure is forgotten at once | `PortalSitemapCache.cs` | KILLED (3 failing) |
| 34 | cache: the last good sitemap is not served after a failed rebuild | `PortalSitemapCache.cs` | KILLED (1 failing) |
| 35 | cache: the last good sitemap is not served during the failure lifetime | `PortalSitemapCache.cs` | KILLED (1 failing) |
| 36 | cache: a success is not kept | `PortalSitemapCache.cs` | KILLED (2 failing) |
| 37 | cache: a finished flight stays | `PortalSitemapCache.cs` | KILLED (2 failing) |
| 38 | provider: the root is always listed | `PortalSitemap.cs` | KILLED (6 failing) |
| 39 | provider: the build has no visitor address | `PortalSitemap.cs` | KILLED (1 failing) |
| 40 | provider: the client cache is an hour | `PortalSitemap.cs` | KILLED (1 failing) |
| 41 | cache: the default lifetime is an hour | `PortalSitemapCache.cs` | KILLED (1 failing) |
| 42 | cache: no explicit expiry | `PortalOutputCache.cs` | SURVIVED (honest survivor, below) |

**Honest survivor.** Row 42 (removing `.Expire(PortalCachePaths.Lifetime)`) survives, because the framework's default expiry is also 60 seconds: the call is documentation, and `PortalCachePathsTests.The_server_and_the_browser_keep_a_page_for_the_same_minute` pins the constant. Row 32 (the build gets `CancellationToken.None`) would hang its test forever without the xUnit `Timeout` on `A_build_that_never_finishes_ends_after_the_timeout_and_counts_as_a_failure`; with it the test fails after 20 seconds.

- [ ] **Step 6: Run the full suites and commit**

```bash
dotnet test --project tests/TechStrap.Portal.Tests -c Release
dotnet test --project tests/TechStrap.Api.Tests -c Release
```

Expected: 1189 and 928 tests pass.

```bash
git add -A
git diff --cached --stat
git commit -m "feat: PHASE-09c Portal clients, output cache, header rule and sitemap plumbing (D-045)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

### Task 3: The help-center home, category and search pages, with the shared components and `KbCopy`

**Review Focus pin:** 1 (XSS: every name, title, summary, snippet and the typed search text is plain text shown by Razor, in the results, the box and the paging links), 2 (an unknown product, an unknown, invisible or empty category, a malformed slug and a page past the end are the one neutral 404, byte for byte, with no theme) and 3 (the real pages are kept once, a 404 and a 429 are not, the search page never, no cookie).

**Files:**

- Create: `src/TechStrap.Portal/Components/Kb/KbArticleCard.razor`
- Create: `src/TechStrap.Portal/Components/Kb/KbBreadcrumbs.razor`
- Create: `src/TechStrap.Portal/Components/Kb/KbCrumb.cs`
- Create: `src/TechStrap.Portal/Components/Kb/KbPaging.cs`
- Create: `src/TechStrap.Portal/Components/Kb/KbSearchBox.razor`
- Create: `src/TechStrap.Portal/Components/Kb/KbSearchText.cs`
- Create: `src/TechStrap.Portal/Components/KbCopy.cs`
- Create: `src/TechStrap.Portal/Components/Pages/KbCategory.razor`
- Create: `src/TechStrap.Portal/Components/Pages/KbCategory.razor.cs`
- Create: `src/TechStrap.Portal/Components/Pages/KbHome.razor`
- Create: `src/TechStrap.Portal/Components/Pages/KbHome.razor.cs`
- Create: `src/TechStrap.Portal/Components/Pages/KbSearch.razor`
- Create: `src/TechStrap.Portal/Components/Pages/KbSearch.razor.cs`
- Create: `src/TechStrap.Portal/Components/Ui/Pager.razor`
- Create: `src/TechStrap.Portal/Components/Ui/StateMessage.razor`
- Modify: `src/TechStrap.Portal/Components/_Imports.razor`
- Modify: `src/TechStrap.Portal/Products/ProductPageBase.cs`
- Modify: `src/TechStrap.Portal/Routing/PortalRoutes.cs`
- Create: `src/TechStrap.Portal/Seo/KbSeo.cs`
- Modify: `src/TechStrap.Portal/Styles/_components.scss`
- Modify: `src/TechStrap.Portal/Suggestions/SuggestEndpoint.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Components/KbComponentTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Components/KbCopyTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Components/KbPagingTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Kb/KbCategoryHostTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Kb/KbHomeHostTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Kb/KbPageCacheHostTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Kb/KbSearchHostTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Kb/KbTestKit.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/Routing/PortalRoutesTests.cs`

**Interfaces:**
- Consumes: Task 2's `IPublicKbClient` methods, `KbSlugShape`, `PortalRoutes.KbSegment/PageParameter/QueryParameter`, `PortalCachePaths`, `PortalHeaderRules`; 09a/09b's `ProductPageBase` (`Theme`, `Key`, `UnavailableMessage`, `RetryHref`, `NotFoundAfterTheming()`), `ProductScope`, `ProductThemeViewModel` (`Key`, `DisplayName`, `LogoUrl`), `ProductUnavailable`, `ShellCopy` (`SearchLabel`, `SearchButton`, `UnavailableTitle`), `ProblemCopy`, `ApiErrorCodes.RateLimited`, `Seen` (the neutral 404 comparison), `FormTestKit`, `StubApiHandler`, bUnit's `BunitContext`, `SyntaxCircus.Blazor.Seo.Components.SeoHead`.
- Produces:
  - `ProductPageBase.RequestAborted` (the request's token) and `ProductPageBase.Fail(ResultError)` (a not-found is the neutral 404 with the theme forgotten, a 429 or any other failure is `UnavailableMessage` and a 429 or 503).
  - Routes: `PortalRoutes.KbCategory(string key, string category, int page)` (page one is the category's own address) and `PortalRoutes.KbSearch(string key, string text, int page)` (the text escaped; a blank text is the search page itself).
  - Helpers: `KbSearchText.Clean(string?)` (trim, cut at 200 characters without splitting a surrogate pair; the suggest adapter uses it too), `KbPaging.Parse(string?)` (a whole number of one or more, else page one, never throws) and `KbPaging.TotalPages(int totalCount, int pageSize)`, `KbCrumb(string Label, string? Href = null)`, `KbSeo.Image(ProductThemeViewModel)` and `KbSeo.FallbackImage` ("/icon-512.png"), `KbCopy` (all help-center sentences and the title and description builders).
  - Components: `StateMessage` (`Heading`, `ChildContent`; `role="status"`), `Pager` (`Page`, `PageSize`, `TotalCount`, `HrefFor`; plain `rel="prev"` and `rel="next"` links; nothing for one page), `KbArticleCard` (`Href`, `Title`, `Summary`, `Meta`), `KbBreadcrumbs` (`Crumbs`; the last step is `aria-current="page"` and has no link), `KbSearchBox` (`ProductKey`, `Query`; a labeled GET form).
  - Pages: `KbHome` (`/p/{key}/kb`), `KbCategory` (`/p/{key}/kb/{category}`, `PageSize` 10) and `KbSearch` (`/p/{key}/kb/search`, `PageSize` 10), each building on `ProductPageBase` and setting its head with `SeoHead`.
  - Test seam: `KbTestKit` (the stub API paths, DTO builders, a parsed document and small helpers).

- [ ] **Step 1: Write the failing tests**

Host tests with a stub API behind the Portal, bUnit tests of the shared components, and unit tests of the paging and copy helpers. The host tests parse the page with AngleSharp and assert on elements and attributes; the 404 tests compare with `Seen.NeutralNotFoundAsync(Ct, path)`, the page an unknown product gets at the same kind of path. `KbPageCacheHostTests` holds the page-level cache pins of this task (the article's arrive in Task 4).

`tests/TechStrap.Portal.Tests/Components/KbComponentTests.cs` (new)

```csharp
using Bunit;
using TechStrap.Portal.Components.Kb;
using TechStrap.Portal.Components.Ui;

namespace TechStrap.Portal.Tests.Components;

/// <summary>
/// The shared help-center components on their own (P09-T12, T13): what each renders for its parameters. Review Focus 1: a title, a summary, a crumb label and a search text are plain text, so a value that looks like markup
/// is shown as text and never becomes an element.
/// </summary>
public sealed class KbComponentTests : BunitContext
{
    [Fact]
    public void An_article_card_is_a_heading_link_with_an_optional_summary_and_meta_line()
    {
        var cut = Render<KbArticleCard>(parameters => parameters
            .Add(card => card.Href, "/p/paperplane/kb/accounts/reset-password")
            .Add(card => card.Title, "Reset your password")
            .Add(card => card.Summary, "How to reset it")
            .Add(card => card.Meta, "Updated 5 Oct 2026"));

        cut.Find("article.ts-kb-card h2 a").GetAttribute("href").ShouldBe("/p/paperplane/kb/accounts/reset-password");
        cut.Find("article.ts-kb-card h2 a").TextContent.ShouldBe("Reset your password");
        cut.Find(".ts-kb-summary").TextContent.ShouldBe("How to reset it");
        cut.Find(".ts-kb-meta").TextContent.ShouldBe("Updated 5 Oct 2026");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_card_without_a_summary_or_meta_has_no_empty_paragraph(string? summary)
    {
        var cut = Render<KbArticleCard>(parameters => parameters
            .Add(card => card.Href, "/x")
            .Add(card => card.Title, "Title")
            .Add(card => card.Summary, summary));

        cut.FindAll("p").Count.ShouldBe(0);
    }

    [Fact]
    public void A_title_summary_and_meta_that_look_like_markup_are_shown_as_text()
    {
        var cut = Render<KbArticleCard>(parameters => parameters
            .Add(card => card.Href, "/x")
            .Add(card => card.Title, "<script>alert(1)</script>")
            .Add(card => card.Summary, "<img src=x onerror=alert(1)>")
            .Add(card => card.Meta, "<b>bold</b>"));

        cut.FindAll("script, img, b").Count.ShouldBe(0);
        cut.Find("h2 a").TextContent.ShouldBe("<script>alert(1)</script>");
        cut.Find(".ts-kb-summary").TextContent.ShouldBe("<img src=x onerror=alert(1)>");
        cut.Markup.ShouldContain("&lt;script&gt;");
    }

    [Fact]
    public void A_trail_links_every_step_but_the_last_which_is_the_current_page()
    {
        var cut = Render<KbBreadcrumbs>(parameters => parameters.Add(trail => trail.Crumbs, new[]
        {
            new KbCrumb("Paperplane", "/p/paperplane"),
            new KbCrumb("Help center", "/p/paperplane/kb"),
            new KbCrumb("Accounts"),
        }));

        cut.Find("nav.ts-breadcrumbs").GetAttribute("aria-label").ShouldBe("Breadcrumb");
        cut.FindAll("nav.ts-breadcrumbs ol > li").Select(li => li.TextContent.Trim()).ShouldBe(["Paperplane", "Help center", "Accounts"]);
        cut.FindAll("nav.ts-breadcrumbs a").Select(a => a.GetAttribute("href")).ShouldBe(["/p/paperplane", "/p/paperplane/kb"]);
        var last = cut.FindAll("li").Last();
        last.GetAttribute("aria-current").ShouldBe("page");
        last.QuerySelector("a").ShouldBeNull();
    }

    [Fact]
    public void A_crumb_label_that_looks_like_markup_is_shown_as_text_and_an_empty_trail_renders_nothing()
    {
        var hostile = Render<KbBreadcrumbs>(parameters => parameters.Add(trail => trail.Crumbs, new[] { new KbCrumb("<b>Cat</b>", "/x"), new KbCrumb("<i>Page</i>") }));
        var empty = Render<KbBreadcrumbs>(parameters => parameters.Add(trail => trail.Crumbs, Array.Empty<KbCrumb>()));

        hostile.FindAll("b, i").Count.ShouldBe(0);
        hostile.Find("a").TextContent.ShouldBe("<b>Cat</b>");
        empty.Markup.Trim().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(1, 10, 0)]
    [InlineData(1, 10, 1)]
    [InlineData(1, 10, 10)]
    public void A_list_of_one_page_or_none_has_no_pager(int page, int pageSize, int total)
    {
        var cut = Render<Pager>(parameters => parameters.Add(pager => pager.Page, page).Add(pager => pager.PageSize, pageSize).Add(pager => pager.TotalCount, total).Add(pager => pager.HrefFor, n => $"/x?page={n}"));

        cut.Markup.Trim().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(1, 11, new string[] { "/x?page=2" }, "Page 1 of 2")]
    [InlineData(2, 11, new string[] { "/x?page=1" }, "Page 2 of 2")]
    [InlineData(2, 25, new string[] { "/x?page=1", "/x?page=3" }, "Page 2 of 3")]
    [InlineData(3, 25, new string[] { "/x?page=2" }, "Page 3 of 3")]
    [InlineData(1, 25, new string[] { "/x?page=2" }, "Page 1 of 3")]
    public void A_pager_links_to_the_previous_and_the_next_page_only_where_there_is_one(int page, int total, string[] links, string state)
    {
        var cut = Render<Pager>(parameters => parameters.Add(pager => pager.Page, page).Add(pager => pager.PageSize, 10).Add(pager => pager.TotalCount, total).Add(pager => pager.HrefFor, n => $"/x?page={n}"));

        cut.FindAll("nav.ts-pager a").Select(a => a.GetAttribute("href")).ShouldBe(links);
        cut.Find(".ts-pager-state").TextContent.ShouldBe(state);
        cut.Find("nav.ts-pager").GetAttribute("aria-label").ShouldBe("Pages");
        cut.FindAll("a[rel=prev]").Count.ShouldBe(page > 1 ? 1 : 0);
        cut.FindAll("a[rel=next]").Count.ShouldBe(page < (total + 9) / 10 ? 1 : 0);
    }

    [Fact]
    public void A_state_message_has_a_heading_and_its_own_content_and_is_a_status_not_an_alert()
    {
        var cut = Render<StateMessage>(parameters => parameters.Add(state => state.Heading, "No articles found").AddChildContent("<p>Try again.</p>"));

        cut.Find("section.ts-state").GetAttribute("role").ShouldBe("status");
        cut.Find("h2").TextContent.ShouldBe("No articles found");
        cut.Find("p").TextContent.ShouldBe("Try again.");
    }

    [Fact]
    public void The_search_box_is_a_labelled_get_form_and_puts_the_text_back_as_an_encoded_value()
    {
        var cut = Render<KbSearchBox>(parameters => parameters.Add(box => box.ProductKey, "paperplane").Add(box => box.Query, "\"><script>alert(1)</script>"));

        var form = cut.Find("form[role=search]");
        form.GetAttribute("method").ShouldBe("get");
        form.GetAttribute("action").ShouldBe("/p/paperplane/kb/search");
        cut.Find("label[for=kb-search]").TextContent.ShouldBe("Search help articles");
        var input = cut.Find("input#kb-search");
        input.GetAttribute("name").ShouldBe("q");
        input.GetAttribute("maxlength").ShouldBe("200");
        input.GetAttribute("value").ShouldBe("\"><script>alert(1)</script>");
        cut.FindAll("script").Count.ShouldBe(0);
        cut.Find("button[type=submit]").TextContent.ShouldBe("Search");
    }
}
```

`tests/TechStrap.Portal.Tests/Components/KbCopyTests.cs` (new)

```csharp
using TechStrap.Portal.Components;

namespace TechStrap.Portal.Tests.Components;

/// <summary>The few help-center sentences that are built from a value (a name, a count, a page, a day). Plain text: the page encodes whatever is put in.</summary>
public sealed class KbCopyTests
{
    [Fact]
    public void The_titles_and_descriptions_name_the_product_and_the_page()
    {
        KbCopy.HomeTitle("Paperplane").ShouldBe("Paperplane Help Center");
        KbCopy.HomeDescription("Paperplane").ShouldBe("Help articles and answers for Paperplane.");
        KbCopy.CategoryTitle("Accounts", "Paperplane", 1).ShouldBe("Accounts - Paperplane Help Center");
        KbCopy.CategoryTitle("Accounts", "Paperplane", 0).ShouldBe("Accounts - Paperplane Help Center");
        KbCopy.CategoryTitle("Accounts", "Paperplane", 3).ShouldBe("Accounts (page 3) - Paperplane Help Center");
        KbCopy.CategoryDescription("Accounts", "Paperplane").ShouldBe("Help articles about Accounts for Paperplane.");
        KbCopy.SearchTitle("Paperplane").ShouldBe("Search - Paperplane Help Center");
        KbCopy.SearchDescription("Paperplane").ShouldBe("Search the help articles for Paperplane.");
    }

    [Theory]
    [InlineData(0, "0 articles")]
    [InlineData(1, "1 article")]
    [InlineData(2, "2 articles")]
    [InlineData(1234, "1234 articles")]
    public void A_count_is_singular_only_for_one(int count, string expected) => KbCopy.ArticleCount(count).ShouldBe(expected);

    [Theory]
    [InlineData(0, "0 results")]
    [InlineData(1, "1 result")]
    [InlineData(25, "25 results")]
    public void A_result_count_is_singular_only_for_one(int count, string expected) => KbCopy.ResultCount(count).ShouldBe(expected);

    [Fact]
    public void The_page_of_text_and_the_updated_day_read_the_same_in_every_culture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            KbCopy.PageOf(2, 1234).ShouldBe("Page 2 of 1234");
            KbCopy.UpdatedOn(new DateTimeOffset(2026, 10, 5, 23, 30, 0, TimeSpan.FromHours(-5))).ShouldBe("Updated 6 Oct 2026", "the day is the UTC day, so the page reads the same everywhere");
            KbCopy.UpdatedOn(new DateTimeOffset(2026, 1, 9, 0, 0, 0, TimeSpan.Zero)).ShouldBe("Updated 9 Jan 2026");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }
}
```

`tests/TechStrap.Portal.Tests/Components/KbPagingTests.cs` (new)

```csharp
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Components.Kb;

namespace TechStrap.Portal.Tests.Components;

/// <summary>The page number and the search text of the help-center pages are read from text a visitor controls; neither may ever throw or send more than the API takes.</summary>
public sealed class KbPagingTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("2", 2)]
    [InlineData("10", 10)]
    [InlineData("9999", 9999)]
    [InlineData("0002", 2)]
    [InlineData("2147483647", int.MaxValue)]
    public void A_whole_number_of_one_or_more_is_the_page(string text, int expected) => KbPaging.Parse(text).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("+2")]
    [InlineData(" 2")]
    [InlineData("2 ")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("1e3")]
    [InlineData("2147483648")]
    [InlineData("99999999999999999999")]
    [InlineData("1,000")]
    public void Anything_else_is_page_one_and_never_throws(string? text) => KbPaging.Parse(text).ShouldBe(1);

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(-5, 10, 0)]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(25, 10, 3)]
    [InlineData(30, 10, 3)]
    [InlineData(5, 0, 0)]
    [InlineData(5, -1, 0)]
    [InlineData(int.MaxValue, 10, 214748365)]
    public void The_number_of_pages_rounds_up_and_a_nonsense_size_gives_none(int total, int size, int pages) => KbPaging.TotalPages(total, size).ShouldBe(pages);

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("  router  ", "router")]
    public void The_search_text_is_trimmed(string? text, string expected) => KbSearchText.Clean(text).ShouldBe(expected);

    [Fact]
    public void A_longer_text_is_cut_at_the_limit_without_splitting_a_surrogate_pair()
    {
        var plain = new string('a', 300);
        var pair = new string('a', KbLimits.MaxSearchTextChars - 1) + char.ConvertFromUtf32(0x1F600) + "tail";

        KbSearchText.Clean(plain).ShouldBe(new string('a', KbLimits.MaxSearchTextChars));
        KbSearchText.Clean(pair).ShouldBe(new string('a', KbLimits.MaxSearchTextChars - 1), "the cut would have fallen inside the pair, so the half pair is dropped");
        KbSearchText.Clean(new string('b', KbLimits.MaxSearchTextChars)).Length.ShouldBe(KbLimits.MaxSearchTextChars);
    }
}
```

`tests/TechStrap.Portal.Tests/Kb/KbCategoryHostTests.cs` (new)

```csharp
using System.Net;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Forms;
using TechStrap.Portal.Tests.Tickets;

namespace TechStrap.Portal.Tests.Kb;

/// <summary>
/// P09-T12 (a category page) at the host: the paged list, links that work without script, the head, and Review Focus 2: an unknown category, a category of another product, an empty one, a slug that is not a slug and a
/// page past the end are the one neutral 404, byte for byte, with no theme in it. Review Focus 1: every title and summary is plain text and encoded.
/// </summary>
public sealed class KbCategoryHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static PortalFactory WithList(int page = 1, int total = 3, params PublicKbArticleSummaryDto[] items)
    {
        var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoryArticlesPath, KbTestKit.Page(page, 10, total, items.Length == 0 ? [KbTestKit.Article(), KbTestKit.Article("change-email", "Change your email", null), KbTestKit.Article("shared-tips", "Shared tips", "For everyone", product: null)] : items));
        return factory;
    }

    [Fact]
    public async Task A_category_lists_its_articles_as_links_under_the_visitors_product_with_summary_and_day()
    {
        await using var factory = WithList();
        using var client = FormTestKit.Client(factory);

        var (response, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        KbTestKit.Texts(dom, "h1").ShouldBe(["Accounts"]);
        KbTestKit.Texts(dom, ".ts-kb-card h2").ShouldBe(["Reset your password", "Change your email", "Shared tips"]);
        KbTestKit.Links(dom, ".ts-kb-card h2 a").ShouldBe(
            ["/p/paperplane/kb/accounts/reset-password", "/p/paperplane/kb/accounts/change-email", "/p/paperplane/kb/accounts/shared-tips"],
            "a shared article is linked under the product the visitor is on");
        KbTestKit.Texts(dom, ".ts-kb-card .ts-kb-summary").ShouldBe(["How to reset it", "For everyone"]);
        KbTestKit.Texts(dom, ".ts-kb-card .ts-kb-meta").ShouldBe(["Updated 5 Oct 2026", "Updated 5 Oct 2026", "Updated 5 Oct 2026"]);
        KbTestKit.Texts(dom, "nav.ts-breadcrumbs li").ShouldBe(["Paperplane", "Help center", "Accounts"]);
        KbTestKit.Links(dom, "nav.ts-breadcrumbs a").ShouldBe(["/p/paperplane", "/p/paperplane/kb"]);
        dom.QuerySelectorAll("nav.ts-pager").Length.ShouldBe(0, "one page needs no pager");
        var sent = factory.Api.Requests.Single(request => request.Path == KbTestKit.CategoryArticlesPath);
        sent.Query.ShouldBe("?page=1&pageSize=10");
        sent.Client.ShouldBe(ApiClientNames.Read);
    }

    [Fact]
    public async Task A_title_and_a_summary_are_plain_text_and_encoded_never_markup()
    {
        await using var factory = WithList(1, 1, KbTestKit.Article("x", "<script>alert(1)</script> & <i>title</i>", "<img src=x onerror=alert(1)> \"summary\""));
        using var client = FormTestKit.Client(factory);

        var (_, html, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts", Ct);

        html.ShouldNotContain("<script>alert(1)");
        html.ShouldNotContain("<img src=x");
        html.ShouldNotContain("<i>title</i>");
        KbTestKit.Texts(dom, ".ts-kb-card h2")[0].ShouldBe("<script>alert(1)</script> & <i>title</i>");
        KbTestKit.Texts(dom, ".ts-kb-card .ts-kb-summary")[0].ShouldBe("<img src=x onerror=alert(1)> \"summary\"");
        dom.QuerySelectorAll(".ts-kb-card script, .ts-kb-card img, .ts-kb-card i").Length.ShouldBe(0);
    }

    [Fact]
    public async Task A_middle_page_links_to_the_previous_and_next_page_with_plain_links_and_page_one_has_no_query()
    {
        await using var factory = WithList(2, 25);
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts?page=2", Ct);

        KbTestKit.Links(dom, "nav.ts-pager a").ShouldBe(["/p/paperplane/kb/accounts", "/p/paperplane/kb/accounts?page=3"]);
        dom.QuerySelector("nav.ts-pager a[rel=prev]")!.GetAttribute("href").ShouldBe("/p/paperplane/kb/accounts");
        dom.QuerySelector("nav.ts-pager a[rel=next]")!.GetAttribute("href").ShouldBe("/p/paperplane/kb/accounts?page=3");
        dom.QuerySelector("nav.ts-pager .ts-pager-state")!.TextContent.ShouldBe("Page 2 of 3");
        dom.QuerySelectorAll("nav.ts-pager button, nav.ts-pager [onclick]").Length.ShouldBe(0, "paging works without script");
        factory.Api.Requests.Single(request => request.Path == KbTestKit.CategoryArticlesPath).Query.ShouldBe("?page=2&pageSize=10");
    }

    [Theory]
    [InlineData(1, 25, "/p/paperplane/kb/accounts?page=2", null)]
    [InlineData(3, 25, null, "/p/paperplane/kb/accounts?page=2")]
    public async Task The_first_page_has_no_previous_link_and_the_last_has_no_next_link(int page, int total, string? next, string? previous)
    {
        await using var factory = WithList(page, total);
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, $"/p/paperplane/kb/accounts?page={page}", Ct);

        (dom.QuerySelector("a[rel=next]")?.GetAttribute("href")).ShouldBe(next);
        (dom.QuerySelector("a[rel=prev]")?.GetAttribute("href")).ShouldBe(previous);
    }

    [Theory]
    [InlineData("?page=abc", "?page=1&pageSize=10")]
    [InlineData("?page=-3", "?page=1&pageSize=10")]
    [InlineData("?page=0", "?page=1&pageSize=10")]
    [InlineData("?page=", "?page=1&pageSize=10")]
    [InlineData("?page=1.5", "?page=1&pageSize=10")]
    [InlineData("?page=99999999999", "?page=1&pageSize=10")]
    [InlineData("?page=2&page=3", "?page=2&pageSize=10")]
    [InlineData("?PAGE=2", "?page=2&pageSize=10")]
    [InlineData("?utm=1", "?page=1&pageSize=10")]
    public async Task A_page_value_that_is_not_a_whole_number_is_page_one_and_never_a_500(string query, string expectedApiQuery)
    {
        await using var factory = WithList();
        using var client = FormTestKit.Client(factory);

        var (response, _, _) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts" + query, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, query);
        factory.Api.Requests.Single(request => request.Path == KbTestKit.CategoryArticlesPath).Query.ShouldBe(expectedApiQuery, query);
    }

    [Fact]
    public async Task The_head_is_unique_per_page_and_the_canonical_address_names_the_page()
    {
        await using var factory = WithList(2, 25);
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts?page=2&utm=x", Ct);

        dom.Title.ShouldBe("Accounts (page 2) - Paperplane Help Center");
        KbTestKit.Meta(dom, "meta[name=description]").ShouldBe("Help articles about Accounts for Paperplane.");
        dom.QuerySelector("link[rel=canonical]")!.GetAttribute("href").ShouldBe(PortalFactory.PublicUrl + "/p/paperplane/kb/accounts?page=2");
        KbTestKit.Meta(dom, "meta[name=robots]").ShouldStartWith("index, follow");
        KbTestKit.Meta(dom, "meta[property='og:type']").ShouldBe("website");
    }

    [Fact]
    public async Task The_first_page_title_has_no_page_number_and_the_canonical_has_no_query()
    {
        await using var factory = WithList();
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts", Ct);

        dom.Title.ShouldBe("Accounts - Paperplane Help Center");
        dom.QuerySelector("link[rel=canonical]")!.GetAttribute("href").ShouldBe(PortalFactory.PublicUrl + "/p/paperplane/kb/accounts");
    }

    [Theory]
    [InlineData("/p/paperplane/kb/accounts")]
    [InlineData("/p/paperplane/kb/accounts/")]
    [InlineData("/P/paperplane/KB/accounts")]
    public async Task A_category_answers_the_same_whatever_the_case_of_the_fixed_segments_or_a_trailing_slash(string path)
    {
        await using var factory = WithList();
        using var client = FormTestKit.Client(factory);

        var (response, _, dom) = await KbTestKit.GetAsync(client, path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        KbTestKit.Texts(dom, "h1").ShouldBe(["Accounts"]);
    }

    [Fact]
    public async Task An_unknown_invisible_or_empty_category_is_the_neutral_404_byte_for_byte_with_no_theme()
    {
        var neutral = await Seen.NeutralNotFoundAsync(Ct, "/p/nope");
        await using var factory = KbTestKit.Factory();
        KbTestKit.Problem(factory, "/api/public/kb/paperplane/categories/gone/articles", HttpStatusCode.NotFound);
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync("/p/paperplane/kb/gone", Ct);
        var seen = await Seen.OfAsync(response, factory.Api.Requests.Count, Ct);

        seen.ShouldBeTheNeutralNotFound(neutral);
        seen.Body.ShouldNotContain("Paperplane");
        factory.Api.Requests.Select(request => request.Path).ShouldBe([KbTestKit.ProductPath, "/api/public/kb/paperplane/categories/gone/articles"]);
    }

    [Theory]
    [InlineData("/p/paperplane/kb/Bad_Slug")]
    [InlineData("/p/paperplane/kb/UPPER")]
    [InlineData("/p/paperplane/kb/-x")]
    [InlineData("/p/paperplane/kb/x--y")]
    [InlineData("/p/paperplane/kb/a%20b")]
    [InlineData("/p/paperplane/kb/a%3Fpage%3D2")]
    public async Task A_category_slug_that_is_not_a_slug_is_the_neutral_404_and_the_category_is_never_asked_for(string path)
    {
        var neutral = await Seen.NeutralNotFoundAsync(Ct, "/p/nope");
        await using var factory = KbTestKit.Factory();
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync(path, Ct);
        var seen = await Seen.OfAsync(response, factory.Api.Requests.Count, Ct);

        seen.ShouldBeTheNeutralNotFound(neutral);
        factory.Api.Requests.Select(request => request.Path).ShouldBe([KbTestKit.ProductPath], path);
    }

    [Fact]
    public async Task A_page_past_the_end_is_the_neutral_404_and_not_an_empty_page_for_a_crawler_to_find()
    {
        var neutral = await Seen.NeutralNotFoundAsync(Ct, "/p/nope");
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoryArticlesPath, KbTestKit.Page(9, 10, 3));
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync("/p/paperplane/kb/accounts?page=9", Ct);
        var seen = await Seen.OfAsync(response, factory.Api.Requests.Count, Ct);

        seen.ShouldBeTheNeutralNotFound(neutral);
    }

    [Fact]
    public async Task The_search_page_is_not_a_category_the_literal_segment_wins()
    {
        await using var factory = KbTestKit.Factory();
        using var client = FormTestKit.Client(factory);

        var (response, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        KbTestKit.Texts(dom, ".ts-state h2").ShouldBe(["What are you looking for?"]);
        factory.Api.Requests.ShouldAllBe(request => !request.Path.Contains("/categories/", StringComparison.Ordinal), "search is not asked for as a category");
    }

    [Fact]
    public async Task A_failing_list_call_is_a_calm_503_and_a_429_is_a_429()
    {
        await using var down = KbTestKit.Factory();
        down.Api.OnStatus(HttpMethod.Get, KbTestKit.CategoryArticlesPath, HttpStatusCode.BadGateway);
        using var downClient = FormTestKit.Client(down);
        await using var busy = KbTestKit.Factory();
        busy.Api.OnStatus(HttpMethod.Get, KbTestKit.CategoryArticlesPath, HttpStatusCode.TooManyRequests);
        using var busyClient = FormTestKit.Client(busy);

        var (downResponse, downHtml, _) = await KbTestKit.GetAsync(downClient, "/p/paperplane/kb/accounts", Ct);
        var (busyResponse, busyHtml, _) = await KbTestKit.GetAsync(busyClient, "/p/paperplane/kb/accounts", Ct);

        downResponse.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        downHtml.ShouldContain("We could not reach our support system.");
        busyResponse.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        busyHtml.ShouldContain("You have sent a lot in a short time.");
    }

    [Fact]
    public async Task An_unknown_product_is_the_neutral_404_and_the_category_is_never_asked_for()
    {
        var neutral = await Seen.NeutralNotFoundAsync(Ct, "/p/nope");
        await using var factory = FormTestKit.Factory(product: false);
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/gone", HttpStatusCode.NotFound, "product-not-found", "No such product.");
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync("/p/gone/kb/accounts", Ct);
        var seen = await Seen.OfAsync(response, factory.Api.Requests.Count, Ct);

        seen.ShouldBeTheNeutralNotFound(neutral);
        factory.Api.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/public/products/gone");
    }
}
```

`tests/TechStrap.Portal.Tests/Kb/KbHomeHostTests.cs` (new)

```csharp
using System.Net;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Forms;
using TechStrap.Portal.Tests.Tickets;

namespace TechStrap.Portal.Tests.Kb;

/// <summary>
/// P09-T12 (the help-center home) at the host, with a stub API behind the Portal: the categories with their counts and descriptions, the empty state, the head, the neutral 404 for an unknown product (no KB call is made)
/// and the calm failure states. Review Focus 1 (names and descriptions are plain text and encoded) and 2 (an unknown product tells nothing).
/// </summary>
public sealed class KbHomeHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static PublicKbCategoryDto[] Categories() =>
    [
        new("accounts", "Accounts", "Sign-in, passwords and security", 4),
        new("billing", "Billing", null, 1),
    ];

    [Fact]
    public async Task The_home_lists_each_category_with_its_description_and_article_count_and_a_search_box()
    {
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoriesPath, Categories());
        using var client = FormTestKit.Client(factory);

        var (response, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        KbTestKit.Texts(dom, "h1").ShouldBe(["Help center"]);
        KbTestKit.Texts(dom, ".ts-kb-list h2").ShouldBe(["Accounts", "Billing"]);
        KbTestKit.Links(dom, ".ts-kb-list h2 a").ShouldBe(["/p/paperplane/kb/accounts", "/p/paperplane/kb/billing"]);
        KbTestKit.Texts(dom, ".ts-kb-list .ts-kb-summary").ShouldBe(["Sign-in, passwords and security"]);
        KbTestKit.Texts(dom, ".ts-kb-list .ts-kb-meta").ShouldBe(["4 articles", "1 article"]);
        KbTestKit.Texts(dom, "nav.ts-breadcrumbs li").ShouldBe(["Paperplane", "Help center"]);
        KbTestKit.Links(dom, "nav.ts-breadcrumbs a").ShouldBe(["/p/paperplane"]);
        dom.QuerySelector("nav.ts-breadcrumbs li[aria-current=page]")!.TextContent.ShouldBe("Help center");
        var form = dom.QuerySelector("form[role=search]")!;
        form.GetAttribute("method").ShouldBe("get");
        form.GetAttribute("action").ShouldBe("/p/paperplane/kb/search");
        form.QuerySelector("input[name=q]")!.GetAttribute("maxlength").ShouldBe("200");
        factory.Api.Requests.Select(request => request.Path).ShouldBe([KbTestKit.ProductPath, KbTestKit.CategoriesPath]);
        factory.Api.Requests.ShouldAllBe(request => request.Client == ApiClientNames.Read);
    }

    [Fact]
    public async Task A_name_and_a_description_are_plain_text_and_encoded_never_markup()
    {
        await using var factory = KbTestKit.Factory();
        const string Name = "<script>alert(1)</script> & <b>\"quoted\"</b>";
        const string Description = "<img src=x onerror=alert(1)> <a href=\"javascript:alert(2)\">click</a>";
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoriesPath, new[] { new PublicKbCategoryDto("accounts", Name, Description, 2) });
        using var client = FormTestKit.Client(factory);

        var (_, html, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb", Ct);

        html.ShouldNotContain("<script>alert(1)");
        html.ShouldNotContain("<img src=x");
        html.ShouldNotContain("<b>\"quoted\"");
        html.ShouldContain("&lt;script&gt;alert(1)&lt;/script&gt;");
        KbTestKit.Texts(dom, ".ts-kb-list h2")[0].ShouldBe(Name);
        KbTestKit.Texts(dom, ".ts-kb-summary")[0].ShouldBe(Description);
        dom.QuerySelectorAll(".ts-kb-list script, .ts-kb-list img, .ts-kb-list b").Length.ShouldBe(0);
        dom.QuerySelectorAll("a[href^=javascript]").Length.ShouldBe(0);
    }

    [Fact]
    public async Task A_product_with_no_category_shows_the_empty_state_with_a_way_to_contact_support_and_is_not_an_error()
    {
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoriesPath, Array.Empty<PublicKbCategoryDto>());
        using var client = FormTestKit.Client(factory);

        var (response, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        KbTestKit.Texts(dom, ".ts-state h2").ShouldBe(["No articles yet"]);
        KbTestKit.Links(dom, ".ts-state a").ShouldBe(["/p/paperplane/contact"]);
        dom.QuerySelectorAll(".ts-kb-list").Length.ShouldBe(0);
    }

    [Fact]
    public async Task The_head_has_a_unique_title_a_description_the_canonical_address_open_graph_and_allows_indexing()
    {
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoriesPath, Categories());
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb?utm_source=mail", Ct);

        dom.Title.ShouldBe("Paperplane Help Center");
        KbTestKit.Meta(dom, "meta[name=description]").ShouldBe("Help articles and answers for Paperplane.");
        dom.QuerySelector("link[rel=canonical]")!.GetAttribute("href").ShouldBe(PortalFactory.PublicUrl + "/p/paperplane/kb", "the canonical address has no query and comes from the public URL, never the Host header");
        KbTestKit.Meta(dom, "meta[name=robots]").ShouldStartWith("index, follow");
        KbTestKit.Meta(dom, "meta[property='og:title']").ShouldBe("Paperplane Help Center");
        KbTestKit.Meta(dom, "meta[property='og:url']").ShouldBe(PortalFactory.PublicUrl + "/p/paperplane/kb");
        KbTestKit.Meta(dom, "meta[property='og:image']").ShouldBe(PortalFactory.PublicUrl + "/icon-512.png", "a product with no logo uses the Portal's own image, never the bare site address");
        KbTestKit.Meta(dom, "meta[property='og:image:alt']").ShouldBe("Paperplane");
    }

    [Fact]
    public async Task The_open_graph_image_is_the_products_logo_when_it_has_an_acceptable_one()
    {
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.ProductPath, FormTestKit.Product() with { LogoPath = "https://cdn.example.com/paperplane.png" });
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoriesPath, Categories());
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb", Ct);

        KbTestKit.Meta(dom, "meta[property='og:image']").ShouldBe("https://cdn.example.com/paperplane.png");
    }

    [Fact]
    public async Task The_page_is_themed_with_the_products_header_and_sets_no_cookie()
    {
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoriesPath, Categories());
        using var client = FormTestKit.Client(factory);

        var (response, html, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb", Ct);

        html.ShouldContain("--ts-accent:#F59E0B");
        dom.QuerySelector("header.ts-product-header .ts-product-name")!.TextContent.Trim().ShouldBe("Paperplane");
        KbTestKit.Header(response, "Set-Cookie").ShouldBeEmpty("a cookie would stop the response being kept (and would need a notice)");
    }

    [Theory]
    [InlineData("/p/gone/kb")]
    [InlineData("/p/BAD_KEY/kb")]
    [InlineData("/p/-x/kb")]
    public async Task An_unknown_or_malformed_product_is_the_neutral_404_and_no_kb_call_is_made(string path)
    {
        var neutral = await Seen.NeutralNotFoundAsync(Ct, "/p/nope");
        await using var factory = FormTestKit.Factory(product: false);
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/gone", HttpStatusCode.NotFound, "product-not-found", "No such product.");
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync(path, Ct);
        var seen = await Seen.OfAsync(response, factory.Api.Requests.Count, Ct);

        seen.ShouldBeTheNeutralNotFound(neutral);
        factory.Api.Requests.ShouldAllBe(request => request.Path.StartsWith("/api/public/products/", StringComparison.Ordinal), "no KB endpoint is called for a product that is not there");
    }

    [Fact]
    public async Task A_failing_categories_call_is_a_calm_503_in_the_products_theme_and_a_429_is_a_429_and_the_apis_words_are_never_shown()
    {
        await using var down = KbTestKit.Factory();
        KbTestKit.Problem(down, KbTestKit.CategoriesPath, HttpStatusCode.InternalServerError, "kb-secret-table");
        using var downClient = FormTestKit.Client(down);
        await using var busy = KbTestKit.Factory();
        busy.Api.OnStatus(HttpMethod.Get, KbTestKit.CategoriesPath, HttpStatusCode.TooManyRequests);
        using var busyClient = FormTestKit.Client(busy);

        var (downResponse, downHtml, downDom) = await KbTestKit.GetAsync(downClient, "/p/paperplane/kb", Ct);
        var (busyResponse, busyHtml, _) = await KbTestKit.GetAsync(busyClient, "/p/paperplane/kb", Ct);

        downResponse.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        downHtml.ShouldContain(ProblemCopy.ApiUnavailable);
        downHtml.ShouldNotContain("kb-secret-table");
        downHtml.ShouldNotContain("Whatever the API says");
        downDom.QuerySelector("header.ts-product-header")!.TextContent.ShouldContain("Paperplane");
        busyResponse.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        busyHtml.ShouldContain(ProblemCopy.RateLimited);
        KbTestKit.Header(downResponse, "Cache-Control").ShouldNotContain("public, max-age=60");
    }
}
```

`tests/TechStrap.Portal.Tests/Kb/KbPageCacheHostTests.cs` (new)

```csharp
using System.Net;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Tests.Forms;

namespace TechStrap.Portal.Tests.Kb;

/// <summary>
/// P09-T15 (output caching) at the host, on the real pages and the stub API: a repeated request makes one API call, a hit still carries the request's own headers, a 404 or a 429 is never stored, nothing that is not a
/// help-center page is ever kept, and a cache key is never shared between products. <c>OutputCachePipelineTests</c> proves the mechanism on a small host; this proves it on the pages. Review Focus 3.
/// </summary>
public sealed class KbPageCacheHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static PortalFactory Factory()
    {
        var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoriesPath, new[] { new PublicKbCategoryDto("accounts", "Accounts", null, 3) });
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoryArticlesPath, KbTestKit.Page(1, 10, 1, KbTestKit.Article()));
        return factory;
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? correlationId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-Id", correlationId);
        }

        return await client.SendAsync(request, Ct);
    }

    [Theory]
    [InlineData("/p/paperplane/kb", KbTestKit.CategoriesPath)]
    [InlineData("/p/paperplane/kb/accounts", KbTestKit.CategoryArticlesPath)]
    public async Task A_help_centre_page_asked_twice_calls_the_api_once_for_the_product_and_once_for_its_data(string path, string dataPath)
    {
        await using var factory = Factory();
        using var client = FormTestKit.Client(factory);

        using var first = await GetAsync(client, path);
        using var second = await GetAsync(client, path);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Api.Count(HttpMethod.Get, KbTestKit.ProductPath).ShouldBe(1);
        factory.Api.Count(HttpMethod.Get, dataPath).ShouldBe(1);
        KbTestKit.Header(second, "Age").ShouldNotBeEmpty();
        (await second.Content.ReadAsStringAsync(Ct)).ShouldBe(await first.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_cached_page_carries_the_security_headers_the_browser_cache_header_and_the_requests_own_correlation_id()
    {
        await using var factory = Factory();
        using var client = FormTestKit.Client(factory);

        using var first = await GetAsync(client, "/p/paperplane/kb", "cid-one");
        using var second = await GetAsync(client, "/p/paperplane/kb", "cid-two");

        factory.Api.Count(HttpMethod.Get, KbTestKit.CategoriesPath).ShouldBe(1);
        KbTestKit.Header(first, "X-Correlation-Id").ShouldBe(["cid-one"]);
        KbTestKit.Header(second, "X-Correlation-Id").ShouldBe(["cid-two"]);
        foreach (var response in new[] { first, second })
        {
            KbTestKit.Header(response, "Content-Security-Policy").ShouldHaveSingleItem().ShouldContain("script-src 'self'");
            KbTestKit.Header(response, "X-Content-Type-Options").ShouldBe(["nosniff"]);
            response.Headers.CacheControl!.ToString().ShouldBe("public, max-age=60");
            KbTestKit.Header(response, "Set-Cookie").ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Only_the_page_value_changes_the_key_and_a_page_asked_with_other_query_values_is_the_same_page()
    {
        await using var factory = Factory();
        using var client = FormTestKit.Client(factory);

        using var a = await GetAsync(client, "/p/paperplane/kb/accounts");
        using var b = await GetAsync(client, "/p/paperplane/kb/accounts?utm_source=mail");
        using var c = await GetAsync(client, "/p/paperplane/kb/accounts?utm_source=other&x=1");

        factory.Api.Count(HttpMethod.Get, KbTestKit.CategoryArticlesPath).ShouldBe(1);

        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoryArticlesPath, KbTestKit.Page(2, 10, 15, KbTestKit.Article("change-email", "Change your email")));
        using var page2 = await GetAsync(client, "/p/paperplane/kb/accounts?page=2");
        using var page2Again = await GetAsync(client, "/p/paperplane/kb/accounts?page=2&utm=1");

        factory.Api.Count(HttpMethod.Get, KbTestKit.CategoryArticlesPath).ShouldBe(2);
        (await page2.Content.ReadAsStringAsync(Ct)).ShouldContain("Change your email");
        (await page2Again.Content.ReadAsStringAsync(Ct)).ShouldContain("Change your email");
    }

    [Fact]
    public async Task A_page_value_that_is_not_a_plain_page_number_is_never_stored()
    {
        await using var factory = Factory();
        using var client = FormTestKit.Client(factory);

        using var first = await GetAsync(client, "/p/paperplane/kb/accounts?page=abc");
        using var second = await GetAsync(client, "/p/paperplane/kb/accounts?page=abc");

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Api.Count(HttpMethod.Get, KbTestKit.CategoryArticlesPath).ShouldBe(2);
    }

    [Fact]
    public async Task A_404_is_never_stored_and_carries_no_public_cache_header()
    {
        await using var factory = Factory();
        KbTestKit.Problem(factory, "/api/public/kb/paperplane/categories/gone/articles", HttpStatusCode.NotFound);
        using var client = FormTestKit.Client(factory);

        using var first = await GetAsync(client, "/p/paperplane/kb/gone");
        using var second = await GetAsync(client, "/p/paperplane/kb/gone");

        foreach (var response in new[] { first, second })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            response.Headers.CacheControl?.Public.ShouldNotBe(true);
            KbTestKit.Header(response, "Age").ShouldBeEmpty();
        }

        factory.Api.Count(HttpMethod.Get, "/api/public/kb/paperplane/categories/gone/articles").ShouldBe(2);
    }

    [Fact]
    public async Task A_429_is_never_stored()
    {
        await using var factory = Factory();
        factory.Api.OnStatus(HttpMethod.Get, KbTestKit.CategoriesPath, HttpStatusCode.TooManyRequests);
        using var client = FormTestKit.Client(factory);

        using var first = await GetAsync(client, "/p/paperplane/kb");
        using var second = await GetAsync(client, "/p/paperplane/kb");

        first.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        second.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        factory.Api.Count(HttpMethod.Get, KbTestKit.CategoriesPath).ShouldBe(2);
        first.Headers.CacheControl?.Public.ShouldNotBe(true);
    }

    [Fact]
    public async Task The_search_page_the_product_home_and_the_form_pages_are_never_stored()
    {
        await using var factory = Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.SearchPath, KbTestKit.Hits(1, 10, 1, KbTestKit.Hit()));
        using var client = FormTestKit.Client(factory);

        foreach (var path in new[] { "/p/paperplane/kb/search?q=router", "/p/paperplane", "/p/paperplane/contact", "/p/paperplane/lost-link" })
        {
            using var first = await GetAsync(client, path);
            using var second = await GetAsync(client, path);
            first.StatusCode.ShouldBe(HttpStatusCode.OK, path);
            KbTestKit.Header(second, "Age").ShouldBeEmpty(path);
            first.Headers.CacheControl?.Public.ShouldNotBe(true, path);
        }

        factory.Api.Count(HttpMethod.Get, KbTestKit.SearchPath).ShouldBe(2);
        factory.Api.Count(HttpMethod.Get, KbTestKit.ProductPath).ShouldBe(8, "each of the four paths asked the API for the product twice");
    }

    [Fact]
    public async Task A_cache_key_is_never_shared_between_products_a_themed_page_is_always_its_own_products()
    {
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/acme", new PublicProductDto("acme", "Acme Anvils", null, "#F59E0B", "#000000", "#9D6507"));
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/orbitly", new PublicProductDto("orbitly", "Orbitly Orbits", null, "#7C3AED", "#FFFFFF", "#7C3AED"));
        factory.Api.OnJson(HttpMethod.Get, "/api/public/kb/acme/categories", new[] { new PublicKbCategoryDto("general", "General", null, 1) });
        factory.Api.OnJson(HttpMethod.Get, "/api/public/kb/orbitly/categories", new[] { new PublicKbCategoryDto("general", "General", null, 9) });
        using var client = FormTestKit.Client(factory);

        var acme = await (await GetAsync(client, "/p/acme/kb")).Content.ReadAsStringAsync(Ct);
        var orbitly = await (await GetAsync(client, "/p/orbitly/kb")).Content.ReadAsStringAsync(Ct);
        var acmeAgain = await (await GetAsync(client, "/p/acme/kb")).Content.ReadAsStringAsync(Ct);

        acme.ShouldContain("Acme Anvils");
        acme.ShouldNotContain("Orbitly");
        orbitly.ShouldContain("Orbitly Orbits");
        orbitly.ShouldContain("9 articles");
        orbitly.ShouldNotContain("Acme");
        acmeAgain.ShouldBe(acme);
        factory.Api.Count(HttpMethod.Get, "/api/public/kb/acme/categories").ShouldBe(1);
        factory.Api.Count(HttpMethod.Get, "/api/public/kb/orbitly/categories").ShouldBe(1);
    }

    [Fact]
    public async Task A_page_that_is_kept_never_sets_a_cookie_on_any_help_centre_page()
    {
        await using var factory = Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.SearchPath, KbTestKit.Hits(1, 10, 1, KbTestKit.Hit()));
        using var client = FormTestKit.Client(factory);

        foreach (var path in new[] { "/p/paperplane/kb", "/p/paperplane/kb/accounts", "/p/paperplane/kb/search", "/p/paperplane/kb/search?q=x" })
        {
            using var response = await GetAsync(client, path);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
            KbTestKit.Header(response, "Set-Cookie").ShouldBeEmpty(path);
        }
    }
}
```

`tests/TechStrap.Portal.Tests/Kb/KbSearchHostTests.cs` (new)

```csharp
using System.Net;
using System.Text.RegularExpressions;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Forms;
using TechStrap.Portal.Tests.Tickets;

namespace TechStrap.Portal.Tests.Kb;

/// <summary>
/// P09-T13 (the help-center search) at the host: a GET form that works without script, an empty-query prompt, a contact link when nothing matches, plain-text snippets, noindex for a query, paging links that keep the text,
/// and that nothing about the page is ever kept. Review Focus 1 (XSS): the text, every title and every snippet are plain text and encoded, in the results and in the box.
/// </summary>
public sealed class KbSearchHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static PortalFactory WithHits(int page = 1, int total = 2, params PublicKbSearchResultDto[] items)
    {
        var factory = KbTestKit.Factory();
        factory.Api.OnJson(
            HttpMethod.Get,
            KbTestKit.SearchPath,
            KbTestKit.Hits(page, 10, total, items.Length == 0 ? [KbTestKit.Hit(), KbTestKit.Hit("change-email", "Change your email", "Open <b>settings</b>.", "accounts", "Accounts")] : items));
        return factory;
    }

    [Fact]
    public async Task An_empty_query_shows_the_prompt_makes_no_search_call_and_stays_indexable()
    {
        await using var factory = KbTestKit.Factory();
        using var client = FormTestKit.Client(factory);

        var (response, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        KbTestKit.Texts(dom, ".ts-state h2").ShouldBe(["What are you looking for?"]);
        dom.QuerySelector("form[role=search] input[name=q]")!.GetAttribute("value").ShouldBeNullOrEmpty();
        KbTestKit.Meta(dom, "meta[name=robots]").ShouldStartWith("index, follow");
        factory.Api.Requests.Select(request => request.Path).ShouldBe([KbTestKit.ProductPath], "an empty text is never sent to the API");
    }

    [Theory]
    [InlineData("?q=")]
    [InlineData("?q=%20%20")]
    [InlineData("?utm=1")]
    public async Task A_blank_text_is_an_empty_query(string query)
    {
        await using var factory = KbTestKit.Factory();
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search" + query, Ct);

        KbTestKit.Texts(dom, ".ts-state h2").ShouldBe(["What are you looking for?"]);
        factory.Api.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_query_lists_the_results_as_cards_with_a_plain_text_snippet_the_category_and_the_count_and_is_noindex()
    {
        await using var factory = WithHits();
        using var client = FormTestKit.Client(factory);

        var (response, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search?q=reset%20password", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        KbTestKit.Texts(dom, ".ts-kb-card h2").ShouldBe(["Reset your password", "Change your email"]);
        KbTestKit.Links(dom, ".ts-kb-card h2 a").ShouldBe(["/p/paperplane/kb/accounts/reset-password", "/p/paperplane/kb/accounts/change-email"]);
        KbTestKit.Texts(dom, ".ts-kb-card .ts-kb-summary").ShouldBe(["Use the reset link.", "Open <b>settings</b>."]);
        KbTestKit.Texts(dom, ".ts-kb-card .ts-kb-meta").ShouldBe(["Accounts", "Accounts"]);
        KbTestKit.Texts(dom, "p.ts-kb-meta").ShouldContain("2 results");
        dom.QuerySelector("form[role=search] input[name=q]")!.GetAttribute("value").ShouldBe("reset password");
        KbTestKit.Meta(dom, "meta[name=robots]").ShouldBe("noindex, nofollow");
        dom.Title.ShouldBe("Search - Paperplane Help Center");
        dom.QuerySelector("link[rel=canonical]")!.GetAttribute("href").ShouldBe(PortalFactory.PublicUrl + "/p/paperplane/kb/search", "the canonical address has no text");
        var sent = factory.Api.Requests.Single(request => request.Path == KbTestKit.SearchPath);
        sent.Query.ShouldBe("?q=reset%20password&page=1&pageSize=10");
        sent.Client.ShouldBe(ApiClientNames.Read);
    }

    [Fact]
    public async Task A_snippet_with_markup_in_it_is_shown_as_text_never_as_markup()
    {
        await using var factory = WithHits(1, 1, KbTestKit.Hit("x", "<script>alert(1)</script>", "<img src=x onerror=alert(1)> <b>bold</b> &amp;", "a", "<u>Cat</u>"));
        using var client = FormTestKit.Client(factory);

        var (_, html, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search?q=x", Ct);

        html.ShouldNotContain("<script>alert(1)");
        html.ShouldNotContain("<img src=x");
        html.ShouldNotContain("<b>bold</b>");
        html.ShouldNotContain("<u>Cat</u>");
        KbTestKit.Texts(dom, ".ts-kb-card h2")[0].ShouldBe("<script>alert(1)</script>");
        KbTestKit.Texts(dom, ".ts-kb-card .ts-kb-summary")[0].ShouldBe("<img src=x onerror=alert(1)> <b>bold</b> &amp;");
        dom.QuerySelectorAll(".ts-kb-list script, .ts-kb-list img, .ts-kb-list b, .ts-kb-list u").Length.ShouldBe(0);
    }

    [Fact]
    public async Task The_text_the_visitor_typed_is_encoded_in_the_box_the_links_and_the_results()
    {
        await using var factory = WithHits(2, 25);
        const string Typed = "\"><script>alert(1)</script> & a=b#c";
        using var client = FormTestKit.Client(factory);

        var (_, html, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search?q=" + Uri.EscapeDataString(Typed) + "&page=2", Ct);

        html.ShouldNotContain("<script>alert(1)");
        dom.QuerySelector("form[role=search] input[name=q]")!.GetAttribute("value").ShouldBe(Typed);
        dom.QuerySelectorAll("script:not([src])").Length.ShouldBe(0);
        KbTestKit.Links(dom, "nav.ts-pager a").ShouldBe(
        [
            "/p/paperplane/kb/search?q=" + Uri.EscapeDataString(Typed),
            "/p/paperplane/kb/search?q=" + Uri.EscapeDataString(Typed) + "&page=3",
        ]);
        factory.Api.Requests.Single(request => request.Path == KbTestKit.SearchPath).Query.ShouldBe("?q=" + Uri.EscapeDataString(Typed) + "&page=2&pageSize=10");
    }

    [Fact]
    public async Task No_result_shows_a_message_and_a_link_to_contact_support()
    {
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.SearchPath, KbTestKit.Hits(1, 10, 0));
        using var client = FormTestKit.Client(factory);

        var (response, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search?q=zzz", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        KbTestKit.Texts(dom, ".ts-state h2").ShouldBe(["No articles found"]);
        KbTestKit.Links(dom, ".ts-state a").ShouldBe(["/p/paperplane/contact"]);
        dom.QuerySelectorAll(".ts-kb-card").Length.ShouldBe(0);
        KbTestKit.Meta(dom, "meta[name=robots]").ShouldBe("noindex, nofollow");
    }

    [Fact]
    public async Task The_paging_links_keep_the_text_and_work_without_script()
    {
        await using var factory = WithHits(2, 25);
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search?q=router&page=2", Ct);

        dom.QuerySelector("a[rel=prev]")!.GetAttribute("href").ShouldBe("/p/paperplane/kb/search?q=router");
        dom.QuerySelector("a[rel=next]")!.GetAttribute("href").ShouldBe("/p/paperplane/kb/search?q=router&page=3");
        dom.QuerySelector(".ts-pager-state")!.TextContent.ShouldBe("Page 2 of 3");
    }

    [Theory]
    [InlineData("&page=abc", 1)]
    [InlineData("&page=-2", 1)]
    [InlineData("&page=99999999999", 1)]
    [InlineData("&page=4", 4)]
    public async Task A_page_value_that_is_not_a_whole_number_is_page_one_and_never_a_500(string page, int expected)
    {
        await using var factory = WithHits(expected, 80);
        using var client = FormTestKit.Client(factory);

        var (response, _, _) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search?q=router" + page, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, page);
        factory.Api.Requests.Single(request => request.Path == KbTestKit.SearchPath).Query.ShouldBe($"?q=router&page={expected}&pageSize=10", page);
    }

    [Fact]
    public async Task A_text_longer_than_the_limit_is_cut_at_two_hundred_characters_before_it_is_sent()
    {
        await using var factory = WithHits();
        using var client = FormTestKit.Client(factory);

        await KbTestKit.GetAsync(client, "/p/paperplane/kb/search?q=" + new string('a', 300), Ct);

        factory.Api.Requests.Single(request => request.Path == KbTestKit.SearchPath).Query.ShouldBe($"?q={new string('a', 200)}&page=1&pageSize=10");
    }

    [Fact]
    public async Task The_page_is_never_kept_by_the_server_or_the_browser()
    {
        await using var factory = WithHits();
        using var client = FormTestKit.Client(factory);

        var (first, _, _) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search?q=router", Ct);
        var (second, _, _) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search?q=router", Ct);

        factory.Api.Count(HttpMethod.Get, KbTestKit.SearchPath).ShouldBe(2);
        factory.Api.Count(HttpMethod.Get, KbTestKit.ProductPath).ShouldBe(2);
        KbTestKit.Header(second, "Age").ShouldBeEmpty();
        first.Headers.CacheControl!.NoStore.ShouldBeTrue();
        KbTestKit.Header(first, "Set-Cookie").ShouldBeEmpty();
    }

    [Fact]
    public async Task The_search_form_is_a_get_form_and_the_page_has_no_inline_script()
    {
        await using var factory = WithHits();
        using var client = FormTestKit.Client(factory);

        var (_, html, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search?q=router", Ct);

        var form = dom.QuerySelector("form[role=search]")!;
        form.GetAttribute("method").ShouldBe("get");
        form.GetAttribute("action").ShouldBe("/p/paperplane/kb/search");
        form.QuerySelectorAll("input[type=hidden]").Length.ShouldBe(0, "no antiforgery token: a GET form sets no cookie");
        Regex.IsMatch(html, @"<script(?![^>]*\bsrc=)").ShouldBeFalse("the CSP allows no inline script");
    }

    [Fact]
    public async Task A_failing_search_is_a_calm_503_and_a_429_is_a_429()
    {
        await using var down = KbTestKit.Factory();
        down.Api.OnStatus(HttpMethod.Get, KbTestKit.SearchPath, HttpStatusCode.GatewayTimeout);
        using var downClient = FormTestKit.Client(down);
        await using var busy = KbTestKit.Factory();
        busy.Api.OnStatus(HttpMethod.Get, KbTestKit.SearchPath, HttpStatusCode.TooManyRequests);
        using var busyClient = FormTestKit.Client(busy);

        var (downResponse, downHtml, _) = await KbTestKit.GetAsync(downClient, "/p/paperplane/kb/search?q=x", Ct);
        var (busyResponse, busyHtml, _) = await KbTestKit.GetAsync(busyClient, "/p/paperplane/kb/search?q=x", Ct);

        downResponse.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        downHtml.ShouldContain("We could not reach our support system.");
        busyResponse.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        busyHtml.ShouldContain("You have sent a lot in a short time.");
    }

    [Fact]
    public async Task An_unknown_product_is_the_neutral_404_and_the_search_is_never_asked_for()
    {
        // The search page's own header rule (never stored) applies to every status on its path, so the neutral page to compare with is the one an unknown product gets at that same path.
        var neutral = await Seen.NeutralNotFoundAsync(Ct, "/p/nope/kb/search?q=router");
        await using var factory = FormTestKit.Factory(product: false);
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/gone", HttpStatusCode.NotFound, "product-not-found", "No such product.");
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync("/p/gone/kb/search?q=router", Ct);
        var seen = await Seen.OfAsync(response, factory.Api.Requests.Count, Ct);

        seen.ShouldBeTheNeutralNotFound(neutral);
        factory.Api.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/public/products/gone");
    }
}
```

`tests/TechStrap.Portal.Tests/Kb/KbTestKit.cs` (new)

```csharp
using System.Net;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Portal.Tests.Forms;

namespace TechStrap.Portal.Tests.Kb;

/// <summary>
/// What the help-center host tests share: the paperplane product behind the stub API (<see cref="FormTestKit"/>, whose factory asserts that every API call carried the visitor's address), the three API paths the pages
/// read, builders for the DTOs, and a parsed document, so a test asserts on elements and attributes, not on strings of markup.
/// </summary>
internal static class KbTestKit
{
    public const string CategoriesPath = "/api/public/kb/paperplane/categories";
    public const string CategoryArticlesPath = "/api/public/kb/paperplane/categories/accounts/articles";
    public const string SearchPath = "/api/public/kb/paperplane/search";
    public const string ProductPath = "/api/public/products/paperplane";

    public static readonly DateTimeOffset Updated = new(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);

    public static PortalFactory Factory(string environment = "Development") => FormTestKit.Factory(environment);

    public static IHtmlDocument Parse(string html) => new HtmlParser().ParseDocument(html);

    public static async Task<(HttpResponseMessage Response, string Html, IHtmlDocument Dom)> GetAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(path, cancellationToken);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        return (response, html, Parse(html));
    }

    public static PublicKbArticleSummaryDto Article(
        string slug = "reset-password", string title = "Reset your password", string? summary = "How to reset it", string category = "accounts", string categoryName = "Accounts", string? product = "paperplane") =>
        new(slug, title, summary, category, categoryName, product, Updated);

    public static PagedResponse<PublicKbArticleSummaryDto> Page(int page, int pageSize, int total, params PublicKbArticleSummaryDto[] items) => new(items, page, pageSize, total);

    public static PublicKbSearchResultDto Hit(string slug = "reset-password", string title = "Reset your password", string snippet = "Use the reset link.", string category = "accounts", string categoryName = "Accounts") =>
        new(slug, title, snippet, category, categoryName, "paperplane");

    public static PagedResponse<PublicKbSearchResultDto> Hits(int page, int pageSize, int total, params PublicKbSearchResultDto[] items) => new(items, page, pageSize, total);

    public static void Problem(PortalFactory factory, string path, HttpStatusCode status, string code = "kb-category-not-found") =>
        factory.Api.OnProblem(HttpMethod.Get, path, status, code, "Whatever the API says: never shown to a visitor.");

    public static string[] Texts(IHtmlDocument dom, string selector) => [.. dom.QuerySelectorAll(selector).Select(element => element.TextContent.Trim())];

    public static string[] Links(IHtmlDocument dom, string selector) => [.. dom.QuerySelectorAll(selector).Select(element => element.GetAttribute("href") ?? string.Empty)];

    public static string Meta(IHtmlDocument dom, string selector) => dom.QuerySelector(selector)?.GetAttribute("content") ?? string.Empty;

    public static string[] Header(HttpResponseMessage response, string name) => response.Headers.TryGetValues(name, out var values) ? [.. values] : [];
}
```

`tests/TechStrap.Portal.Tests/Routing/PortalRoutesTests.cs`

```diff
@@ -33,6 +33,26 @@ public sealed class PortalRoutesTests
         PortalRoutes.KbSearch("paperplane").ShouldBe("/p/paperplane/kb/search");
     }
 
+    [Theory]
+    [InlineData("paperplane", "accounts", 0, "/p/paperplane/kb/accounts")]
+    [InlineData("paperplane", "accounts", 1, "/p/paperplane/kb/accounts")]
+    [InlineData("paperplane", "accounts", 2, "/p/paperplane/kb/accounts?page=2")]
+    [InlineData("paperplane", "accounts", 120, "/p/paperplane/kb/accounts?page=120")]
+    [InlineData("paperplane", "a/b?c#d", 2, "/p/paperplane/kb/a%2Fb%3Fc%23d?page=2")]
+    public void A_category_page_builds_one_address_per_page_and_page_one_has_no_query(string key, string category, int page, string expected) =>
+        PortalRoutes.KbCategory(key, category, page).ShouldBe(expected);
+
+    [Theory]
+    [InlineData("router", 1, "/p/paperplane/kb/search?q=router")]
+    [InlineData("router", 0, "/p/paperplane/kb/search?q=router")]
+    [InlineData("router", 3, "/p/paperplane/kb/search?q=router&page=3")]
+    [InlineData("a b&c=d#e", 2, "/p/paperplane/kb/search?q=a%20b%26c%3Dd%23e&page=2")]
+    [InlineData("\"><script>", 1, "/p/paperplane/kb/search?q=%22%3E%3Cscript%3E")]
+    [InlineData("", 2, "/p/paperplane/kb/search")]
+    [InlineData("   ", 1, "/p/paperplane/kb/search")]
+    public void A_search_page_builds_the_text_escaped_so_it_can_never_add_a_parameter_and_a_blank_text_is_the_search_page_itself(string text, int page, string expected) =>
+        PortalRoutes.KbSearch("paperplane", text, page).ShouldBe(expected);
+
     [Fact]
     public void The_templates_match_the_routes_of_the_spec_exactly()
     {
```


- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build tests/TechStrap.Portal.Tests -c Release
```

Expected (recorded with the source files set aside):

```text
error CS0234: The type or namespace name 'Kb' does not exist in the namespace 'TechStrap.Portal.Components'
```

- [ ] **Step 3: Write the implementation**

The page base gains the two helpers, the routes gain the paged builders, the search text moves out of the suggest adapter, and the pages and components arrive. A page branches on `UnavailableMessage` first, then on its loaded data, so an unknown product (no theme, no message) and a not-found after theming render nothing and the router's neutral page shows. The new SCSS is structure only (lists, the breadcrumb trail, the pager); the visual polish is 09d.

`src/TechStrap.Portal/Components/Kb/KbArticleCard.razor` (new)

```razor
<article class="ts-kb-card">
    <h2><a href="@Href">@Title</a></h2>
    @if (!string.IsNullOrWhiteSpace(Summary))
    {
        <p class="ts-kb-summary">@Summary</p>
    }
    @if (Meta is not null)
    {
        <p class="ts-kb-meta">@Meta</p>
    }
</article>

@code {
    /// <summary>The article's address: a path of this site built by <c>PortalRoutes.KbArticle</c>.</summary>
    [Parameter, EditorRequired]
    public string Href { get; set; } = string.Empty;

    /// <summary>Plain text, encoded when shown.</summary>
    [Parameter, EditorRequired]
    public string Title { get; set; } = string.Empty;

    /// <summary>The author's summary or a search snippet: plain text, never markup, encoded when shown.</summary>
    [Parameter]
    public string? Summary { get; set; }

    /// <summary>A short line under the summary (the category, the day it changed): plain text.</summary>
    [Parameter]
    public string? Meta { get; set; }
}
```

`src/TechStrap.Portal/Components/Kb/KbBreadcrumbs.razor` (new)

```razor
@if (Crumbs.Count > 0)
{
    <nav class="ts-breadcrumbs" aria-label="@KbCopy.BreadcrumbLabel">
        <ol>
            @foreach (var crumb in Crumbs)
            {
                @if (crumb.Href is { } href)
                {
                    <li><a href="@href">@crumb.Label</a></li>
                }
                else
                {
                    <li aria-current="page">@crumb.Label</li>
                }
            }
        </ol>
    </nav>
}

@code {
    /// <summary>The trail from the product's home to this page. The last step has no address: it is the page itself.</summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<KbCrumb> Crumbs { get; set; } = [];
}
```

`src/TechStrap.Portal/Components/Kb/KbCrumb.cs` (new)

```csharp
namespace TechStrap.Portal.Components.Kb;

/// <summary>One step of a breadcrumb trail: a plain-text label and, for every step but the last, the address it links to (a path of this site).</summary>
public sealed record KbCrumb(string Label, string? Href = null);
```

`src/TechStrap.Portal/Components/Kb/KbPaging.cs` (new)

```csharp
using System.Globalization;

namespace TechStrap.Portal.Components.Kb;

/// <summary>
/// The page number of a paged help-center list, read from text. The page is bound as text and parsed here, because the framework's own binding to a number answers 500 for <c>?page=abc</c> or a number that
/// does not fit (the spike). Anything that is not a whole number of one or more is page one, so a link someone mangled still shows the first page and nothing throws.
/// </summary>
public static class KbPaging
{
    public static int Parse(string? text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var page) && page >= 1 ? page : 1;

    /// <summary>The number of pages of <paramref name="totalCount"/> items, <paramref name="pageSize"/> to a page (zero for none).</summary>
    public static int TotalPages(int totalCount, int pageSize) => pageSize < 1 || totalCount < 1 ? 0 : (int)(((long)totalCount + pageSize - 1) / pageSize);
}
```

`src/TechStrap.Portal/Components/Kb/KbSearchBox.razor` (new)

```razor
<form method="get" action="@PortalRoutes.KbSearch(ProductKey)" role="search" class="ts-help-search">
    <label for="kb-search" class="form-label">@ShellCopy.SearchLabel</label>
    <div class="ts-help-search-row">
        <input id="kb-search" name="@PortalRoutes.QueryParameter" type="search" class="form-control" maxlength="@KbLimits.MaxSearchTextChars" autocomplete="off" value="@Query" />
        <button type="submit" class="btn btn-primary">@ShellCopy.SearchButton</button>
    </div>
</form>

@code {
    [Parameter, EditorRequired]
    public string ProductKey { get; set; } = string.Empty;

    /// <summary>The text to put back in the box (what the visitor searched for): shown as an attribute value, so Razor encodes it.</summary>
    [Parameter]
    public string? Query { get; set; }
}
```

`src/TechStrap.Portal/Components/Kb/KbSearchText.cs` (new)

```csharp
using TechStrap.Contracts.Kb;

namespace TechStrap.Portal.Components.Kb;

/// <summary>The search text the Portal sends on: trimmed and cut at the API's own limit without splitting a surrogate pair. Both the search page and the suggest adapter use it, so they cut alike.</summary>
public static class KbSearchText
{
    public static string Clean(string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length <= KbLimits.MaxSearchTextChars)
        {
            return trimmed;
        }

        var cut = trimmed[..KbLimits.MaxSearchTextChars];
        return char.IsHighSurrogate(cut[^1]) ? cut[..^1] : cut;
    }
}
```

`src/TechStrap.Portal/Components/KbCopy.cs` (new)

```csharp
using System.Globalization;

namespace TechStrap.Portal.Components;

/// <summary>
/// The words of the help-center pages (PHASE-09c). Plain copy, no humor (BRAND.md), the product's name only where a page needs it. A page references these and never writes a sentence of its own. Everything a
/// visitor typed or an agent wrote (a search text, a category name, an article title) is passed in as an argument and shown by Razor, which encodes it; none of it is ever markup.
/// </summary>
public static class KbCopy
{
    // The help-center home.
    public const string HomeHeading = "Help center";
    public const string EmptyHomeHeading = "No articles yet";
    public const string EmptyHomeText = "There are no help articles for this product yet. If you need help, contact support.";

    // Navigation.
    public const string BreadcrumbLabel = "Breadcrumb";
    public const string PagerLabel = "Pages";
    public const string PreviousPage = "Previous page";
    public const string NextPage = "Next page";

    // Search.
    public const string SearchHeading = "Search results";
    public const string SearchPromptHeading = "What are you looking for?";
    public const string SearchPromptText = "Type a few words about your question and search the help articles.";
    public const string NoResultsHeading = "No articles found";
    public const string NoResultsText = "Try different words, or contact support and we will help you.";
    public const string ContactUs = "Contact support";

    public static string HomeTitle(string productName) => $"{productName} Help Center";

    public static string HomeDescription(string productName) => $"Help articles and answers for {productName}.";

    public static string CategoryTitle(string categoryName, string productName, int page) =>
        page <= 1 ? $"{categoryName} - {productName} Help Center" : $"{categoryName} (page {page.ToString(CultureInfo.InvariantCulture)}) - {productName} Help Center";

    public static string CategoryDescription(string categoryName, string productName) => $"Help articles about {categoryName} for {productName}.";

    public static string SearchTitle(string productName) => $"Search - {productName} Help Center";

    public static string SearchDescription(string productName) => $"Search the help articles for {productName}.";

    public static string ArticleCount(int count) => count == 1 ? "1 article" : $"{count.ToString(CultureInfo.InvariantCulture)} articles";

    public static string ResultCount(int total) => total == 1 ? "1 result" : $"{total.ToString(CultureInfo.InvariantCulture)} results";

    public static string PageOf(int page, int pages) => $"Page {page.ToString(CultureInfo.InvariantCulture)} of {pages.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The day an article was last changed, in UTC, so the page reads the same everywhere.</summary>
    public static string UpdatedOn(DateTimeOffset moment) => $"Updated {moment.UtcDateTime.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";
}
```

`src/TechStrap.Portal/Components/Pages/KbCategory.razor` (new)

```razor
@attribute [Route(PortalRoutes.KbCategoryTemplate)]
@inherits ProductPageBase

@if (UnavailableMessage is not null)
{
    <PageTitle>@ShellCopy.UnavailableTitle</PageTitle>
    <ProductUnavailable Message="@UnavailableMessage" RetryHref="@RetryHref" />
}
else if (Theme is { } theme && Listing is { } listing)
{
    var name = listing.Items[0].CategoryName;
    <SeoHead Title="@KbCopy.CategoryTitle(name, theme.DisplayName, listing.Page)" Description="@KbCopy.CategoryDescription(name, theme.DisplayName)" RelativeUrl="@PortalRoutes.KbCategory(theme.Key, Category, listing.Page)" ImageUrl="@KbSeo.Image(theme)" ImageAlt="@theme.DisplayName" />
    <KbBreadcrumbs Crumbs="@Crumbs(theme, name)" />
    <h1>@name</h1>
    <ul class="ts-kb-list">
        @foreach (var article in listing.Items)
        {
            <li><KbArticleCard Href="@PortalRoutes.KbArticle(theme.Key, article.CategorySlug, article.Slug)" Title="@article.Title" Summary="@article.Summary" Meta="@KbCopy.UpdatedOn(article.UpdatedAt)" /></li>
        }
    </ul>
    <Pager Page="@listing.Page" PageSize="@listing.PageSize" TotalCount="@listing.TotalCount" HrefFor="@(page => PortalRoutes.KbCategory(theme.Key, Category, page))" />
}
```

`src/TechStrap.Portal/Components/Pages/KbCategory.razor.cs` (new)

```csharp
using Microsoft.AspNetCore.Components;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Components.Kb;
using TechStrap.Portal.Products;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Components.Pages;

/// <summary>
/// One category of a product's help center (P09-T12): a page of its published articles, newest update first, <see cref="PageSize"/> to a page, with plain <c>?page=n</c> links that work without script. Review Focus 2:
/// an unknown category, another product's category, an empty one, a slug that is not a slug and a page past the end are all the neutral 404, byte for byte the page an unknown route gets (the product is forgotten first), and
/// a slug that is not a slug is answered without a call (the client refuses it: <see cref="IPublicKbClient"/>; a check of the page's own was dead code, as a surviving mutation showed). The page number is bound as text and parsed by <see cref="KbPaging"/>, because the framework's own number binding answers 500 for <c>?page=abc</c>.
/// </summary>
public partial class KbCategory : ProductPageBase
{
    public const int PageSize = KbLimits.DefaultPublicSearchPageSize;

    [Parameter]
    public string Category { get; set; } = string.Empty;

    [SupplyParameterFromQuery(Name = PortalRoutes.PageParameter)]
    private string? PageText { get; set; }

    [Inject]
    private IPublicKbClient Kb { get; set; } = default!;

    private PagedResponse<PublicKbArticleSummaryDto>? Listing { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (Theme is null)
        {
            return;
        }

        var result = await Kb.ListCategoryArticlesAsync(Key, Category, KbPaging.Parse(PageText), PageSize, RequestAborted);
        if (result.IsFailure)
        {
            Fail(result.Errors[0]);
            return;
        }

        if (result.Value.Items.Count == 0)
        {
            // A page past the end: the same 404 as a category that does not exist (a 200 for every page number would be a page per number for a crawler to find).
            NotFoundAfterTheming();
            return;
        }

        Listing = result.Value;
    }

    private static IReadOnlyList<KbCrumb> Crumbs(ProductThemeViewModel theme, string categoryName) =>
        [new KbCrumb(theme.DisplayName, PortalRoutes.ProductHome(theme.Key)), new KbCrumb(KbCopy.HomeHeading, PortalRoutes.KbHome(theme.Key)), new KbCrumb(categoryName)];
}
```

`src/TechStrap.Portal/Components/Pages/KbHome.razor` (new)

```razor
@attribute [Route(PortalRoutes.KbHomeTemplate)]
@inherits ProductPageBase

@if (UnavailableMessage is not null)
{
    <PageTitle>@ShellCopy.UnavailableTitle</PageTitle>
    <ProductUnavailable Message="@UnavailableMessage" RetryHref="@RetryHref" />
}
else if (Theme is { } theme && Categories is { } categories)
{
    <SeoHead Title="@KbCopy.HomeTitle(theme.DisplayName)" Description="@KbCopy.HomeDescription(theme.DisplayName)" RelativeUrl="@PortalRoutes.KbHome(theme.Key)" ImageUrl="@KbSeo.Image(theme)" ImageAlt="@theme.DisplayName" />
    <KbBreadcrumbs Crumbs="@Crumbs(theme)" />
    <h1>@KbCopy.HomeHeading</h1>
    <KbSearchBox ProductKey="@theme.Key" />
    @if (categories.Count == 0)
    {
        <StateMessage Heading="@KbCopy.EmptyHomeHeading">
            <p>@KbCopy.EmptyHomeText <a href="@PortalRoutes.Contact(theme.Key)">@KbCopy.ContactUs</a></p>
        </StateMessage>
    }
    else
    {
        <ul class="ts-kb-list">
            @foreach (var category in categories)
            {
                <li>
                    <article class="ts-kb-card">
                        <h2><a href="@PortalRoutes.KbCategory(theme.Key, category.Slug)">@category.Name</a></h2>
                        @if (!string.IsNullOrWhiteSpace(category.Description))
                        {
                            <p class="ts-kb-summary">@category.Description</p>
                        }
                        <p class="ts-kb-meta">@KbCopy.ArticleCount(category.ArticleCount)</p>
                    </article>
                </li>
            }
        </ul>
    }
}
```

`src/TechStrap.Portal/Components/Pages/KbHome.razor.cs` (new)

```csharp
using Microsoft.AspNetCore.Components;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Components.Kb;
using TechStrap.Portal.Products;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Components.Pages;

/// <summary>
/// The help-center home of a product (P09-T12): the categories it can see, each with its published article count and description, through <see cref="IPublicKbClient"/>. The product loads first (the base class): an unknown,
/// inactive or malformed product is the neutral 404 before any KB call is made. A product with no category shows the empty state, not an error. Every name and description is plain text and encoded.
/// </summary>
public partial class KbHome : ProductPageBase
{
    [Inject]
    private IPublicKbClient Kb { get; set; } = default!;

    private IReadOnlyList<PublicKbCategoryDto>? Categories { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (Theme is null)
        {
            return;
        }

        var result = await Kb.ListCategoriesAsync(Key, RequestAborted);
        if (result.IsSuccess)
        {
            Categories = result.Value;
            return;
        }

        Fail(result.Errors[0]);
    }

    private static IReadOnlyList<KbCrumb> Crumbs(ProductThemeViewModel theme) =>
        [new KbCrumb(theme.DisplayName, PortalRoutes.ProductHome(theme.Key)), new KbCrumb(KbCopy.HomeHeading)];
}
```

`src/TechStrap.Portal/Components/Pages/KbSearch.razor` (new)

```razor
@attribute [Route(PortalRoutes.KbSearchTemplate)]
@inherits ProductPageBase

@if (UnavailableMessage is not null)
{
    <PageTitle>@ShellCopy.UnavailableTitle</PageTitle>
    <ProductUnavailable Message="@UnavailableMessage" RetryHref="@RetryHref" />
}
else if (Theme is { } theme)
{
    <SeoHead Title="@KbCopy.SearchTitle(theme.DisplayName)" Description="@KbCopy.SearchDescription(theme.DisplayName)" RelativeUrl="@PortalRoutes.KbSearch(theme.Key)" ImageUrl="@KbSeo.Image(theme)" ImageAlt="@theme.DisplayName" NoIndex="@(Text.Length > 0)" />
    <KbBreadcrumbs Crumbs="@Crumbs(theme)" />
    <h1>@KbCopy.HomeHeading</h1>
    <KbSearchBox ProductKey="@theme.Key" Query="@Text" />
    @if (Text.Length == 0)
    {
        <StateMessage Heading="@KbCopy.SearchPromptHeading">
            <p>@KbCopy.SearchPromptText</p>
        </StateMessage>
    }
    else if (Results is { Items.Count: 0 })
    {
        <StateMessage Heading="@KbCopy.NoResultsHeading">
            <p>@KbCopy.NoResultsText <a href="@PortalRoutes.Contact(theme.Key)">@KbCopy.ContactUs</a></p>
        </StateMessage>
    }
    else if (Results is { } results)
    {
        <h2>@KbCopy.SearchHeading</h2>
        <p class="ts-kb-meta">@KbCopy.ResultCount(results.TotalCount)</p>
        <ul class="ts-kb-list">
            @foreach (var hit in results.Items)
            {
                <li><KbArticleCard Href="@PortalRoutes.KbArticle(theme.Key, hit.CategorySlug, hit.Slug)" Title="@hit.Title" Summary="@hit.Snippet" Meta="@hit.CategoryName" /></li>
            }
        </ul>
        <Pager Page="@results.Page" PageSize="@results.PageSize" TotalCount="@results.TotalCount" HrefFor="@(page => PortalRoutes.KbSearch(theme.Key, Text, page))" />
    }
}
```

`src/TechStrap.Portal/Components/Pages/KbSearch.razor.cs` (new)

```csharp
using Microsoft.AspNetCore.Components;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Components.Kb;
using TechStrap.Portal.Products;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Components.Pages;

/// <summary>
/// The help-center search (P09-T13): a plain GET form (<c>?q=&amp;page=</c>) that works without script. An empty text shows a prompt and makes no call; a text is cut at the API's limit (<see cref="KbSearchText"/>) and
/// searched through <see cref="IPublicKbClient"/>; no result shows a way to contact support. Review Focus 1 (XSS): the text, every title and every snippet are plain text shown by Razor, which encodes them; the snippet is
/// never markup (D-044). A page with a text is <c>noindex</c> (every text is a different page), the page is never kept by the cache (<c>PortalCachePaths</c>) or a browser (a header rule), and a paging link keeps the
/// text, escaped by <see cref="PortalRoutes.KbSearch(string, string, int)"/>. The page number is bound as text and parsed by <see cref="KbPaging"/>.
/// </summary>
public partial class KbSearch : ProductPageBase
{
    public const int PageSize = KbLimits.DefaultPublicSearchPageSize;

    [SupplyParameterFromQuery(Name = PortalRoutes.QueryParameter)]
    private string? Query { get; set; }

    [SupplyParameterFromQuery(Name = PortalRoutes.PageParameter)]
    private string? PageText { get; set; }

    [Inject]
    private IPublicKbClient Kb { get; set; } = default!;

    /// <summary>The cleaned search text: empty when there is none.</summary>
    private string Text { get; set; } = string.Empty;

    private PagedResponse<PublicKbSearchResultDto>? Results { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (Theme is null)
        {
            return;
        }

        Text = KbSearchText.Clean(Query);
        if (Text.Length == 0)
        {
            return;
        }

        var result = await Kb.SearchAsync(Key, Text, KbPaging.Parse(PageText), PageSize, RequestAborted);
        if (result.IsSuccess)
        {
            Results = result.Value;
            return;
        }

        Fail(result.Errors[0]);
    }

    private static IReadOnlyList<KbCrumb> Crumbs(ProductThemeViewModel theme) =>
        [new KbCrumb(theme.DisplayName, PortalRoutes.ProductHome(theme.Key)), new KbCrumb(KbCopy.HomeHeading, PortalRoutes.KbHome(theme.Key)), new KbCrumb(KbCopy.SearchHeading)];
}
```

`src/TechStrap.Portal/Components/Ui/Pager.razor` (new)

```razor
@if (Pages > 1)
{
    <nav class="ts-pager" aria-label="@KbCopy.PagerLabel">
        @if (Page > 1)
        {
            <a class="ts-pager-prev" rel="prev" href="@HrefFor(Page - 1)">@KbCopy.PreviousPage</a>
        }
        <span class="ts-pager-state" aria-current="page">@KbCopy.PageOf(Page, Pages)</span>
        @if (Page < Pages)
        {
            <a class="ts-pager-next" rel="next" href="@HrefFor(Page + 1)">@KbCopy.NextPage</a>
        }
    </nav>
}

@code {
    /// <summary>The page being shown, from one.</summary>
    [Parameter, EditorRequired]
    public int Page { get; set; } = 1;

    [Parameter, EditorRequired]
    public int PageSize { get; set; }

    [Parameter, EditorRequired]
    public int TotalCount { get; set; }

    /// <summary>The address of a page, built by <c>PortalRoutes</c> (it keeps a search text and escapes it). The links are plain, so paging works without script.</summary>
    [Parameter, EditorRequired]
    public Func<int, string> HrefFor { get; set; } = _ => string.Empty;

    private int Pages => KbPaging.TotalPages(TotalCount, PageSize);
}
```

`src/TechStrap.Portal/Components/Ui/StateMessage.razor` (new)

```razor
<section class="ts-state" role="status">
    <h2>@Heading</h2>
    @ChildContent
</section>

@code {
    /// <summary>The short heading of the state, plain text.</summary>
    [Parameter, EditorRequired]
    public string Heading { get; set; } = string.Empty;

    /// <summary>What to say and where to go next: the page's own markup, built from <c>KbCopy</c> and <c>PortalRoutes</c>.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}
```

`src/TechStrap.Portal/Components/_Imports.razor`

```diff
@@ -1,13 +1,16 @@
 @using Microsoft.AspNetCore.Components.Forms
 @using Microsoft.AspNetCore.Components.Routing
+@using SyntaxCircus.Blazor.Seo.Components
 @using Microsoft.AspNetCore.Components.Web
 @using TechStrap.Contracts.Kb
 @using TechStrap.Portal
 @using TechStrap.Portal.Components
+@using TechStrap.Portal.Components.Kb
 @using TechStrap.Portal.Components.Layout
 @using TechStrap.Portal.Components.Tickets
 @using TechStrap.Portal.Components.Ui
 @using TechStrap.Portal.Forms
 @using TechStrap.Portal.Products
 @using TechStrap.Portal.Routing
+@using TechStrap.Portal.Seo
 @using TechStrap.Portal.Tickets
```

`src/TechStrap.Portal/Products/ProductPageBase.cs`

```diff
@@ -40,6 +40,25 @@ public abstract class ProductPageBase : ComponentBase
     /// <summary>This page's own address, root-relative, for a "Try again" link (the document's <c>base</c> is <c>/</c>).</summary>
     protected string RetryHref => "/" + Navigation.ToBaseRelativePath(Navigation.Uri);
 
+    /// <summary>The token of the request this page is serving: an API call made for it stops when the visitor goes away.</summary>
+    protected CancellationToken RequestAborted => HttpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
+
+    /// <summary>
+    /// What a page does with a failed read of its own data once the product has loaded (the help-center pages): a not-found is the neutral 404 (the product is forgotten first, so the 404 is byte for byte the page an unknown route
+    /// gets), a rate limit is a 429 and anything else a 503, each with the fixed sentence of <see cref="ProblemCopy"/>; whatever the API said is never shown. The page keeps its theme and shows <see cref="UnavailableMessage"/>.
+    /// </summary>
+    protected void Fail(ResultError error)
+    {
+        ArgumentNullException.ThrowIfNull(error);
+        if (error.Kind == ResultErrorKind.NotFound)
+        {
+            NotFoundAfterTheming();
+            return;
+        }
+
+        Unavailable(error);
+    }
+
     /// <summary>Ends the request in the neutral 404 after the product was set (a post the API answers 404 to): the product is forgotten first, so the 404 carries none of its accent, header or footer.</summary>
     protected void NotFoundAfterTheming()
     {
@@ -56,7 +75,7 @@ public abstract class ProductPageBase : ComponentBase
             return;
         }
 
-        var result = await Products.GetAsync(Key, HttpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);
+        var result = await Products.GetAsync(Key, RequestAborted);
         if (result.IsSuccess)
         {
             Scope.Set(ProductThemeViewModel.From(result.Value, Environment.IsDevelopment()));
@@ -70,7 +89,12 @@ public abstract class ProductPageBase : ComponentBase
             return;
         }
 
-        // Fixed copy only: whatever the API said (a 400's own detail included) is never shown on a product page.
+        Unavailable(error);
+    }
+
+    // Fixed copy only: whatever the API said (a 400's own detail included) is never shown on a product page.
+    private void Unavailable(ResultError error)
+    {
         var rateLimited = error.Code == ApiErrorCodes.RateLimited;
         UnavailableMessage = rateLimited ? ProblemCopy.RateLimited : ProblemCopy.ApiUnavailable;
         SetStatus(rateLimited ? StatusCodes.Status429TooManyRequests : StatusCodes.Status503ServiceUnavailable);
```

`src/TechStrap.Portal/Routing/PortalRoutes.cs`

```diff
@@ -76,10 +76,19 @@ public static class PortalRoutes
 
     public static string KbCategory(string key, string category) => $"{KbHome(key)}/{Escape(category)}";
 
+    /// <summary>A page of a category: page one is the category's own address, so there is one address for it; a later page adds <c>?page=n</c>.</summary>
+    public static string KbCategory(string key, string category, int page) => page <= 1 ? KbCategory(key, category) : $"{KbCategory(key, category)}?{PageParameter}={page}";
+
     public static string KbArticle(string key, string category, string slug) => $"{KbCategory(key, category)}/{Escape(slug)}";
 
     public static string KbSearch(string key) => $"{KbHome(key)}/{KbSearchSegment}";
 
+    /// <summary>A page of search results: the text is escaped, so it can never add a parameter, and a paging link keeps it. Page one has no <c>page</c>; a blank text is the search page itself.</summary>
+    public static string KbSearch(string key, string text, int page) =>
+        string.IsNullOrWhiteSpace(text)
+            ? KbSearch(key)
+            : $"{KbSearch(key)}?{QueryParameter}={Escape(text)}{(page <= 1 ? string.Empty : $"&{PageParameter}={page}")}";
+
     public static string Suggest(string key) => $"{ProductHome(key)}/suggest";
 
     public static string Ticket(string token) => $"{TicketPrefix}/{Escape(token)}";
```

`src/TechStrap.Portal/Seo/KbSeo.cs` (new)

```csharp
using TechStrap.Portal.Products;

namespace TechStrap.Portal.Seo;

/// <summary>What the help-center pages tell <c>SeoHead</c> that is the same on every page.</summary>
public static class KbSeo
{
    /// <summary>A Portal asset: the Open Graph image of a product with no logo (the package would otherwise fall back to the bare site address, which is a page and not an image).</summary>
    public const string FallbackImage = "/icon-512.png";

    /// <summary>The product's logo when it has an acceptable one (https, already checked), else the Portal's own image.</summary>
    public static string Image(ProductThemeViewModel theme) => theme.LogoUrl ?? FallbackImage;
}
```

`src/TechStrap.Portal/Styles/_components.scss`

```diff
@@ -261,3 +261,63 @@
   margin-top: 24px;
   padding-left: 20px;
 }
+
+// The help center (PHASE-09c): structure only (lists, the breadcrumb trail, the pager); the visual polish pass is PHASE-09d.
+.ts-kb-list {
+  margin: 24px 0;
+  padding: 0;
+  list-style: none;
+
+  li + li {
+    margin-top: 20px;
+  }
+
+  h2 {
+    font-size: 1.125rem;
+    margin-bottom: 4px;
+  }
+}
+
+.ts-kb-summary {
+  margin: 0 0 4px;
+  overflow-wrap: anywhere;
+}
+
+.ts-kb-meta {
+  margin: 0;
+  font-size: .875rem;
+  color: var(--p-ink2);
+}
+
+.ts-breadcrumbs {
+  margin-bottom: 16px;
+  font-size: .875rem;
+
+  ol {
+    display: flex;
+    flex-wrap: wrap;
+    gap: 4px 8px;
+    margin: 0;
+    padding: 0;
+    list-style: none;
+  }
+
+  li + li::before {
+    content: "/";
+    margin-right: 8px;
+    color: var(--p-ink2);
+  }
+}
+
+.ts-pager {
+  display: flex;
+  flex-wrap: wrap;
+  gap: 8px 16px;
+  align-items: center;
+  justify-content: space-between;
+  margin: 24px 0;
+}
+
+.ts-state {
+  margin: 24px 0;
+}
```

`src/TechStrap.Portal/Suggestions/SuggestEndpoint.cs`

```diff
@@ -1,6 +1,7 @@
 using SyntaxCircus.Common;
 using TechStrap.Contracts.Kb;
 using TechStrap.Portal.Clients;
+using TechStrap.Portal.Components.Kb;
 using TechStrap.Portal.Routing;
 
 namespace TechStrap.Portal.Suggestions;
@@ -29,7 +30,7 @@ public static class SuggestEndpoint
     private static async Task<IResult> HandleAsync(string key, string? q, IPublicKbClient kb, HttpContext http, CancellationToken cancellationToken)
     {
         http.Response.Headers.CacheControl = "no-store";
-        var text = Clean(q);
+        var text = KbSearchText.Clean(q);
         if (text.Length == 0)
         {
             return Results.Json(Array.Empty<SuggestionDto>());
@@ -50,16 +51,4 @@ public static class SuggestEndpoint
             .ToList();
         return Results.Json(items);
     }
-
-    private static string Clean(string? text)
-    {
-        var trimmed = text?.Trim() ?? string.Empty;
-        if (trimmed.Length <= KbLimits.MaxSearchTextChars)
-        {
-            return trimmed;
-        }
-
-        var cut = trimmed[..KbLimits.MaxSearchTextChars];
-        return char.IsHighSurrogate(cut[^1]) ? cut[..^1] : cut;
-    }
 }
```


- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet build TechStrap.slnx -c Release
dotnet test --project tests/TechStrap.Portal.Tests -c Release --filter-query "/*/TechStrap.Portal.Tests.Kb/*/*"
dotnet test --project tests/TechStrap.Portal.Tests -c Release --filter-query "/*/TechStrap.Portal.Tests.Components/*/*"
dotnet test --project tests/TechStrap.Portal.Tests -c Release
dotnet test --project tests/TechStrap.Architecture.Tests -c Release
```

Expected: 0 warnings; the `Kb` namespace 68 tests, `Components` 77, all of the Portal tests `total: 1330 succeeded: 1330`, Architecture 294.

- [ ] **Step 5: Prove each pin with a recorded mutation**

Save `$T\specs\m3.py`:

```python
def PT(namespace):
    return ["dotnet", "test", "--project", "tests/TechStrap.Portal.Tests", "-c", "Release", "--filter-query", f"/*/TechStrap.Portal.Tests.{namespace}/*/*"]


KB, COMPONENTS, ROUTING = PT("Kb"), PT("Components"), PT("Routing")
P = "src/TechStrap.Portal/Components/Pages/"
C = "src/TechStrap.Portal/Components/"

MUTATIONS = [
    ("1 home: a category name is markup", P + "KbHome.razor", [("@category.Name</a></h2>", "@((MarkupString)category.Name)</a></h2>")], KB),
    ("2 card: the summary is markup", C + "Kb/KbArticleCard.razor", [('<p class="ts-kb-summary">@Summary</p>', '<p class="ts-kb-summary">@((MarkupString)Summary)</p>')], COMPONENTS),
    ("3 search: a query page is indexable", P + "KbSearch.razor", [('NoIndex="@(Text.Length > 0)"', 'NoIndex="false"')], KB),
    ("4 search: no result has no contact link", P + "KbSearch.razor", [('<p>@KbCopy.NoResultsText <a href="@PortalRoutes.Contact(theme.Key)">@KbCopy.ContactUs</a></p>', "<p>@KbCopy.NoResultsText</p>")], KB),
    ("5 search: an empty text calls the api", P + "KbSearch.razor.cs", [("        Text = KbSearchText.Clean(Query);\n        if (Text.Length == 0)\n        {\n            return;\n        }\n", "        Text = KbSearchText.Clean(Query);\n")], KB),
    ("6 search: the text is not cleaned", P + "KbSearch.razor.cs", [("Text = KbSearchText.Clean(Query);", "Text = Query ?? string.Empty;")], KB),
    ("7 search: the page is bound as a number", P + "KbSearch.razor.cs", [("KbPaging.Parse(PageText)", "int.Parse(PageText ?? \"1\")")], KB),
    ("8 search: a paging link forgets the text", P + "KbSearch.razor", [("PortalRoutes.KbSearch(theme.Key, Text, page)", "PortalRoutes.KbSearch(theme.Key)")], KB),
    ("9 client: a category slug is not checked", "src/TechStrap.Portal/Clients/PublicKbClient.cs", [("ProductKeyShape.IsWellFormed(productKey) && KbSlugShape.IsWellFormed(categorySlug)\n            ? api.GetAsync<PagedResponse<PublicKbArticleSummaryDto>>(", "ProductKeyShape.IsWellFormed(productKey)\n            ? api.GetAsync<PagedResponse<PublicKbArticleSummaryDto>>(")], KB),
    ("10 category: a page past the end is an empty page", P + "KbCategory.razor.cs", [("if (result.Value.Items.Count == 0)", "if (result.Value.Items.Count < 0)")], KB),
    ("11 base: a not-found keeps the product's theme", "src/TechStrap.Portal/Products/ProductPageBase.cs", [("            NotFoundAfterTheming();\n            return;\n        }\n\n        Unavailable(error);", "            Navigation.NotFound();\n            return;\n        }\n\n        Unavailable(error);")], KB),
    ("12 base: a failed read shows nothing", "src/TechStrap.Portal/Products/ProductPageBase.cs", [("        }\n\n        Unavailable(error);\n    }\n\n    /// <summary>Ends the request", "        }\n\n        _ = error;\n    }\n\n    /// <summary>Ends the request")], KB),
    ("13 pager: a next link on the last page", C + "Ui/Pager.razor", [("@if (Page < Pages)", "@if (Page <= Pages)")], COMPONENTS),
    ("14 pager: a previous link on the first page", C + "Ui/Pager.razor", [("@if (Page > 1)", "@if (Page >= 1)")], COMPONENTS),
    ("15 paging: the pages are rounded down", C + "Kb/KbPaging.cs", [("+ pageSize - 1)", "+ pageSize)")], COMPONENTS),
    ("16 paging: a signed page is accepted", C + "Kb/KbPaging.cs", [("NumberStyles.None", "NumberStyles.Integer")], COMPONENTS),
    ("17 routes: the search text is not escaped", "src/TechStrap.Portal/Routing/PortalRoutes.cs", [("?{QueryParameter}={Escape(text)}", "?{QueryParameter}={text}")], ROUTING),
    ("18 routes: page one has a query", "src/TechStrap.Portal/Routing/PortalRoutes.cs", [("page <= 1 ? KbCategory(key, category)", "page < 1 ? KbCategory(key, category)")], ROUTING),
    ("19 text: a surrogate pair is split", C + "Kb/KbSearchText.cs", [("char.IsHighSurrogate(cut[^1]) ? cut[..^1] : cut", "cut")], COMPONENTS),
    ("20 seo: no fallback image", "src/TechStrap.Portal/Seo/KbSeo.cs", [("theme.LogoUrl ?? FallbackImage", "theme.LogoUrl ?? string.Empty")], KB),
    ("21 home: no empty state", P + "KbHome.razor", [("@if (categories.Count == 0)", "@if (categories.Count < 0)")], KB),
    ("22 copy: one article is plural", C + "KbCopy.cs", [('count == 1 ? "1 article"', 'count == 2 ? "1 article"')], COMPONENTS),
    ("23 copy: the day is the local day", C + "KbCopy.cs", [("moment.UtcDateTime.ToString", "moment.DateTime.ToString")], COMPONENTS),
    ("24 search: the canonical address names the text", P + "KbSearch.razor", [('RelativeUrl="@PortalRoutes.KbSearch(theme.Key)"', 'RelativeUrl="@PortalRoutes.KbSearch(theme.Key, Text, 1)"')], KB),
    ("25 category: the canonical address forgets the page", P + "KbCategory.razor", [("PortalRoutes.KbCategory(theme.Key, Category, listing.Page)", "PortalRoutes.KbCategory(theme.Key, Category)")], KB),
    ("26 copy: page one is titled as a later page", C + "KbCopy.cs", [("page <= 1 ? $", "page < 1 ? $")], COMPONENTS),
    ("27 crumbs: the last step is not the current page", C + "Kb/KbBreadcrumbs.razor", [('<li aria-current="page">@crumb.Label</li>', "<li>@crumb.Label</li>")], COMPONENTS),
    ("28 state: an alert", C + "Ui/StateMessage.razor", [('role="status"', 'role="alert"')], COMPONENTS),
    ("29 box: the field is not called q", C + "Kb/KbSearchBox.razor", [('name="@PortalRoutes.QueryParameter"', 'name="query"')], KB),
    ("30 category: the product home link is lost from the trail", P + "KbCategory.razor.cs", [("[new KbCrumb(theme.DisplayName, PortalRoutes.ProductHome(theme.Key)), new KbCrumb(KbCopy.HomeHeading, PortalRoutes.KbHome(theme.Key)), new KbCrumb(categoryName)]", "[new KbCrumb(theme.DisplayName), new KbCrumb(KbCopy.HomeHeading, PortalRoutes.KbHome(theme.Key)), new KbCrumb(categoryName)]")], KB),
]
```

Run them in foreground batches and record the results:

| # | The mutation | File | Result |
| --- | --- | --- | --- |
| 1 | home: a category name is markup | `KbHome.razor` | KILLED (1 failing) |
| 2 | card: the summary is markup | `KbArticleCard.razor` | KILLED (1 failing) |
| 3 | search: a query page is indexable | `KbSearch.razor` | KILLED (2 failing) |
| 4 | search: no result has no contact link | `KbSearch.razor` | KILLED (1 failing) |
| 5 | search: an empty text calls the api | `KbSearch.razor.cs` | KILLED (5 failing) |
| 6 | search: the text is not cleaned | `KbSearch.razor.cs` | KILLED (2 failing) |
| 7 | search: the page is bound as a number | `KbSearch.razor.cs` | KILLED (3 failing) |
| 8 | search: a paging link forgets the text | `KbSearch.razor` | KILLED (2 failing) |
| 9 | client: a category slug is not checked | `PublicKbClient.cs` | KILLED (6 failing) |
| 10 | category: a page past the end is an empty page | `KbCategory.razor.cs` | KILLED (1 failing) |
| 11 | base: a not-found keeps the product's theme | `ProductPageBase.cs` | KILLED (7 failing) |
| 12 | base: a failed read shows nothing | `ProductPageBase.cs` | KILLED (4 failing) |
| 13 | pager: a next link on the last page | `Pager.razor` | KILLED (2 failing) |
| 14 | pager: a previous link on the first page | `Pager.razor` | KILLED (2 failing) |
| 15 | paging: the pages are rounded down | `KbPaging.cs` | KILLED (3 failing) |
| 16 | paging: a signed page is accepted | `KbPaging.cs` | KILLED (3 failing) |
| 17 | routes: the search text is not escaped | `PortalRoutes.cs` | KILLED (2 failing) |
| 18 | routes: page one has a query | `PortalRoutes.cs` | KILLED (1 failing) |
| 19 | text: a surrogate pair is split | `KbSearchText.cs` | KILLED (1 failing) |
| 20 | seo: no fallback image | `KbSeo.cs` | KILLED (1 failing) |
| 21 | home: no empty state | `KbHome.razor` | KILLED (1 failing) |
| 22 | copy: one article is plural | `KbCopy.cs` | KILLED (2 failing) |
| 23 | copy: the day is the local day | `KbCopy.cs` | KILLED (1 failing) |
| 24 | search: the canonical address names the text | `KbSearch.razor` | KILLED (1 failing) |
| 25 | category: the canonical address forgets the page | `KbCategory.razor` | KILLED (1 failing) |
| 26 | copy: page one is titled as a later page | `KbCopy.cs` | KILLED (1 failing) |
| 27 | crumbs: the last step is not the current page | `KbBreadcrumbs.razor` | KILLED (1 failing) |
| 28 | state: an alert | `StateMessage.razor` | KILLED (1 failing) |
| 29 | box: the field is not called q | `KbSearchBox.razor` | KILLED (4 failing) |
| 30 | category: the product home link is lost from the trail | `KbCategory.razor.cs` | KILLED (1 failing) |

Every mutation is killed. A first version of this task had a page-level `KbSlugShape` check on the category page; a mutation that removed it SURVIVED, because the client already refuses a malformed slug without a call (row 9 pins that), so the check was removed as dead code rather than pinned.

- [ ] **Step 6: Run the full suites and commit**

```bash
dotnet test --project tests/TechStrap.Portal.Tests -c Release
```

Expected: 1330 tests pass.

```bash
git add -A
git diff --cached --stat
git commit -m "feat: PHASE-09c help-center home, category and search pages with shared components (D-045)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

### Task 4: The article page: `KbArticleBody` (the second markup site), the SEO head and the escaped JSON-LD

**Review Focus pin:** 1 (XSS: `KbArticleBody` is the only new markup site and the body is the API's HTML byte for byte; a hostile title, category, summary or product name cannot leave the heading, the title, the meta tags, the breadcrumb or either JSON-LD block), 2 (an unpublished article, a wrong category, an unknown slug, a path under the search page and an unknown product are the one neutral 404) and 3 (an article is kept once, a 404 is not, no cookie).

**Files:**

- Create: `src/TechStrap.Portal/Components/Kb/KbArticleBody.razor`
- Create: `src/TechStrap.Portal/Components/Kb/KbPlainText.cs`
- Modify: `src/TechStrap.Portal/Components/KbCopy.cs`
- Create: `src/TechStrap.Portal/Components/Pages/KbArticle.razor`
- Create: `src/TechStrap.Portal/Components/Pages/KbArticle.razor.cs`
- Create: `src/TechStrap.Portal/Seo/JsonLdText.cs`
- Create: `src/TechStrap.Portal/Seo/KbStructuredData.cs`
- Modify: `src/TechStrap.Portal/Styles/_components.scss`
- Test (modify): `tests/TechStrap.Architecture.Tests/PortalRuleTests.cs`
- Test (modify): `tests/TechStrap.Architecture.Tests/PortalRules.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Components/KbPlainTextTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Kb/KbArticleHostTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Seo/JsonLdTextTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Seo/KbStructuredDataTests.cs`

**Interfaces:**
- Consumes: Task 3's `ProductPageBase.Fail`, `RequestAborted`, `KbBreadcrumbs`, `KbCrumb`, `KbSeo`, `KbCopy`, `KbTestKit`; Task 2's `IPublicKbClient.GetArticleAsync` and the cache; `PublishedKbArticleDto`; `ISeoUrlBuilder.AbsoluteUrl` (the package: a path gets the public address, an absolute address stays as it is); `SeoHead` and its `OgType` and `StructuredData` parameters; `PortalRules`, `ProjectGraph`.
- Produces:
  - `KbArticleBody` (`Html`: the API's sanitized HTML, rendered once; the second and last `MarkupString` site) and the page `KbArticle` (`/p/{key}/kb/{category}/{slug}`).
  - `KbPlainText.Describe(string? summary, string? html)`: the summary, else the first sentence of the body's first paragraph as plain text, at most `KbPlainText.MaxDescription` (160) characters cut at a word with an ellipsis, empty when there is no text.
  - `JsonLdText.Safe(string?)` (a value type; `Value`) and `JsonLdTextConverter`; the records `BreadcrumbItemLd`, `BreadcrumbListLd`, `WebPageLd`, `OrganizationLd` and `ArticleSchema`; `KbStructuredData.ForArticle(Func<string, string> absoluteUrl, ProductThemeViewModel theme, PublishedKbArticleDto article, string description, IReadOnlyList<KbCrumb> trail) : IReadOnlyList<object>` (the breadcrumb list, then the article).
  - `KbCopy.ArticleTitle(articleTitle, productName)`, `ArticleDescriptionFallback(articleTitle, productName)`, `StillNeedHelp`, `StillNeedHelpText`.
  - `PortalRules.MarkupStringSites` is exactly `["Components/Kb/KbArticleBody.razor", "Components/Tickets/CustomerMessageBody.razor"]`; `PortalRuleTests` pins it and counts that the word appears in two Portal files only.

- [ ] **Step 1: Write the failing tests**

The architecture pin changes first (the second site), then the unit tests of the escaping, the structured data and the description, then the host tests of the page. `KbArticleHostTests` is the heart: it compares the body with the API's string, parses the head, parses both JSON-LD blocks as JSON, throws hostile titles, categories, summaries and a hostile product name at every place they appear, and checks that a draft, a wrong category, an unknown slug and a path under `/kb/search/` are indistinguishable.

`tests/TechStrap.Architecture.Tests/PortalRuleTests.cs`

```diff
@@ -127,13 +127,16 @@ public sealed class PortalRuleTests
     // ---- MarkupString sites -----------------------------------------------------------------------------------------------------------
 
     [Fact]
-    public void In_09b_exactly_CustomerMessageBody_turns_text_into_markup()
+    public void In_09c_exactly_KbArticleBody_and_CustomerMessageBody_turn_text_into_markup()
     {
         var files = PortalRules.Sources(ProjectGraph.FindRepositoryRoot()).ToList();
 
         files.Count.ShouldBeGreaterThan(20, "the scan must see the Portal sources");
-        PortalRules.MarkupStringSites.ShouldBe(["Components/Tickets/CustomerMessageBody.razor"], "09b adds CustomerMessageBody (the API sanitizes the message body); 09c adds KbArticleBody in its own commit");
+        PortalRules.MarkupStringSites.ShouldBe(
+            ["Components/Kb/KbArticleBody.razor", "Components/Tickets/CustomerMessageBody.razor"],
+            "09b added CustomerMessageBody (the API sanitizes the message body) and 09c added KbArticleBody (the API sanitizes the article with the same rules, D-044); a third site is a design decision, argued in its own commit");
         PortalRules.MarkupStringViolations(files).ShouldBeEmpty();
+        files.Count(file => file.Text.Contains("MarkupString", StringComparison.Ordinal)).ShouldBe(2, "the word appears in those two files and nowhere else, comments included");
     }
 
     [Theory]
@@ -151,15 +154,29 @@ public sealed class PortalRuleTests
         violations[0].ShouldContain(path.Replace("src/TechStrap.Portal/", string.Empty, StringComparison.Ordinal));
     }
 
+    private const string ArticleBody = "src/TechStrap.Portal/Components/Kb/KbArticleBody.razor";
+    private const string MessageBody = "src/TechStrap.Portal/Components/Tickets/CustomerMessageBody.razor";
+    private const string Markup = "<div>@((MarkupString)Html)</div>";
+    private const string Encoded = "<div>@Html</div>";
+
     [Fact]
-    public void The_real_allow_list_passes_for_its_own_file_and_flags_the_same_text_anywhere_else()
+    public void The_real_allow_list_passes_for_its_two_files_and_flags_the_same_text_anywhere_else()
     {
-        const string Real = "src/TechStrap.Portal/Components/Tickets/CustomerMessageBody.razor";
-
-        PortalRules.MarkupStringViolations([(Real, "<div>@((MarkupString)Html)</div>")]).ShouldBeEmpty();
-        PortalRules.MarkupStringViolations([("src/TechStrap.Portal/Components/Tickets/MessageThread.razor", "<div>@((MarkupString)Html)</div>"), (Real, "<div>@((MarkupString)Html)</div>")])
+        PortalRules.MarkupStringViolations([(ArticleBody, Markup), (MessageBody, Markup)]).ShouldBeEmpty();
+        PortalRules.MarkupStringViolations([("src/TechStrap.Portal/Components/Tickets/MessageThread.razor", Markup), (ArticleBody, Markup), (MessageBody, Markup)])
             .ShouldHaveSingleItem().ShouldContain("MessageThread.razor");
-        PortalRules.MarkupStringViolations([(Real, "<div>@Html</div>")]).ShouldHaveSingleItem().ShouldContain("no longer");
+        PortalRules.MarkupStringViolations([("src/TechStrap.Portal/Components/Pages/KbArticle.razor", "<div>@((MarkupString)article.Html)</div>"), (ArticleBody, Markup), (MessageBody, Markup)])
+            .ShouldHaveSingleItem().ShouldContain("KbArticle.razor");
+        PortalRules.MarkupStringViolations([("src/TechStrap.Portal/Components/Kb/KbArticleCard.razor", "<p>@((MarkupString)Summary)</p>"), (ArticleBody, Markup), (MessageBody, Markup)])
+            .ShouldHaveSingleItem().ShouldContain("KbArticleCard.razor");
+    }
+
+    [Fact]
+    public void A_listed_file_that_no_longer_uses_it_and_a_missing_one_are_flagged()
+    {
+        PortalRules.MarkupStringViolations([(ArticleBody, Encoded), (MessageBody, Markup)]).ShouldHaveSingleItem().ShouldContain("KbArticleBody.razor");
+        PortalRules.MarkupStringViolations([(ArticleBody, Markup), (MessageBody, Encoded)]).ShouldHaveSingleItem().ShouldContain("CustomerMessageBody.razor");
+        PortalRules.MarkupStringViolations([(ArticleBody, Markup)]).ShouldHaveSingleItem().ShouldContain("no longer");
     }
 
     [Fact]
```

`tests/TechStrap.Architecture.Tests/PortalRules.cs`

```diff
@@ -29,10 +29,10 @@ public static partial class PortalRules
 
     /// <summary>
     /// The files (relative to src/TechStrap.Portal) that may turn API text into markup, which is where a stored-XSS bug would live. The API sanitizes the HTML before it sends it, and the Portal does not
-    /// sanitize again, so each site is argued for in the commit that adds it: 09b adds <c>CustomerMessageBody</c> (a ticket message body: the API's sanitized HTML, D-045 addendum) and 09c adds
-    /// <c>KbArticleBody</c> (a published article). Every other string the Portal shows is plain text, and Razor encodes it.
+    /// sanitize again, so each site is argued for in the commit that adds it: 09b added <c>CustomerMessageBody</c> (a ticket message body: the API's sanitized HTML, D-045 addendum) and 09c added
+    /// <c>KbArticleBody</c> (a published article: the same sanitizer, D-044, rendered once in this one component). There are exactly these two. Every other string the Portal shows is plain text, and Razor encodes it.
     /// </summary>
-    public static IReadOnlyList<string> MarkupStringSites { get; } = ["Components/Tickets/CustomerMessageBody.razor"];
+    public static IReadOnlyList<string> MarkupStringSites { get; } = ["Components/Kb/KbArticleBody.razor", "Components/Tickets/CustomerMessageBody.razor"];
 
     [GeneratedRegex(@"\b(?:I|Add)?HttpClient(?:Factory)?\b", RegexOptions.CultureInvariant)]
     private static partial Regex HttpClientUse();
```

`tests/TechStrap.Portal.Tests/Components/KbPlainTextTests.cs` (new)

```csharp
using TechStrap.Portal.Components.Kb;

namespace TechStrap.Portal.Tests.Components;

/// <summary>PHASE-09c T14, ruling 5: the description of an article is its summary, else the first sentence of its body as plain text. The result is text that a meta tag encodes, never markup.</summary>
public sealed class KbPlainTextTests
{
    [Theory]
    [InlineData("How to reset it", "<p>Ignored.</p>", "How to reset it")]
    [InlineData("  How   to\nreset  it  ", null, "How to reset it")]
    [InlineData("Summary with <b>tags</b> stays as written", "<p>x</p>", "Summary with <b>tags</b> stays as written")]
    public void A_summary_with_words_is_the_description(string summary, string? html, string expected) => KbPlainText.Describe(summary, html).ShouldBe(expected);

    [Theory]
    [InlineData(null, "<p>Open the app. Then tap settings.</p>", "Open the app.")]
    [InlineData("", "<p>Open the app. Then tap settings.</p>", "Open the app.")]
    [InlineData("   ", "<h1>Title</h1>\n<p>Open the app! Then go.</p>", "Open the app!")]
    [InlineData(null, "<h1>Reset</h1>\n<p>Is it <strong>broken</strong>? Try this.</p>", "Is it broken?")]
    [InlineData(null, "<p>No full stop here</p>", "No full stop here")]
    [InlineData(null, "<p>Fish &amp; chips &lt;3. Next.</p>", "Fish & chips <3.")]
    [InlineData(null, "<p class=\"lead\" id=\"x\">Attributes are dropped. More.</p>", "Attributes are dropped.")]
    [InlineData(null, "<P>Upper case tags. More.</P>", "Upper case tags.")]
    [InlineData(null, "<p>Version 1.2 is out. Update.</p>", "Version 1.2 is out.")]
    [InlineData(null, "<p>Line one\nline two. Three.</p>", "Line one line two.")]
    [InlineData(null, "<h2>Only a heading</h2>", "Only a heading")]
    [InlineData(null, "<ul><li>First item. Second.</li></ul>", "First item.")]
    [InlineData(null, "<p><script>alert(1)</script>Safe text. More.</p>", "Safe text.")]
    [InlineData(null, "<p><style>p{}</style>Styled. More.</p>", "Styled.")]
    [InlineData(null, "<p><img src=\"x.png\" alt=\"pic\"> After the image. More.</p>", "After the image.")]
    public void Without_a_summary_it_is_the_first_sentence_of_the_first_paragraph_as_plain_text(string? summary, string html, string expected) => KbPlainText.Describe(summary, html).ShouldBe(expected);

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "<p></p>")]
    [InlineData(null, "<img src=\"x.png\">")]
    [InlineData(null, "<table><tr><td> </td></tr></table>")]
    public void No_words_at_all_is_an_empty_description(string? summary, string? html) => KbPlainText.Describe(summary, html).ShouldBeEmpty();

    [Fact]
    public void A_long_sentence_is_cut_at_a_word_with_an_ellipsis_within_the_limit()
    {
        var sentence = string.Join(' ', Enumerable.Repeat("wordy", 60)) + ".";

        var description = KbPlainText.Describe(null, "<p>" + sentence + "</p>");

        description.Length.ShouldBeLessThanOrEqualTo(KbPlainText.MaxDescription + 3);
        description.ShouldEndWith("wordy...");
        KbPlainText.Describe(new string('a', 400), null).ShouldBe(new string('a', KbPlainText.MaxDescription) + "...", "no space to cut at: cut at the limit");
        KbPlainText.MaxDescription.ShouldBe(160);
    }

    [Fact]
    public void A_cut_never_splits_a_surrogate_pair()
    {
        var text = new string('a', KbPlainText.MaxDescription - 1) + char.ConvertFromUtf32(0x1F600) + "tail";

        var description = KbPlainText.Describe(text, null);

        description.ShouldBe(new string('a', KbPlainText.MaxDescription - 1) + "...");
    }

    [Fact]
    public void Markup_in_the_body_never_survives_into_the_description()
    {
        var description = KbPlainText.Describe(null, "<p>&lt;script&gt;alert(1)&lt;/script&gt; and <a href=\"javascript:alert(1)\">link</a>. Next.</p>");

        description.ShouldBe("<script>alert(1)</script> and link.");
    }
}
```

`tests/TechStrap.Portal.Tests/Kb/KbArticleHostTests.cs` (new)

```csharp
using System.Net;
using System.Text.Json;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Forms;
using TechStrap.Portal.Tests.Tickets;

namespace TechStrap.Portal.Tests.Kb;

/// <summary>
/// P09-T14 (the article page) at the host: the body is the API's HTML byte for byte, the head (title, description, canonical, Open Graph) and the two JSON-LD blocks parse, and Review Focus 1 and 2: a hostile title,
/// category or summary cannot break out of the page or of the JSON-LD, the body is the only markup, and an unpublished article, a wrong category, an unknown slug and an unknown product are the one neutral 404.
/// </summary>
public sealed class KbArticleHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private const string ArticlePath = "/api/public/kb/paperplane/articles/accounts/reset-password";

    // What the API's sanitizer produces: headings, a paragraph with a link, a list, a table, code and an image.
    private const string Body =
        "<h2>Steps</h2>\n<p>Open <a href=\"https://app.example.com/settings\" rel=\"nofollow\">settings</a> &amp; choose <em>Reset</em>. Then wait.</p>\n<ul>\n<li>One</li>\n<li>Two &lt;3</li>\n</ul>\n"
        + "<table>\n<thead><tr><th>A</th><th>B</th></tr></thead>\n<tbody><tr><td>1</td><td>2</td></tr></tbody>\n</table>\n<pre><code class=\"language-bash\">echo &quot;hi&quot;\n</code></pre>\n"
        + "<p><img src=\"https://api.example.com/kb-images/a.png\" alt=\"The settings screen\"></p>\n";

    private static PublishedKbArticleDto Published(string title = "Reset your password", string? summary = "How to reset it", string html = Body, string? product = "paperplane", string category = "accounts", string categoryName = "Accounts", string slug = "reset-password") =>
        new(product, category, categoryName, slug, title, summary, html, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero), KbTestKit.Updated);

    private static PortalFactory With(PublishedKbArticleDto article)
    {
        var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, $"/api/public/kb/paperplane/articles/{article.CategorySlug}/{article.Slug}", article);
        return factory;
    }

    private static JsonDocument[] JsonLd(AngleSharp.Html.Dom.IHtmlDocument dom) =>
        [.. dom.QuerySelectorAll("script[type='application/ld+json']").Select(script => JsonDocument.Parse(script.TextContent))];

    [Fact]
    public async Task The_body_is_the_apis_html_byte_for_byte_inside_one_container()
    {
        await using var factory = With(Published());
        using var client = FormTestKit.Client(factory);

        var (response, html, _) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts/reset-password", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain($"<div class=\"ts-kb-article-body\">{Body}</div>", Case.Sensitive);
        factory.Api.Requests.Select(request => request.Path).ShouldBe([KbTestKit.ProductPath, ArticlePath]);
        factory.Api.Requests.ShouldAllBe(request => request.Client == ApiClientNames.Read);
    }

    [Fact]
    public async Task The_page_has_the_title_the_updated_day_the_trail_and_a_way_to_contact_support()
    {
        await using var factory = With(Published());
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts/reset-password", Ct);

        KbTestKit.Texts(dom, "h1").ShouldBe(["Reset your password"]);
        KbTestKit.Texts(dom, "article.ts-kb-article .ts-kb-meta").ShouldBe(["Updated 5 Oct 2026"]);
        KbTestKit.Texts(dom, "nav.ts-breadcrumbs li").ShouldBe(["Paperplane", "Help center", "Accounts", "Reset your password"]);
        KbTestKit.Links(dom, "nav.ts-breadcrumbs a").ShouldBe(["/p/paperplane", "/p/paperplane/kb", "/p/paperplane/kb/accounts"]);
        KbTestKit.Texts(dom, "aside.ts-kb-help h2").ShouldBe(["Still need help?"]);
        KbTestKit.Links(dom, "aside.ts-kb-help a").ShouldBe(["/p/paperplane/contact"]);
        dom.QuerySelectorAll(".ts-kb-article-body table, .ts-kb-article-body pre, .ts-kb-article-body img").Length.ShouldBe(3);
    }

    [Fact]
    public async Task The_head_has_a_unique_title_the_summary_as_description_the_canonical_address_and_open_graph_for_an_article()
    {
        await using var factory = With(Published());
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts/reset-password?utm_source=mail", Ct);

        dom.Title.ShouldBe("Reset your password - Paperplane Help Center");
        KbTestKit.Meta(dom, "meta[name=description]").ShouldBe("How to reset it");
        dom.QuerySelector("link[rel=canonical]")!.GetAttribute("href").ShouldBe(PortalFactory.PublicUrl + "/p/paperplane/kb/accounts/reset-password");
        KbTestKit.Meta(dom, "meta[name=robots]").ShouldStartWith("index, follow");
        KbTestKit.Meta(dom, "meta[property='og:type']").ShouldBe("article");
        KbTestKit.Meta(dom, "meta[property='og:title']").ShouldBe("Reset your password - Paperplane Help Center");
        KbTestKit.Meta(dom, "meta[property='og:description']").ShouldBe("How to reset it");
        KbTestKit.Meta(dom, "meta[property='og:url']").ShouldBe(PortalFactory.PublicUrl + "/p/paperplane/kb/accounts/reset-password");
        KbTestKit.Meta(dom, "meta[property='og:image']").ShouldBe(PortalFactory.PublicUrl + "/icon-512.png");
        KbTestKit.Meta(dom, "meta[name='twitter:title']").ShouldBe("Reset your password - Paperplane Help Center");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Without_a_summary_the_description_is_the_first_sentence_of_the_body_as_plain_text(string? summary)
    {
        await using var factory = With(Published(summary: summary));
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts/reset-password", Ct);

        KbTestKit.Meta(dom, "meta[name=description]").ShouldBe("Open settings & choose Reset.");
        using var page = JsonLd(dom)[1];
        page.RootElement.GetProperty("description").GetString().ShouldBe("Open settings & choose Reset.");
    }

    [Fact]
    public async Task With_neither_a_summary_nor_any_text_the_description_names_the_article_and_the_product()
    {
        await using var factory = With(Published(summary: null, html: "<p><img src=\"https://api.example.com/kb-images/a.png\" alt=\"\"></p>"));
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts/reset-password", Ct);

        KbTestKit.Meta(dom, "meta[name=description]").ShouldBe("Reset your password. Help article for Paperplane.");
    }

    [Fact]
    public async Task The_page_carries_a_breadcrumb_list_and_an_article_as_json_that_parses()
    {
        await using var factory = With(Published());
        using var client = FormTestKit.Client(factory);

        var (_, html, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts/reset-password", Ct);

        html.ShouldContain("type=\"application/ld&#x2B;json\"", Case.Sensitive);
        var blocks = JsonLd(dom);
        blocks.Length.ShouldBe(2);
        blocks[0].RootElement.GetProperty("@type").GetString().ShouldBe("BreadcrumbList");
        blocks[0].RootElement.GetProperty("itemListElement").EnumerateArray().Select(item => item.GetProperty("item").GetString()).ShouldBe(
        [
            PortalFactory.PublicUrl + "/p/paperplane",
            PortalFactory.PublicUrl + "/p/paperplane/kb",
            PortalFactory.PublicUrl + "/p/paperplane/kb/accounts",
            PortalFactory.PublicUrl + "/p/paperplane/kb/accounts/reset-password",
        ]);
        blocks[1].RootElement.GetProperty("@type").GetString().ShouldBe("Article");
        blocks[1].RootElement.GetProperty("headline").GetString().ShouldBe("Reset your password");
        blocks[1].RootElement.GetProperty("datePublished").GetDateTimeOffset().ShouldBe(new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        blocks[1].RootElement.GetProperty("mainEntityOfPage").GetProperty("@id").GetString().ShouldBe(PortalFactory.PublicUrl + "/p/paperplane/kb/accounts/reset-password");
        foreach (var block in blocks)
        {
            block.Dispose();
        }
    }

    [Fact]
    public async Task The_json_ld_is_data_not_script_and_the_security_policy_is_unchanged()
    {
        await using var factory = With(Published());
        using var client = FormTestKit.Client(factory);

        var (response, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts/reset-password", Ct);

        var policy = KbTestKit.Header(response, "Content-Security-Policy").Single().Split(';', StringSplitOptions.TrimEntries);
        policy.ShouldContain("script-src 'self'");
        policy.ShouldNotContain(directive => directive.Contains("unsafe-inline", StringComparison.Ordinal) && directive.StartsWith("script-src", StringComparison.Ordinal));
        dom.QuerySelectorAll("script[type='application/ld+json']").ShouldAllBe(script => script.GetAttribute("src") == null);
        dom.QuerySelectorAll("script:not([type='application/ld+json'])").ShouldAllBe(script => script.GetAttribute("src") != null, "every executable script is an external file");
    }

    [Theory]
    [InlineData("</script><img src=x onerror=alert(1)>")]
    [InlineData("<!-- x --> <script>alert(1)</script>")]
    [InlineData("\"><svg onload=alert(1)>")]
    public async Task A_hostile_title_category_and_summary_cannot_break_out_of_the_page_the_head_or_the_json_ld(string hostile)
    {
        await using var factory = With(Published(hostile, hostile, categoryName: hostile));
        using var client = FormTestKit.Client(factory);

        var (_, html, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts/reset-password", Ct);

        // Not one element or event handler of the hostile text exists anywhere in the document.
        dom.QuerySelectorAll("img[src=x], svg, [onerror], [onload]").Length.ShouldBe(0);
        dom.QuerySelectorAll("script:not([src]):not([type='application/ld+json'])").Length.ShouldBe(0);
        html.ShouldNotContain("<img src=x");
        html.ShouldNotContain("<svg onload");
        html.ShouldNotContain("<script>alert(1)");

        // The text is still all there, as text: in the heading, the title, the meta tags and the trail.
        KbTestKit.Texts(dom, "h1").ShouldBe([hostile]);
        dom.Title.ShouldBe(hostile + " - Paperplane Help Center");
        KbTestKit.Meta(dom, "meta[name=description]").ShouldBe(hostile);
        KbTestKit.Meta(dom, "meta[property='og:title']").ShouldBe(hostile + " - Paperplane Help Center");
        KbTestKit.Texts(dom, "nav.ts-breadcrumbs li").ShouldBe(["Paperplane", "Help center", hostile, hostile]);

        // The JSON-LD blocks hold no angle bracket, are valid JSON, and read back as the original text.
        var scripts = dom.QuerySelectorAll("script[type='application/ld+json']").Select(script => script.TextContent).ToList();
        scripts.Count.ShouldBe(2);
        scripts.ShouldAllBe(script => !script.Contains('<') && !script.Contains('>') && !script.Contains('&'));
        using var breadcrumbs = JsonDocument.Parse(scripts[0]);
        breadcrumbs.RootElement.GetProperty("itemListElement")[3].GetProperty("name").GetString().ShouldBe(hostile);
        using var article = JsonDocument.Parse(scripts[1]);
        article.RootElement.GetProperty("headline").GetString().ShouldBe(hostile);
        article.RootElement.GetProperty("description").GetString().ShouldBe(hostile);
    }

    [Fact]
    public async Task A_hostile_product_name_cannot_break_out_of_the_json_ld_either()
    {
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.ProductPath, FormTestKit.Product("</script><img src=x onerror=alert(1)>"));
        factory.Api.OnJson(HttpMethod.Get, ArticlePath, Published());
        using var client = FormTestKit.Client(factory);

        var (_, html, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts/reset-password", Ct);

        html.ShouldNotContain("<img src=x");
        dom.QuerySelectorAll("img[src=x]").Length.ShouldBe(0);
        dom.QuerySelectorAll("script[type='application/ld+json']").Select(script => script.TextContent).ShouldAllBe(script => !script.Contains('<'));
    }

    [Fact]
    public async Task The_summary_is_never_markup_and_the_body_is_the_only_place_that_is()
    {
        await using var factory = With(Published(summary: "<b>bold</b> <img src=x onerror=alert(1)>"));
        using var client = FormTestKit.Client(factory);

        var (_, html, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts/reset-password", Ct);

        html.ShouldNotContain("<img src=x");
        KbTestKit.Meta(dom, "meta[name=description]").ShouldBe("<b>bold</b> <img src=x onerror=alert(1)>");
        dom.QuerySelectorAll("img[src=x]").Length.ShouldBe(0);
        dom.QuerySelectorAll(".ts-kb-article-body em").Length.ShouldBe(1, "the body's own markup is intact");
    }

    [Fact]
    public async Task A_shared_article_is_shown_and_canonical_under_the_product_the_visitor_is_on()
    {
        await using var factory = With(Published(product: null, category: "general", categoryName: "General", slug: "shared-tips", title: "Shared tips"));
        using var client = FormTestKit.Client(factory);

        var (response, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/general/shared-tips", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        dom.QuerySelector("link[rel=canonical]")!.GetAttribute("href").ShouldBe(PortalFactory.PublicUrl + "/p/paperplane/kb/general/shared-tips");
        KbTestKit.Links(dom, "nav.ts-breadcrumbs a").ShouldBe(["/p/paperplane", "/p/paperplane/kb", "/p/paperplane/kb/general"]);
    }

    [Fact]
    public async Task An_unpublished_article_a_wrong_category_and_an_unknown_slug_are_the_neutral_404_byte_for_byte_with_no_theme()
    {
        var neutral = await Seen.NeutralNotFoundAsync(Ct, "/p/nope");
        await using var factory = KbTestKit.Factory();
        foreach (var path in new[] { "/api/public/kb/paperplane/articles/accounts/draft", "/api/public/kb/paperplane/articles/billing/reset-password", "/api/public/kb/paperplane/articles/accounts/no-such" })
        {
            KbTestKit.Problem(factory, path, HttpStatusCode.NotFound, "kb-article-not-found");
        }

        using var client = FormTestKit.Client(factory);
        var answers = new List<Seen>();
        foreach (var path in new[] { "/p/paperplane/kb/accounts/draft", "/p/paperplane/kb/billing/reset-password", "/p/paperplane/kb/accounts/no-such" })
        {
            using var response = await client.GetAsync(path, Ct);
            answers.Add(await Seen.OfAsync(response, factory.Api.Requests.Count, Ct));
        }

        foreach (var seen in answers)
        {
            seen.ShouldBeTheNeutralNotFound(neutral);
            seen.Body.ShouldNotContain("Paperplane");
            seen.Body.ShouldBe(answers[0].Body, "an unpublished article, a wrong category and an unknown slug are indistinguishable");
            seen.Headers.ShouldBe(answers[0].Headers);
        }
    }

    [Theory]
    [InlineData("/p/paperplane/kb/Bad_Cat/reset-password")]
    [InlineData("/p/paperplane/kb/accounts/Bad_Slug")]
    [InlineData("/p/paperplane/kb/accounts/-x")]
    [InlineData("/p/paperplane/kb/accounts/a%2Fb")]
    [InlineData("/p/paperplane/kb/accounts/x--y")]
    public async Task A_category_or_slug_that_is_not_a_slug_is_the_neutral_404_and_the_article_is_never_asked_for(string path)
    {
        var neutral = await Seen.NeutralNotFoundAsync(Ct, "/p/nope");
        await using var factory = KbTestKit.Factory();
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync(path, Ct);
        var seen = await Seen.OfAsync(response, factory.Api.Requests.Count, Ct);

        seen.ShouldBeTheNeutralNotFound(neutral);
        factory.Api.Requests.Select(request => request.Path).ShouldBe([KbTestKit.ProductPath], path);
    }

    [Fact]
    public async Task A_path_under_the_search_page_is_an_article_route_with_the_reserved_category_and_the_api_refuses_it()
    {
        var neutral = await Seen.NeutralNotFoundAsync(Ct, "/p/nope");
        await using var factory = KbTestKit.Factory();
        KbTestKit.Problem(factory, "/api/public/kb/paperplane/articles/search/anything", HttpStatusCode.NotFound, "kb-article-not-found");
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync("/p/paperplane/kb/search/anything", Ct);
        var seen = await Seen.OfAsync(response, factory.Api.Requests.Count, Ct);

        seen.ShouldBeTheNeutralNotFound(neutral);
        factory.Api.Requests.Select(request => request.Path).ShouldBe([KbTestKit.ProductPath, "/api/public/kb/paperplane/articles/search/anything"]);
        factory.Api.Requests.ShouldAllBe(request => !request.Path.EndsWith("/search", StringComparison.Ordinal), "the search endpoint is never called for it");
    }

    [Fact]
    public async Task An_unknown_product_is_the_neutral_404_and_the_article_is_never_asked_for()
    {
        var neutral = await Seen.NeutralNotFoundAsync(Ct, "/p/nope");
        await using var factory = FormTestKit.Factory(product: false);
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/gone", HttpStatusCode.NotFound, "product-not-found", "No such product.");
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync("/p/gone/kb/accounts/reset-password", Ct);
        var seen = await Seen.OfAsync(response, factory.Api.Requests.Count, Ct);

        seen.ShouldBeTheNeutralNotFound(neutral);
        factory.Api.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/public/products/gone");
    }

    [Fact]
    public async Task A_failing_article_call_is_a_calm_503_and_a_429_is_a_429()
    {
        await using var down = KbTestKit.Factory();
        down.Api.OnStatus(HttpMethod.Get, ArticlePath, HttpStatusCode.ServiceUnavailable);
        using var downClient = FormTestKit.Client(down);
        await using var busy = KbTestKit.Factory();
        busy.Api.OnStatus(HttpMethod.Get, ArticlePath, HttpStatusCode.TooManyRequests);
        using var busyClient = FormTestKit.Client(busy);

        var (downResponse, downHtml, _) = await KbTestKit.GetAsync(downClient, "/p/paperplane/kb/accounts/reset-password", Ct);
        var (busyResponse, busyHtml, _) = await KbTestKit.GetAsync(busyClient, "/p/paperplane/kb/accounts/reset-password", Ct);

        downResponse.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        downHtml.ShouldContain("We could not reach our support system.");
        busyResponse.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        busyHtml.ShouldContain("You have sent a lot in a short time.");
    }

    [Fact]
    public async Task An_article_is_kept_for_a_minute_a_404_is_not_and_no_page_sets_a_cookie()
    {
        await using var factory = With(Published());
        KbTestKit.Problem(factory, "/api/public/kb/paperplane/articles/accounts/gone", HttpStatusCode.NotFound, "kb-article-not-found");
        using var client = FormTestKit.Client(factory);

        using var first = await client.GetAsync("/p/paperplane/kb/accounts/reset-password", Ct);
        using var second = await client.GetAsync("/p/paperplane/kb/accounts/reset-password", Ct);
        using var goneFirst = await client.GetAsync("/p/paperplane/kb/accounts/gone", Ct);
        using var goneSecond = await client.GetAsync("/p/paperplane/kb/accounts/gone", Ct);

        factory.Api.Count(HttpMethod.Get, ArticlePath).ShouldBe(1);
        factory.Api.Count(HttpMethod.Get, "/api/public/kb/paperplane/articles/accounts/gone").ShouldBe(2);
        first.Headers.CacheControl!.ToString().ShouldBe("public, max-age=60");
        KbTestKit.Header(second, "Age").ShouldNotBeEmpty();
        goneFirst.Headers.CacheControl?.Public.ShouldNotBe(true);
        foreach (var response in new[] { first, second, goneFirst, goneSecond })
        {
            KbTestKit.Header(response, "Set-Cookie").ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task The_body_html_reaches_the_page_even_when_it_looks_odd_so_the_portal_never_rewrites_what_the_api_decided()
    {
        const string Odd = "<p>&nbsp;&copy; 2026 &#x2B; &amp;amp; tail</p>\n<blockquote>\n<p>Quote</p>\n</blockquote>\n";
        await using var factory = With(Published(html: Odd));
        using var client = FormTestKit.Client(factory);

        var (_, html, _) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts/reset-password", Ct);

        html.ShouldContain($"<div class=\"ts-kb-article-body\">{Odd}</div>", Case.Sensitive);
    }

    [Fact]
    public async Task The_product_logo_is_the_open_graph_image_when_it_has_an_acceptable_one()
    {
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.ProductPath, FormTestKit.Product() with { LogoPath = "https://cdn.example.com/paperplane.png" });
        factory.Api.OnJson(HttpMethod.Get, ArticlePath, Published());
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts/reset-password", Ct);

        KbTestKit.Meta(dom, "meta[property='og:image']").ShouldBe("https://cdn.example.com/paperplane.png");
        using var article = JsonLd(dom)[1];
        article.RootElement.GetProperty("image").GetString().ShouldBe("https://cdn.example.com/paperplane.png");
    }
}
```

`tests/TechStrap.Portal.Tests/Seo/JsonLdTextTests.cs` (new)

```csharp
using System.Text.Encodings.Web;
using System.Text.Json;
using TechStrap.Portal.Seo;

namespace TechStrap.Portal.Tests.Seo;

/// <summary>
/// PHASE-09c Review Focus 1 (XSS in SEO): the text of a structured-data value cannot end its script block, and it reads back as exactly what was written. The serializer options below are the ones
/// <c>SyntaxCircus.Blazor.Seo</c> 0.1.4's <c>JsonLd</c> component uses (camel case, nulls left out, the encoder that leaves <c>&lt;</c>, <c>&gt;</c> and <c>&amp;</c> alone), so the test sees what the page would write.
/// </summary>
public sealed class JsonLdTextTests
{
    private sealed record Holder(JsonLdText Name);

    private static readonly JsonSerializerOptions PackageOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private static string Json(string? text) => JsonSerializer.Serialize(new Holder(JsonLdText.Safe(text)), PackageOptions);

    public static TheoryData<string> Hostile() => new()
    {
        "</script><img src=x onerror=alert(1)>",
        "</SCRIPT >",
        "<!-- comment -->",
        "<script>alert(1)</script>",
        "a & b &amp; c",
        "she said \"hi\" and \\ left",
        "line one\nline two\ttabbed\r\n",
        "it's 1+1",
        "caf" + char.ConvertFromUtf32(0xE9),
        "emoji " + char.ConvertFromUtf32(0x1F600) + " end",
        "line" + char.ConvertFromUtf32(0x2028) + "separator" + char.ConvertFromUtf32(0x2029) + "end",
        "nul" + char.ConvertFromUtf32(0) + "char",
        "a lone surrogate " + (char)0xD800 + " here",
        "",
        "plain text",
    };

    [Theory]
    [MemberData(nameof(Hostile))]
    public void The_json_has_no_character_that_could_end_a_script_block_or_start_markup(string text)
    {
        var json = Json(text);

        json.ShouldNotContain("<");
        json.ShouldNotContain(">");
        json.ShouldNotContain("&");
        json.ShouldNotContain("'");
        json.ShouldNotContain("+");
        json.ShouldAllBe(character => character >= 0x20 && character < 0x7F, "every character is printable ASCII: the rest are \\u escapes");
    }

    [Theory]
    [MemberData(nameof(Hostile))]
    public void The_json_reads_back_as_exactly_the_text_that_was_written(string text)
    {
        using var document = JsonDocument.Parse(Json(text));

        // A lone surrogate is not valid text; the strict encoder writes the replacement character for it, which is what a reader gets back.
        var expected = text.Contains((char)0xD800) ? text.Replace((char)0xD800, (char)0xFFFD) : text;
        document.RootElement.GetProperty("name").GetString().ShouldBe(expected);
    }

    [Fact]
    public void A_plain_string_in_the_same_serialiser_is_not_escaped_which_is_why_the_wrapper_exists()
    {
        var plain = JsonSerializer.Serialize(new { name = "</script><b>" }, PackageOptions);

        plain.ShouldContain("</script><b>", Case.Sensitive);
        Json("</script><b>").ShouldNotContain("</script>");
        Json("</script><b>").ShouldContain("\\u003C/script\\u003E\\u003Cb\\u003E");
    }

    [Fact]
    public void A_null_is_the_empty_string_and_the_value_is_kept_as_given()
    {
        JsonLdText.Safe(null).Value.ShouldBe(string.Empty);
        JsonLdText.Safe("a < b").Value.ShouldBe("a < b");
        JsonLdText.Safe("a < b").ToString().ShouldBe("a < b");
        Json(null).ShouldBe("{\"name\":\"\"}");
    }

    [Fact]
    public void The_converter_also_reads_a_string_back()
    {
        var holder = JsonSerializer.Deserialize<Holder>("{\"name\":\"a \\u003C b\"}", PackageOptions);

        holder!.Name.Value.ShouldBe("a < b");
    }
}
```

`tests/TechStrap.Portal.Tests/Seo/KbStructuredDataTests.cs` (new)

```csharp
using System.Text.Encodings.Web;
using System.Text.Json;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Components.Kb;
using TechStrap.Portal.Products;
using TechStrap.Portal.Seo;

namespace TechStrap.Portal.Tests.Seo;

/// <summary>PHASE-09c T14: the shape of the article's structured data, and Review Focus 1: every string of it, whatever an author wrote, stays inside its string.</summary>
public sealed class KbStructuredDataTests
{
    private static readonly JsonSerializerOptions PackageOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private static readonly ProductThemeViewModel Theme = new("paperplane", "Paperplane", "#F59E0B", null);
    private static readonly DateTimeOffset Published = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Updated = new(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);

    // What ISeoUrlBuilder.AbsoluteUrl does: a path gets the public address in front, an absolute address stays as it is.
    private static string Absolute(string path) => path.StartsWith("https://", StringComparison.Ordinal) ? path : "https://portal.test" + path;

    private static PublishedKbArticleDto Article(string title = "Reset your password", string category = "Accounts") =>
        new("paperplane", "accounts", category, "reset-password", title, "How to reset it", "<p>Open settings.</p>", Published, Updated);

    private static KbCrumb[] Trail(PublishedKbArticleDto article) =>
    [
        new(Theme.DisplayName, "/p/paperplane"),
        new("Help center", "/p/paperplane/kb"),
        new(article.CategoryName, "/p/paperplane/kb/accounts"),
        new(article.Title),
    ];

    private static string[] Scripts(PublishedKbArticleDto article, string description = "How to reset it") =>
        [.. KbStructuredData.ForArticle(Absolute, Theme, article, description, Trail(article)).Select(data => JsonSerializer.Serialize(data, data.GetType(), PackageOptions))];

    [Fact]
    public void An_article_has_a_breadcrumb_list_and_an_article_both_schema_org()
    {
        var article = Article();

        var scripts = Scripts(article);

        scripts.Length.ShouldBe(2);
        using var breadcrumbs = JsonDocument.Parse(scripts[0]);
        var list = breadcrumbs.RootElement;
        list.GetProperty("@context").GetString().ShouldBe("https://schema.org");
        list.GetProperty("@type").GetString().ShouldBe("BreadcrumbList");
        var items = list.GetProperty("itemListElement").EnumerateArray().ToList();
        items.Select(item => item.GetProperty("position").GetInt32()).ShouldBe([1, 2, 3, 4]);
        items.Select(item => item.GetProperty("@type").GetString()).ShouldAllBe(type => type == "ListItem");
        items.Select(item => item.GetProperty("name").GetString()).ShouldBe(["Paperplane", "Help center", "Accounts", "Reset your password"]);
        items.Select(item => item.GetProperty("item").GetString()).ShouldBe(
        [
            "https://portal.test/p/paperplane",
            "https://portal.test/p/paperplane/kb",
            "https://portal.test/p/paperplane/kb/accounts",
            "https://portal.test/p/paperplane/kb/accounts/reset-password",
        ]);

        using var page = JsonDocument.Parse(scripts[1]);
        var root = page.RootElement;
        root.GetProperty("@context").GetString().ShouldBe("https://schema.org");
        root.GetProperty("@type").GetString().ShouldBe("Article");
        root.GetProperty("headline").GetString().ShouldBe("Reset your password");
        root.GetProperty("description").GetString().ShouldBe("How to reset it");
        root.GetProperty("datePublished").GetDateTimeOffset().ShouldBe(Published);
        root.GetProperty("dateModified").GetDateTimeOffset().ShouldBe(Updated);
        root.GetProperty("mainEntityOfPage").GetProperty("@type").GetString().ShouldBe("WebPage");
        root.GetProperty("mainEntityOfPage").GetProperty("@id").GetString().ShouldBe("https://portal.test/p/paperplane/kb/accounts/reset-password");
        root.GetProperty("image").GetString().ShouldBe("https://portal.test/icon-512.png");
        root.GetProperty("author").GetProperty("@type").GetString().ShouldBe("Organization");
        root.GetProperty("author").GetProperty("name").GetString().ShouldBe("Paperplane");
        root.GetProperty("publisher").GetProperty("name").GetString().ShouldBe("Paperplane");
    }

    [Fact]
    public void The_image_is_the_products_logo_when_it_has_one()
    {
        var theme = Theme with { LogoUrl = "https://cdn.example.com/paperplane.png" };
        var article = Article();

        var data = KbStructuredData.ForArticle(Absolute, theme, article, "d", Trail(article));

        using var page = JsonDocument.Parse(JsonSerializer.Serialize(data[1], data[1].GetType(), PackageOptions));
        page.RootElement.GetProperty("image").GetString().ShouldBe("https://cdn.example.com/paperplane.png");
    }

    [Theory]
    [InlineData("</script><img src=x onerror=alert(1)>")]
    [InlineData("<!-- x --> & <b>y</b> '+'")]
    [InlineData("</SCRIPT >")]
    public void A_hostile_title_category_or_description_cannot_leave_its_string_and_reads_back_unchanged(string hostile)
    {
        var article = Article(hostile, hostile);

        var scripts = Scripts(article, hostile);

        foreach (var script in scripts)
        {
            script.ShouldNotContain("<");
            script.ShouldNotContain(">");
            script.ShouldNotContain("&");
        }

        using var breadcrumbs = JsonDocument.Parse(scripts[0]);
        var names = breadcrumbs.RootElement.GetProperty("itemListElement").EnumerateArray().Select(item => item.GetProperty("name").GetString()).ToList();
        names[2].ShouldBe(hostile);
        names[3].ShouldBe(hostile);
        using var page = JsonDocument.Parse(scripts[1]);
        page.RootElement.GetProperty("headline").GetString().ShouldBe(hostile);
        page.RootElement.GetProperty("description").GetString().ShouldBe(hostile);
    }

    [Fact]
    public void A_hostile_product_name_cannot_leave_its_string_either()
    {
        var theme = Theme with { DisplayName = "</script><i>Acme</i>" };
        var article = Article();

        var data = KbStructuredData.ForArticle(Absolute, theme, article, "d", Trail(article));
        var scripts = data.Select(item => JsonSerializer.Serialize(item, item.GetType(), PackageOptions)).ToList();

        scripts.ShouldAllBe(script => !script.Contains('<') && !script.Contains('>'));
        using var page = JsonDocument.Parse(scripts[1]);
        page.RootElement.GetProperty("publisher").GetProperty("name").GetString().ShouldBe("</script><i>Acme</i>");
    }

    [Fact]
    public void The_last_step_of_the_trail_is_the_page_itself_and_a_trail_without_links_still_has_addresses()
    {
        var article = Article();

        var data = KbStructuredData.ForArticle(Absolute, Theme, article, "d", [new KbCrumb("Only")]);

        using var breadcrumbs = JsonDocument.Parse(JsonSerializer.Serialize(data[0], data[0].GetType(), PackageOptions));
        breadcrumbs.RootElement.GetProperty("itemListElement")[0].GetProperty("item").GetString().ShouldBe("https://portal.test/p/paperplane/kb/accounts/reset-password");
    }

    [Fact]
    public void A_missing_argument_is_refused()
    {
        var article = Article();

        Should.Throw<ArgumentNullException>(() => KbStructuredData.ForArticle(null!, Theme, article, "d", []));
        Should.Throw<ArgumentNullException>(() => KbStructuredData.ForArticle(Absolute, null!, article, "d", []));
        Should.Throw<ArgumentNullException>(() => KbStructuredData.ForArticle(Absolute, Theme, null!, "d", []));
        Should.Throw<ArgumentNullException>(() => KbStructuredData.ForArticle(Absolute, Theme, article, "d", null!));
    }
}
```


- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test --project tests/TechStrap.Architecture.Tests -c Release
dotnet build tests/TechStrap.Portal.Tests -c Release
```

Expected (recorded with the source files set aside; the first one with only the architecture test changed):

```text
failed TechStrap.Architecture.Tests.PortalRuleTests.In_09c_exactly_KbArticleBody_and_CustomerMessageBody_turn_text_into_markup
failed TechStrap.Architecture.Tests.PortalRuleTests.The_real_allow_list_passes_for_its_two_files_and_flags_the_same_text_anywhere_else
failed TechStrap.Architecture.Tests.PortalRuleTests.A_listed_file_that_no_longer_uses_it_and_a_missing_one_are_flagged
  total: 295
  failed: 3
error CS0246: The type or namespace name 'JsonLdText' could not be found
```

- [ ] **Step 3: Write the implementation**

`PortalRules.MarkupStringSites` gains the second site, in this commit, with its reason in the comment. `JsonLdText` and the structured-data records, `KbPlainText`, the body component, the page and its copy. A Razor file cannot hold the text `</script`, so nothing here does; the component's comment names `PortalRules.MarkupStringSites` (it may: it is the site).

`src/TechStrap.Portal/Components/Kb/KbArticleBody.razor` (new)

```razor
<div class="ts-kb-article-body">@((MarkupString)Html)</div>

@code {
    /// <summary>
    /// The article body exactly as the API sent it. The API renders the agent's Markdown and sanitizes the HTML (D-044); the Portal does not sanitize again and does not change a byte, so what a visitor reads is what the API
    /// decided is safe. This and <c>CustomerMessageBody</c> are the only two places the Portal turns text into elements (PortalRules.MarkupStringSites); every other string it shows is encoded.
    /// </summary>
    [Parameter, EditorRequired]
    public string Html { get; set; } = string.Empty;
}
```

`src/TechStrap.Portal/Components/Kb/KbPlainText.cs` (new)

```csharp
using System.Net;
using System.Text.RegularExpressions;

namespace TechStrap.Portal.Components.Kb;

/// <summary>
/// The description of an article for a search engine (PHASE-09c): the author's summary, else the first sentence of the body as plain text. The body is the API's sanitized HTML, read here only to take the words out of it:
/// the result is plain text that Razor encodes when it is written into a meta tag, and it is never put back into markup, so an imperfect tag strip cannot become an injection.
/// </summary>
public static partial class KbPlainText
{
    /// <summary>The longest description: a search engine shows about 160 characters.</summary>
    public const int MaxDescription = 160;

    [GeneratedRegex(@"<p(?:\s[^>]*)?>(.*?)</p\s*>", RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex FirstParagraph();

    [GeneratedRegex(@"<(?:script|style)\b.*?</(?:script|style)\s*>", RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptAndStyle();

    // A tag that ends a line of text (a paragraph, a heading, a list item, a row, a break) is a space, so two blocks do not run together; any other tag (emphasis, a link, code) is nothing, so a word is not split.
    [GeneratedRegex(@"</?(?:p|div|h[1-6]|li|ul|ol|tr|td|th|br|hr|pre|table|thead|tbody|blockquote)[^>]*>", RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex BlockTag();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^.*?[.!?](?=\s|$)", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex FirstSentence();

    /// <summary>The summary when it has words, else the first sentence of the body's first paragraph (or of its text when it has none), cut at <see cref="MaxDescription"/> characters at a word; empty when there is no text at all.</summary>
    public static string Describe(string? summary, string? html)
    {
        var fromSummary = Collapse(summary);
        if (fromSummary.Length > 0)
        {
            return Cut(fromSummary);
        }

        var body = html ?? string.Empty;
        var paragraph = FirstParagraph().Match(body);
        var text = Collapse(Strip(paragraph.Success ? paragraph.Groups[1].Value : body));
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var sentence = FirstSentence().Match(text);
        return Cut(sentence.Success ? sentence.Value : text);
    }

    private static string Strip(string html) => WebUtility.HtmlDecode(AnyTag().Replace(BlockTag().Replace(ScriptAndStyle().Replace(html, " "), " "), string.Empty));

    private static string Collapse(string? text) => Whitespace().Replace(text ?? string.Empty, " ").Trim();

    private static string Cut(string text)
    {
        if (text.Length <= MaxDescription)
        {
            return text;
        }

        var cut = text[..MaxDescription];
        if (char.IsHighSurrogate(cut[^1]))
        {
            cut = cut[..^1];
        }

        var space = cut.LastIndexOf(' ');
        return (space > MaxDescription / 2 ? cut[..space] : cut).TrimEnd() + "...";
    }
}
```

`src/TechStrap.Portal/Components/KbCopy.cs`

```diff
@@ -19,6 +19,10 @@ public static class KbCopy
     public const string PreviousPage = "Previous page";
     public const string NextPage = "Next page";
 
+    // The end of an article.
+    public const string StillNeedHelp = "Still need help?";
+    public const string StillNeedHelpText = "If this did not answer your question, contact support and we will help you.";
+
     // Search.
     public const string SearchHeading = "Search results";
     public const string SearchPromptHeading = "What are you looking for?";
@@ -36,6 +40,10 @@ public static class KbCopy
 
     public static string CategoryDescription(string categoryName, string productName) => $"Help articles about {categoryName} for {productName}.";
 
+    public static string ArticleTitle(string articleTitle, string productName) => $"{articleTitle} - {productName} Help Center";
+
+    public static string ArticleDescriptionFallback(string articleTitle, string productName) => $"{articleTitle}. Help article for {productName}.";
+
     public static string SearchTitle(string productName) => $"Search - {productName} Help Center";
 
     public static string SearchDescription(string productName) => $"Search the help articles for {productName}.";
```

`src/TechStrap.Portal/Components/Pages/KbArticle.razor` (new)

```razor
@attribute [Route(PortalRoutes.KbArticleTemplate)]
@inherits ProductPageBase

@if (UnavailableMessage is not null)
{
    <PageTitle>@ShellCopy.UnavailableTitle</PageTitle>
    <ProductUnavailable Message="@UnavailableMessage" RetryHref="@RetryHref" />
}
else if (Theme is { } theme && Article is { } article)
{
    <SeoHead Title="@KbCopy.ArticleTitle(article.Title, theme.DisplayName)" Description="@Description" RelativeUrl="@PortalRoutes.KbArticle(theme.Key, article.CategorySlug, article.Slug)" ImageUrl="@KbSeo.Image(theme)" ImageAlt="@theme.DisplayName" OgType="article" StructuredData="@StructuredData" />
    <KbBreadcrumbs Crumbs="@Trail" />
    <article class="ts-kb-article">
        <h1>@article.Title</h1>
        <p class="ts-kb-meta">@KbCopy.UpdatedOn(article.UpdatedAt)</p>
        <KbArticleBody Html="@article.Html" />
    </article>
    <aside class="ts-kb-help">
        <h2>@KbCopy.StillNeedHelp</h2>
        <p>@KbCopy.StillNeedHelpText</p>
        <p><a class="btn btn-primary" href="@PortalRoutes.Contact(theme.Key)">@KbCopy.ContactUs</a></p>
    </aside>
}
```

`src/TechStrap.Portal/Components/Pages/KbArticle.razor.cs` (new)

```csharp
using Microsoft.AspNetCore.Components;
using SyntaxCircus.Blazor.Seo;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Components.Kb;
using TechStrap.Portal.Products;
using TechStrap.Portal.Routing;
using TechStrap.Portal.Seo;

namespace TechStrap.Portal.Components.Pages;

/// <summary>
/// One published article (P09-T14). Review Focus 2: an unpublished article, another product's, a wrong category, an unknown slug and an unknown product are the one neutral 404, byte for byte (the product is forgotten first),
/// and a slug that is not a slug is answered without a call (the client refuses it). Review Focus 1: the body is the API's sanitized HTML shown by <see cref="KbArticleBody"/>, the one place that renders it; the title, the
/// category, the summary and every meta value are plain text and encoded; the structured data is written through <see cref="JsonLdText"/>. The canonical address is the product path the visitor is on (a shared article is
/// canonical under each product, D-045). <c>/p/{key}/kb/search/{slug}</c> matches this route with the category <c>search</c>, which the API refuses (it is reserved), so it is a 404 here too.
/// </summary>
public partial class KbArticle : ProductPageBase
{
    [Parameter]
    public string Category { get; set; } = string.Empty;

    [Parameter]
    public string Slug { get; set; } = string.Empty;

    [Inject]
    private IPublicKbClient Kb { get; set; } = default!;

    [Inject]
    private ISeoUrlBuilder Urls { get; set; } = default!;

    private PublishedKbArticleDto? Article { get; set; }

    private string Description { get; set; } = string.Empty;

    private IReadOnlyList<KbCrumb> Trail { get; set; } = [];

    private IReadOnlyList<object> StructuredData { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (Theme is not { } theme)
        {
            return;
        }

        var result = await Kb.GetArticleAsync(Key, Category, Slug, RequestAborted);
        if (result.IsFailure)
        {
            Fail(result.Errors[0]);
            return;
        }

        var article = result.Value;
        Description = KbPlainText.Describe(article.Summary, article.Html);
        if (Description.Length == 0)
        {
            Description = KbCopy.ArticleDescriptionFallback(article.Title, theme.DisplayName);
        }

        Trail =
        [
            new KbCrumb(theme.DisplayName, PortalRoutes.ProductHome(theme.Key)),
            new KbCrumb(KbCopy.HomeHeading, PortalRoutes.KbHome(theme.Key)),
            new KbCrumb(article.CategoryName, PortalRoutes.KbCategory(theme.Key, article.CategorySlug)),
            new KbCrumb(article.Title),
        ];
        StructuredData = KbStructuredData.ForArticle(Urls.AbsoluteUrl, theme, article, Description, Trail);
        Article = article;
    }
}
```

`src/TechStrap.Portal/Seo/JsonLdText.cs` (new)

```csharp
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TechStrap.Portal.Seo;

/// <summary>
/// A string of structured data that cannot end the script block it is written into. <c>SyntaxCircus.Blazor.Seo</c> 0.1.4 writes its JSON-LD through a markup string with an encoder that leaves <c>&lt;</c>, <c>&gt;</c> and
/// <c>&amp;</c> alone, so an article title that contains <c>&lt;/script&gt;</c> ends the block and injects markup into the head (the PHASE-09c spike reproduced it). Escaping the text before it reaches that serializer would
/// be escaped a second time (the JSON would read back as backslash text), so a value is wrapped in this type, whose converter writes the string itself with the strict encoder: <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, the
/// apostrophe, <c>+</c> and every non-ASCII character become <c>\uXXXX</c> escapes, which a JSON reader turns back into the original text. <see cref="Safe"/> is the only way to make one; every string of every
/// structured-data record the Portal writes is one.
/// </summary>
[JsonConverter(typeof(JsonLdTextConverter))]
public readonly record struct JsonLdText
{
    private JsonLdText(string value) => Value = value;

    /// <summary>The text as it was given (never escaped: the escaping happens when it is written).</summary>
    public string Value { get; }

    /// <summary>Wraps a string (a null is the empty string).</summary>
    public static JsonLdText Safe(string? text) => new(text ?? string.Empty);

    public override string ToString() => Value;
}

/// <summary>Writes a <see cref="JsonLdText"/> as a JSON string literal with the strict (HTML-safe, ASCII-only) encoder.</summary>
public sealed class JsonLdTextConverter : JsonConverter<JsonLdText>
{
    private static readonly JsonSerializerOptions Strict = new() { Encoder = JavaScriptEncoder.Default };

    public override JsonLdText Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => JsonLdText.Safe(reader.GetString());

    public override void Write(Utf8JsonWriter writer, JsonLdText value, JsonSerializerOptions options) => writer.WriteRawValue(JsonSerializer.Serialize(value.Value, Strict));
}
```

`src/TechStrap.Portal/Seo/KbStructuredData.cs` (new)

```csharp
using System.Text.Json.Serialization;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Components;
using TechStrap.Portal.Components.Kb;
using TechStrap.Portal.Products;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Seo;

/// <summary>One step of a <see cref="BreadcrumbListLd"/>: its place in the trail, its name and the absolute address it stands for.</summary>
public sealed record BreadcrumbItemLd(
    int Position,
    JsonLdText Name,
    JsonLdText Item,
    [property: JsonPropertyName("@type")] string Type = "ListItem");

/// <summary>
/// schema.org <c>BreadcrumbList</c>. The package has a record of the same shape, but its strings are plain strings that its serializer writes without escaping <c>&lt;</c>; this one carries <see cref="JsonLdText"/>.
/// </summary>
public sealed record BreadcrumbListLd(
    IReadOnlyList<BreadcrumbItemLd> ItemListElement,
    [property: JsonPropertyName("@context")] string Context = "https://schema.org",
    [property: JsonPropertyName("@type")] string Type = "BreadcrumbList");

/// <summary>A schema.org <c>WebPage</c> reference, for <c>mainEntityOfPage</c>.</summary>
public sealed record WebPageLd(
    [property: JsonPropertyName("@id")] JsonLdText Id,
    [property: JsonPropertyName("@type")] string Type = "WebPage");

/// <summary>A schema.org <c>Organization</c> reference (the product), for <c>author</c> and <c>publisher</c>.</summary>
public sealed record OrganizationLd(JsonLdText Name, [property: JsonPropertyName("@type")] string Type = "Organization");

/// <summary>
/// schema.org <c>Article</c> for a help article (the package has none). Every string is a <see cref="JsonLdText"/>. The address of the page is the canonical one, which for a shared article is the product path the visitor is on.
/// </summary>
public sealed record ArticleSchema(
    JsonLdText Headline,
    JsonLdText Description,
    DateTimeOffset DatePublished,
    DateTimeOffset DateModified,
    WebPageLd MainEntityOfPage,
    JsonLdText Image,
    OrganizationLd Author,
    OrganizationLd Publisher,
    [property: JsonPropertyName("@context")] string Context = "https://schema.org",
    [property: JsonPropertyName("@type")] string Type = "Article");

/// <summary>Builds the structured data of an article page: the breadcrumb trail and the article, each safe to write into a script block.</summary>
public static class KbStructuredData
{
    /// <param name="absoluteUrl">Turns a root-relative path into the absolute address (<c>ISeoUrlBuilder.AbsoluteUrl</c>).</param>
    public static IReadOnlyList<object> ForArticle(
        Func<string, string> absoluteUrl, ProductThemeViewModel theme, PublishedKbArticleDto article, string description, IReadOnlyList<KbCrumb> trail)
    {
        ArgumentNullException.ThrowIfNull(absoluteUrl);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(article);
        ArgumentNullException.ThrowIfNull(trail);
        var pageUrl = absoluteUrl(PortalRoutes.KbArticle(theme.Key, article.CategorySlug, article.Slug));
        var product = new OrganizationLd(JsonLdText.Safe(theme.DisplayName));
        var items = trail
            .Select((crumb, index) => new BreadcrumbItemLd(index + 1, JsonLdText.Safe(crumb.Label), JsonLdText.Safe(crumb.Href is { } href ? absoluteUrl(href) : pageUrl)))
            .ToList();
        return
        [
            new BreadcrumbListLd(items),
            new ArticleSchema(
                JsonLdText.Safe(article.Title),
                JsonLdText.Safe(description),
                article.PublishedAt,
                article.UpdatedAt,
                new WebPageLd(JsonLdText.Safe(pageUrl)),
                JsonLdText.Safe(absoluteUrl(KbSeo.Image(theme))),
                product,
                product),
        ];
    }
}
```

`src/TechStrap.Portal/Styles/_components.scss`

```diff
@@ -321,3 +321,26 @@
 .ts-state {
   margin: 24px 0;
 }
+
+// An article's body is the API's sanitized HTML: a wide table, a code block or an image scrolls or shrinks inside its own box instead of widening the page.
+.ts-kb-article-body {
+  overflow-wrap: anywhere;
+
+  img {
+    max-width: 100%;
+    height: auto;
+  }
+
+  pre,
+  table {
+    display: block;
+    max-width: 100%;
+    overflow-x: auto;
+  }
+}
+
+.ts-kb-help {
+  margin-top: 32px;
+  padding-top: 8px;
+  border-top: 1px solid var(--p-line);
+}
```


- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet build TechStrap.slnx -c Release
dotnet test --project tests/TechStrap.Architecture.Tests -c Release
dotnet test --project tests/TechStrap.Portal.Tests -c Release
```

Expected: 0 warnings; Architecture `total: 295 succeeded: 295`; Portal `total: 1424 succeeded: 1424`.

- [ ] **Step 5: Prove each pin with a recorded mutation**

Save `$T\specs\m4.py`:

```python
def PT(namespace):
    return ["dotnet", "test", "--project", "tests/TechStrap.Portal.Tests", "-c", "Release", "--filter-query", f"/*/TechStrap.Portal.Tests.{namespace}/*/*"]


KB, SEO, COMPONENTS = PT("Kb"), PT("Seo"), PT("Components")
ARCH = ["dotnet", "test", "--project", "tests/TechStrap.Architecture.Tests", "-c", "Release"]
P = "src/TechStrap.Portal/Components/Pages/"
C = "src/TechStrap.Portal/Components/"
S = "src/TechStrap.Portal/Seo/"

MUTATIONS = [
    ("1 body: the html is encoded", C + "Kb/KbArticleBody.razor", [("@((MarkupString)Html)", "@Html")], KB),
    ("2 page: the html is trimmed", P + "KbArticle.razor", [('Html="@article.Html"', 'Html="@article.Html.Trim()"')], KB),
    ("3 jsonld: the relaxed encoder", S + "JsonLdText.cs", [("Encoder = JavaScriptEncoder.Default", "Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping")], SEO),
    ("4 structured: positions start at zero", S + "KbStructuredData.cs", [("new BreadcrumbItemLd(index + 1,", "new BreadcrumbItemLd(index,")], SEO),
    ("5 structured: the last step has no address", S + "KbStructuredData.cs", [("crumb.Href is { } href ? absoluteUrl(href) : pageUrl", "crumb.Href is { } href ? absoluteUrl(href) : string.Empty")], SEO),
    ("6 structured: the dates are swapped", S + "KbStructuredData.cs", [("                article.PublishedAt,\n                article.UpdatedAt,", "                article.UpdatedAt,\n                article.PublishedAt,")], SEO),
    ("7 structured: the logo is ignored", S + "KbStructuredData.cs", [("JsonLdText.Safe(absoluteUrl(KbSeo.Image(theme)))", "JsonLdText.Safe(absoluteUrl(KbSeo.FallbackImage))")], SEO),
    ("8 page: the open graph type is website", P + "KbArticle.razor", [(' OgType="article"', "")], KB),
    ("9 page: no first-sentence fallback", P + "KbArticle.razor.cs", [("KbPlainText.Describe(article.Summary, article.Html)", "article.Summary ?? string.Empty")], KB),
    ("10 page: no title fallback", P + "KbArticle.razor.cs", [("        if (Description.Length == 0)\n        {\n            Description = KbCopy.ArticleDescriptionFallback(article.Title, theme.DisplayName);\n        }\n", "")], KB),
    ("11 text: the limit is 1600", C + "Kb/KbPlainText.cs", [("MaxDescription = 160;", "MaxDescription = 1600;")], COMPONENTS),
    ("12 text: a script inside a paragraph is kept", C + "Kb/KbPlainText.cs", [("BlockTag().Replace(ScriptAndStyle().Replace(html, \" \"), \" \")", "BlockTag().Replace(html, \" \")")], COMPONENTS),
    ("13 text: the first paragraph is not preferred", C + "Kb/KbPlainText.cs", [("var paragraph = FirstParagraph().Match(body);", "var paragraph = Match.Empty;")], COMPONENTS),
    ("14 text: the summary keeps its whitespace", C + "Kb/KbPlainText.cs", [("Whitespace().Replace(text ?? string.Empty, \" \").Trim()", "(text ?? string.Empty).Trim()")], COMPONENTS),
    ("15 text: a surrogate pair is split by the cut", C + "Kb/KbPlainText.cs", [("        if (char.IsHighSurrogate(cut[^1]))\n        {\n            cut = cut[..^1];\n        }\n\n", "")], COMPONENTS),
    ("16 text: an inline tag splits a word", C + "Kb/KbPlainText.cs", [("AnyTag().Replace(BlockTag().Replace(ScriptAndStyle().Replace(html, \" \"), \" \"), string.Empty)", "AnyTag().Replace(BlockTag().Replace(ScriptAndStyle().Replace(html, \" \"), \" \"), \" \")")], COMPONENTS),
    ("17 copy: the article title is product first", C + "KbCopy.cs", [("$\"{articleTitle} - {productName} Help Centre\"", "$\"{productName} - {articleTitle} Help Centre\"")], KB),
    ("18 page: no contact link", P + "KbArticle.razor", [('        <p><a class="btn btn-primary" href="@PortalRoutes.Contact(theme.Key)">@KbCopy.ContactUs</a></p>\n', "")], KB),
    ("19 page: the category crumb links the help center home", P + "KbArticle.razor.cs", [("new KbCrumb(article.CategoryName, PortalRoutes.KbCategory(theme.Key, article.CategorySlug)),", "new KbCrumb(article.CategoryName, PortalRoutes.KbHome(theme.Key)),")], KB),
    ("20 rules: the article body is not a listed site", "tests/TechStrap.Architecture.Tests/PortalRules.cs", [('["Components/Kb/KbArticleBody.razor", "Components/Tickets/CustomerMessageBody.razor"]', '["Components/Tickets/CustomerMessageBody.razor"]')], ARCH),
    ("21 page: an article is noindex", P + "KbArticle.razor", [(' OgType="article"', ' OgType="article" NoIndex="true"')], KB),
    ("22 page: the canonical is the route's own category", P + "KbArticle.razor", [("PortalRoutes.KbArticle(theme.Key, article.CategorySlug, article.Slug)\" ImageUrl", "PortalRoutes.KbHome(theme.Key)\" ImageUrl")], KB),
]
```

Run them in foreground batches and record the results:

| # | The mutation | File | Result |
| --- | --- | --- | --- |
| 1 | body: the html is encoded | `KbArticleBody.razor` | KILLED (4 failing) |
| 2 | page: the html is trimmed | `KbArticle.razor` | KILLED (2 failing) |
| 3 | jsonld: the relaxed encoder | `JsonLdText.cs` | KILLED (12 failing) |
| 4 | structured: positions start at zero | `KbStructuredData.cs` | KILLED (1 failing) |
| 5 | structured: the last step has no address | `KbStructuredData.cs` | KILLED (2 failing) |
| 6 | structured: the dates are swapped | `KbStructuredData.cs` | KILLED (1 failing) |
| 7 | structured: the logo is ignored | `KbStructuredData.cs` | KILLED (1 failing) |
| 8 | page: the open graph type is website | `KbArticle.razor` | KILLED (1 failing) |
| 9 | page: no first-sentence fallback | `KbArticle.razor.cs` | KILLED (3 failing) |
| 10 | page: no title fallback | `KbArticle.razor.cs` | KILLED (1 failing) |
| 11 | text: the limit is 1600 | `KbPlainText.cs` | KILLED (1 failing) |
| 12 | text: a script inside a paragraph is kept | `KbPlainText.cs` | KILLED (2 failing) |
| 13 | text: the first paragraph is not preferred | `KbPlainText.cs` | KILLED (2 failing) |
| 14 | text: the summary keeps its whitespace | `KbPlainText.cs` | KILLED (2 failing) |
| 15 | text: a surrogate pair is split by the cut | `KbPlainText.cs` | KILLED (1 failing) |
| 16 | text: an inline tag splits a word | `KbPlainText.cs` | KILLED (2 failing) |
| 17 | copy: the article title is product first | `KbCopy.cs` | KILLED (4 failing) |
| 18 | page: no contact link | `KbArticle.razor` | KILLED (1 failing) |
| 19 | page: the category crumb links the help center home | `KbArticle.razor.cs` | KILLED (3 failing) |
| 20 | rules: the article body is not a listed site | `PortalRules.cs` | KILLED (3 failing) |
| 21 | page: an article is noindex | `KbArticle.razor` | KILLED (1 failing) |
| 22 | page: the canonical is the route's own category | `KbArticle.razor` | KILLED (2 failing) |

Every mutation is killed; row 20 is the architecture rule and runs the Architecture tests, the others run the Portal tests of one namespace.

- [ ] **Step 6: Run the full suites and commit**

```bash
dotnet test --project tests/TechStrap.Portal.Tests -c Release
dotnet test --project tests/TechStrap.Architecture.Tests -c Release
```

Expected: 1424 and 295 tests pass.

```bash
git add -A
git diff --cached --stat
git commit -m "feat: PHASE-09c article page, SEO head, safe JSON-LD and the second markup site (D-045)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

### Task 5: Close-out: the sitemap host tests, the developer guide, the PHASE-09 ticks and the D-045 as-built notes

**Review Focus pin:** 4 (the sitemap as a visitor and a crawler meet it: absolute addresses, exactly what the API returned, a shared article under each product, the single-flight cache, a crawler that goes away, a failure remembered for a minute with the last good sitemap served, the 50,000 cap) and 2 (nothing the API did not return is listed). The documentation pins keep the guide, the spec and the decision log true.

**Files:**

- Modify: `docs/architecture/00-DISCOVERY-INDEX.md`
- Modify: `docs/architecture/02-ARCHITECTURE.md`
- Modify: `docs/architecture/04-DECISION-LOG.md`
- Modify: `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`
- Modify: `docs/architecture/PHASE-09-public-portal.md`
- Modify: `docs/architecture/UX-BRIEF-portal.md`
- Modify: `docs/development/PORTAL-APP.md`
- Test (modify): `scripts/tests/RepositoryDocs.Tests.ps1`
- Test (create): `tests/TechStrap.Portal.Tests/Seo/SitemapHostTests.cs`

**Interfaces:**
- Consumes: Task 2's `PortalSitemap`, `PortalSitemapBuilder`, `PortalSitemapCache`, `MapPortalSeo`; `PortalFactory`, `FormTestKit.Factory(environment, configure, settings, product)` and `FormTestKit.Client`, `StubApiHandler` (`OnJson`, `OnStatus`, `OnProblem`, `On`, `Count`, `JsonResponse`), `PortalOptions.DefaultProductKey`; `Get-RepoText` (the Pester helper).
- Produces:
  - `SitemapHostTests`: ten host tests over the real wiring and the stub API (`PortalFactory.PublicUrl`).
  - Docs: the "Consequences of the addendum (as built in 09c)" notes in D-045; the PHASE-09 spec with T02, T04 and T12 to T15 ticked (each with its 09c evidence), the deliverables and one success criterion ticked, T09 now deferred to 09d, the sitemap handoff names corrected (`GetKbSitemapRequestHandler`, `ListPublicProductsRequestHandler`, `PortalSitemapBuilder`, `MapSeoSitemap`) and a "Corrections (D-045 addendum, 2026-10-06, PHASE-09c)" block; `PORTAL-APP.md` rewritten for the help center, the SEO head, the cache and the sitemap; the roadmap and discovery rows "09a merged (PR #14); 09b merged (PR #15); 09c complete (pending merge); 09d not started"; the old handler name removed from 02-ARCHITECTURE and UX-BRIEF-portal.
  - `RepositoryDocs.Tests.ps1`: the tick pin updated, a new `Describe 'D-045 as built in 09c'`, and the guide headings.

- [ ] **Step 1: Write the failing tests**

`SitemapHostTests` pins behavior that Task 2 built, so it passes at once; each of its rows is proved by a mutation below instead. The Pester changes are the red ones: the ticks, the corrected names, the guide headings and the as-built notes.

`scripts/tests/RepositoryDocs.Tests.ps1`

```diff
@@ -163,31 +163,31 @@ Describe 'D-045 (the public portal)' {
         (Get-RepoText 'docs/architecture/03-PACKAGE-MAP.md') | Should -Match 'MapSeoRobotsTxt'
     }
 
-    It 'ticks only the tasks and deliverables 09a and 09b fully deliver, and the roadmap and discovery rows say 09b is complete, pending merge' {
+    It 'ticks only the tasks and deliverables 09a, 09b and 09c fully deliver, and the roadmap and discovery rows say 09c is complete, pending merge' {
         $spec = Get-RepoText 'docs/architecture/PHASE-09-public-portal.md'
-        foreach ($number in 1, 3, 5, 6, 7, 8, 10, 11, 17, 19, 21, 22, 23) {
+        foreach ($number in 1, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12, 13, 14, 15, 17, 19, 21, 22, 23) {
             $id = 'P09-T{0:00}' -f $number
-            $spec | Should -Match ('(?m)^- \[x\] \*\*' + $id + '\*\*') -Because "$id is delivered by 09a or 09b"
+            $spec | Should -Match ('(?m)^- \[x\] \*\*' + $id + '\*\*') -Because "$id is delivered by 09a, 09b or 09c"
         }
-        # T02 waits for the KB client of 09c, T09 for the double-submit guard (09c), T04 for the sitemap, T18 for the owner's compose run of the smoke, T12 to T16 are 09c, T20 is deferred.
-        foreach ($number in 2, 4, 9, 12, 13, 14, 15, 16, 18, 20) {
+        # T09 waits for the double-submit guard (09d), T16 is the 09d polish pass, T18 waits for the owner's compose run of the smoke, T20 is deferred.
+        foreach ($number in 9, 16, 18, 20) {
             $id = 'P09-T{0:00}' -f $number
-            $spec | Should -Match ('(?m)^- \[ \] \*\*' + $id + '\*\*') -Because "$id is not finished by 09b"
+            $spec | Should -Match ('(?m)^- \[ \] \*\*' + $id + '\*\*') -Because "$id is not finished by 09c"
         }
-        $spec | Should -Match 'delivered except double-submit \(deferred to 09c, D-045' -Because 'T09 stays open with the reason written down'
+        $spec | Should -Match 'delivered except double-submit \(deferred to 09d, D-045' -Because 'T09 stays open with the reason written down'
         $spec | Should -Match '(?m)^- \[x\] `TechStrap\.Portal` host with `\.env\.example`, forwarded-headers and client-IP forwarding to the API\.'
         $spec | Should -Match '(?m)^- \[x\] Branded layout with per-product theming and NotFound handling\.'
         $spec | Should -Match '(?m)^- \[x\] Contact page with honeypot, attachments, deflection island, submitted page\.'
         $spec | Should -Match '(?m)^- \[x\] Customer ticket view, reply \(incl\. Closed -> follow-up\), lost-link, attachment pass-through\.'
-        $spec | Should -Match '(?m)^- \[ \] Typed clients for public product, public ticket, customer ticket, public KB\.'
-        $spec | Should -Match '(?m)^- \[ \] KB home/category/search/article pages'
-        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 09 \|.*D-045.*\| 09a merged \(PR #14\); 09b complete \(pending merge\); 09c not started'
-        (Get-RepoText 'docs/architecture/00-DISCOVERY-INDEX.md') | Should -Match '(?m)^\| 09 \|.*\| 09a merged \(PR #14\); 09b complete \(pending merge\); 09c not started'
+        $spec | Should -Match '(?m)^- \[x\] Typed clients for public product, public ticket, customer ticket, public KB\.'
+        $spec | Should -Match '(?m)^- \[x\] KB home/category/search/article pages'
+        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 09 \|.*D-045.*\| 09a merged \(PR #14\); 09b merged \(PR #15\); 09c complete \(pending merge\); 09d not started'
+        (Get-RepoText 'docs/architecture/00-DISCOVERY-INDEX.md') | Should -Match '(?m)^\| 09 \|.*\| 09a merged \(PR #14\); 09b merged \(PR #15\); 09c complete \(pending merge\); 09d not started'
     }
 
     It 'has a Portal developer guide, linked from the README, that lists every setting and the known gaps' {
         $guide = Get-RepoText 'docs/development/PORTAL-APP.md'
-        foreach ($heading in '## Run it locally', '### Configuration', '## How a page is served', '### Forms and uploads', '### Suggestions beside the subject', '### The ticket page and attachments', '### The lost-link page', '## Where things live', '## Tests', '## Known gaps') {
+        foreach ($heading in '## Run it locally', '### Configuration', '## How a page is served', '### Forms and uploads', '### Suggestions beside the subject', '### The help centre', '### SEO and structured data', '### Caching and the sitemap', '### The ticket page and attachments', '### The lost-link page', '## Where things live', '## Tests', '## Known gaps') {
             $guide | Should -Match ('(?m)^' + [regex]::Escape($heading))
         }
         foreach ($key in 'API__BASEURL', 'TECHSTRAP_PORTAL_PUBLIC_URL', 'TECHSTRAP_PORTAL_DEFAULT_PRODUCT', 'TECHSTRAP_PORTAL_SHOW_POWERED_BY', 'CANONICALHOST__CANONICALHOST') {
@@ -289,6 +289,51 @@ Describe 'D-045 as built in 09b' {
     }
 }
 
+Describe 'D-045 as built in 09c' {
+    BeforeAll {
+        $log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md'
+        $script:Section = [regex]::Match($log, '(?s)## D-045:.*?(?=\r?\n## D-\d+:|\z)').Value
+        $script:Guide = Get-RepoText 'docs/development/PORTAL-APP.md'
+        $script:Spec = Get-RepoText 'docs/architecture/PHASE-09-public-portal.md'
+    }
+
+    It 'records what the 09c build found, as consequences of the addendum' {
+        foreach ($phrase in 'As built in 09c: the category list and the product list', 'As built in 09c: the pages never fail on a visitor', 'As built in 09c: the cache', 'As built in 09c: structured data',
+                'As built in 09c: the sitemap', 'As built in 09c: the second markup site', 'Known in 09c: the sitemap build', 'Known in 09c: a plain-http image', 'Known in 09c: the category page shows no description',
+                'Known in 09c: the package', 'Resolved in 09c: the product pages no longer link ahead') {
+            $script:Section | Should -Match ([regex]::Escape($phrase)) -Because "the as-built list must carry: $phrase"
+        }
+    }
+
+    It 'has a developer guide that describes the help centre, the SEO head, the cache, the sitemap and the two markup sites' {
+        foreach ($phrase in 'KbArticleBody', 'KbHome', 'KbCategory', 'KbSearch', 'KbArticle', 'Pager', 'StateMessage', 'KbCopy', 'JsonLdText', 'PortalCachePaths', 'AddPortalOutputCache', 'SetOnSuccess', 'X-Correlation-Id',
+                'PortalSitemapCache', 'PortalSitemapBuilder', 'single-flight', '50,000', 'exactly two files', 'KbPaging', 'KbSlugShape', 'ListAsync') {
+            $script:Guide | Should -Match ([regex]::Escape($phrase)) -Because "PORTAL-APP.md must mention $phrase"
+        }
+        $script:Guide | Should -Not -Match 'answers 404 until 09c'
+        $script:Guide | Should -Not -Match 'search only; 09c extends it'
+        $script:Guide | Should -Not -Match 'is used in one file'
+    }
+
+    It 'names the real sitemap handoff in the spec and the architecture, and the old names appear only where the spec says they never existed' {
+        $script:Spec | Should -Match '(?m)^### Corrections \(D-045 addendum, 2026-10-06, PHASE-09c\)'
+        $block = [regex]::Match($script:Spec, '(?ms)### Corrections \(D-045 addendum, 2026-10-06, PHASE-09c\).*?(?=^## )').Value
+        $block | Should -Match 'GetKbSitemapRequestHandler'
+        $block | Should -Match 'IPublicKbClient\.GetSitemapAsync'
+        $block | Should -Match 'PortalSitemapBuilder'
+        $script:Spec | Should -Match 'PortalSitemapBuilder` \(Portal\)'
+        $script:Spec | Should -Match '`MapSeoSitemap` endpoint itself is package-owned'
+        foreach ($old in 'ISitemapEntryProvider', 'GetSitemapEntriesRequestHandler', 'ApiSitemapEntryProvider') {
+            $lines = $script:Spec -split '\r?\n' | Where-Object { $_ -match [regex]::Escape($old) }
+            foreach ($line in $lines) {
+                $line | Should -Match 'do not exist|never existed' -Because "$old may be named only to say it does not exist"
+            }
+        }
+        (Get-RepoText 'docs/architecture/02-ARCHITECTURE.md') | Should -Not -Match 'GetSitemapEntriesRequestHandler'
+        (Get-RepoText 'docs/architecture/UX-BRIEF-portal.md') | Should -Not -Match 'GetSitemapEntriesRequestHandler'
+    }
+}
+
 Describe 'the deployment runbook' {
     BeforeAll { $script:Runbook = Get-RepoText 'docs/self-hosting/DEPLOYMENT.md' }
 
```

`tests/TechStrap.Portal.Tests/Seo/SitemapHostTests.cs` (new)

```csharp
using System.Net;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Seo;
using TechStrap.Portal.Settings;
using TechStrap.Portal.Tests.Forms;

namespace TechStrap.Portal.Tests.Seo;

/// <summary>
/// P09-T04 and T15 at the host: the sitemap the Portal serves (PHASE-09c). Absolute addresses from the public URL, exactly the articles the API returned (it returns published ones only, pinned in the API tests), a shared
/// article under each product, XML-safe addresses, the single-flight cache, a failure that is remembered for a minute while the last good sitemap is served, a crawler that goes away without poisoning the cache, and the
/// 50,000-address cap. Review Focus 4.
/// </summary>
public sealed class SitemapHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Updated = new(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);

    private static PortalFactory Factory(string environment = "Development", Action<IServiceCollection>? configure = null, IReadOnlyDictionary<string, string?>? settings = null)
    {
        var factory = FormTestKit.Factory(environment, configure, settings, product: false);
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products", new[] { new PublicProductSummaryDto("acme", "Acme"), new PublicProductSummaryDto("orbitly", "Orbitly") });
        factory.Api.OnJson(
            HttpMethod.Get,
            "/api/public/kb/acme/sitemap",
            new[] { new KbSitemapEntryDto(null, "general", "shared-tips", Updated), new KbSitemapEntryDto("acme", "accounts", "reset-password", Updated.AddDays(-30)) });
        factory.Api.OnJson(HttpMethod.Get, "/api/public/kb/orbitly/sitemap", new[] { new KbSitemapEntryDto(null, "general", "shared-tips", Updated) });
        return factory;
    }

    private static async Task<(HttpResponseMessage Response, XDocument Xml)> GetAsync(HttpClient client)
    {
        var response = await client.GetAsync("/sitemap.xml", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response, XDocument.Parse(text));
    }

    private static string[] Locations(XDocument xml) => [.. xml.Descendants().Where(element => element.Name.LocalName == "loc").Select(element => element.Value)];

    [Fact]
    public async Task The_sitemap_lists_the_root_each_products_home_help_centre_categories_and_articles_with_absolute_addresses_and_days()
    {
        await using var factory = Factory();
        using var client = FormTestKit.Client(factory);

        var (response, xml) = await GetAsync(client);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/xml");
        response.Headers.CacheControl!.ToString().ShouldBe("public, max-age=300");
        xml.Root!.Name.NamespaceName.ShouldBe("http://www.sitemaps.org/schemas/sitemap/0.9");
        Locations(xml).ShouldBe(
        [
            PortalFactory.PublicUrl + "/",
            PortalFactory.PublicUrl + "/p/acme",
            PortalFactory.PublicUrl + "/p/acme/kb",
            PortalFactory.PublicUrl + "/p/acme/kb/accounts",
            PortalFactory.PublicUrl + "/p/acme/kb/general",
            PortalFactory.PublicUrl + "/p/acme/kb/general/shared-tips",
            PortalFactory.PublicUrl + "/p/acme/kb/accounts/reset-password",
            PortalFactory.PublicUrl + "/p/orbitly",
            PortalFactory.PublicUrl + "/p/orbitly/kb",
            PortalFactory.PublicUrl + "/p/orbitly/kb/general",
            PortalFactory.PublicUrl + "/p/orbitly/kb/general/shared-tips",
        ]);
        var days = xml.Descendants().Where(element => element.Name.LocalName == "url")
            .ToDictionary(url => url.Elements().First(e => e.Name.LocalName == "loc").Value, url => url.Elements().FirstOrDefault(e => e.Name.LocalName == "lastmod")?.Value);
        days[PortalFactory.PublicUrl + "/p/acme/kb/general/shared-tips"].ShouldBe("2026-10-05");
        days[PortalFactory.PublicUrl + "/p/acme/kb/accounts/reset-password"].ShouldBe("2026-09-05");
        days[PortalFactory.PublicUrl + "/p/acme"].ShouldBeNull();
    }

    [Fact]
    public async Task A_shared_article_is_listed_under_every_product_and_nothing_the_api_did_not_return_is_listed()
    {
        await using var factory = Factory();
        using var client = FormTestKit.Client(factory);

        var (_, xml) = await GetAsync(client);

        var locations = Locations(xml);
        locations.ShouldContain(PortalFactory.PublicUrl + "/p/acme/kb/general/shared-tips");
        locations.ShouldContain(PortalFactory.PublicUrl + "/p/orbitly/kb/general/shared-tips");
        locations.Count(location => location.EndsWith("/shared-tips", StringComparison.Ordinal)).ShouldBe(2);
        locations.ShouldAllBe(location => location.StartsWith(PortalFactory.PublicUrl + "/", StringComparison.Ordinal));
        locations.Count(location => new Uri(location).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length == 5).ShouldBe(3, "the three articles the API returned, and no other");
    }

    [Fact]
    public async Task An_address_with_characters_that_mean_something_in_xml_or_a_url_is_escaped_and_the_document_still_parses()
    {
        await using var factory = Factory();
        factory.Api.OnJson(HttpMethod.Get, "/api/public/kb/acme/sitemap", new[] { new KbSitemapEntryDto("acme", "a&b<c>", "x\"y'z&w", Updated) });
        using var client = FormTestKit.Client(factory);

        var (response, xml) = await GetAsync(client);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Locations(xml).ShouldContain(PortalFactory.PublicUrl + "/p/acme/kb/a%26b%3Cc%3E/x%22y%27z%26w");
    }

    [Fact]
    public async Task With_a_default_product_the_root_is_a_redirect_so_it_is_not_listed()
    {
        await using var factory = Factory(settings: new Dictionary<string, string?> { [PortalOptions.DefaultProductKey] = "acme" });
        using var client = FormTestKit.Client(factory);

        var (_, xml) = await GetAsync(client);

        Locations(xml).ShouldNotContain(PortalFactory.PublicUrl + "/");
        Locations(xml).ShouldContain(PortalFactory.PublicUrl + "/p/acme");
    }

    [Fact]
    public async Task Robots_txt_names_the_sitemap_and_the_sitemap_is_where_it_says()
    {
        await using var factory = Factory();
        using var client = FormTestKit.Client(factory);

        var robots = await client.GetStringAsync("/robots.txt", Ct);
        var line = robots.Split('\n', StringSplitOptions.TrimEntries).Single(l => l.StartsWith("Sitemap:", StringComparison.Ordinal));
        var path = new Uri(line["Sitemap:".Length..].Trim()).AbsolutePath;
        using var response = await client.GetAsync(path, Ct);

        line.ShouldBe($"Sitemap: {PortalFactory.PublicUrl}/sitemap.xml");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        robots.ShouldContain("Disallow: /t/");
    }

    [Fact]
    public async Task Twenty_concurrent_requests_make_one_build_and_a_later_request_makes_none()
    {
        await using var factory = Factory();
        factory.Api.On(HttpMethod.Get, "/api/public/products", _ =>
        {
            Thread.Sleep(150);
            return StubJson(new[] { new PublicProductSummaryDto("acme", "Acme"), new PublicProductSummaryDto("orbitly", "Orbitly") });
        });
        using var client = FormTestKit.Client(factory);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => client.GetAsync("/sitemap.xml", Ct)));
        using var later = await client.GetAsync("/sitemap.xml", Ct);

        responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.OK);
        factory.Api.Count(HttpMethod.Get, "/api/public/products").ShouldBe(1);
        factory.Api.Count(HttpMethod.Get, "/api/public/kb/acme/sitemap").ShouldBe(1);
        factory.Api.Count(HttpMethod.Get, "/api/public/kb/orbitly/sitemap").ShouldBe(1);
        later.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_crawler_that_goes_away_does_not_poison_the_cache_and_the_next_request_is_served_from_the_same_build()
    {
        await using var factory = Factory();
        factory.Api.On(HttpMethod.Get, "/api/public/products", _ =>
        {
            Thread.Sleep(400);
            return StubJson(new[] { new PublicProductSummaryDto("acme", "Acme"), new PublicProductSummaryDto("orbitly", "Orbitly") });
        });
        using var client = FormTestKit.Client(factory);
        using var impatient = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        impatient.CancelAfter(TimeSpan.FromMilliseconds(80));

        await Should.ThrowAsync<OperationCanceledException>(() => client.GetAsync("/sitemap.xml", impatient.Token));
        var (response, xml) = await GetAsync(client);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Locations(xml).ShouldContain(PortalFactory.PublicUrl + "/p/orbitly");
        factory.Api.Count(HttpMethod.Get, "/api/public/products").ShouldBe(1, "the build the first crawler started was not canceled and not repeated");
    }

    [Fact]
    public async Task A_failed_build_is_a_500_and_is_not_repeated_for_a_minute()
    {
        await using var factory = Factory("Production");
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products", HttpStatusCode.InternalServerError, "boom", "A table name the visitor must never see.");
        using var client = FormTestKit.Client(factory);

        using var first = await client.GetAsync("/sitemap.xml", Ct);
        using var second = await client.GetAsync("/sitemap.xml", Ct);
        using var third = await client.GetAsync("/sitemap.xml", Ct);

        first.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        second.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        third.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await first.Content.ReadAsStringAsync(Ct)).ShouldNotContain("table name");
        factory.Api.Count(HttpMethod.Get, "/api/public/products").ShouldBe(1, "the API is not asked again while the failure is remembered");
        first.Headers.CacheControl?.Public.ShouldNotBe(true);
    }

    [Fact]
    public async Task When_a_rebuild_fails_the_last_good_sitemap_is_served_and_the_api_is_left_alone_for_the_failure_lifetime()
    {
        await using var factory = Factory(configure: services => services.AddSingleton(new PortalSitemapCache(
            new MemoryCache(new MemoryCacheOptions()), NullLogger<PortalSitemapCache>.Instance, TimeSpan.FromMilliseconds(400), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1))));
        using var client = FormTestKit.Client(factory);
        var (_, good) = await GetAsync(client);

        await Task.Delay(TimeSpan.FromMilliseconds(900), Ct);
        factory.Api.OnStatus(HttpMethod.Get, "/api/public/products", HttpStatusCode.InternalServerError);
        var (staleResponse, stale) = await GetAsync(client);
        var (againResponse, again) = await GetAsync(client);

        staleResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        againResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        Locations(stale).ShouldBe(Locations(good));
        Locations(again).ShouldBe(Locations(good));
        factory.Api.Count(HttpMethod.Get, "/api/public/products").ShouldBe(2, "one good build and one failed rebuild; nothing while the failure is remembered");
    }

    [Fact]
    public async Task At_most_fifty_thousand_addresses_are_served()
    {
        await using var factory = Factory();
        factory.Api.OnJson(
            HttpMethod.Get,
            "/api/public/kb/acme/sitemap",
            Enumerable.Range(0, PortalSitemapBuilder.MaxUrls + 25).Select(i => new KbSitemapEntryDto("acme", "accounts", $"article-{i}", Updated)).ToArray());
        using var client = FormTestKit.Client(factory);

        var (response, xml) = await GetAsync(client);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Locations(xml).Length.ShouldBe(PortalSitemapBuilder.MaxUrls, "the root page and the products' addresses together fill exactly one sitemap file");
        Locations(xml)[0].ShouldBe(PortalFactory.PublicUrl + "/", "the static entry is kept and the products' own addresses are cut");
    }

    private static HttpResponseMessage StubJson<T>(T body) => TechStrap.Portal.Tests.Api.StubApiHandler.JsonResponse(HttpStatusCode.OK, body);
}
```


- [ ] **Step 2: Run the Pester block to verify it fails**

```bash
pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal -Path scripts/tests/RepositoryDocs.Tests.ps1
```

Expected (recorded with the documentation changes set aside): `Tests Passed: 33, Failed: 5`.

- [ ] **Step 3: Write the documentation**

The D-045 as-built notes, the spec (ticks with evidence, the corrected sitemap handoff, the corrections block), the developer guide, the two status rows, and the old handler name removed from the two other documents.

`docs/architecture/00-DISCOVERY-INDEX.md`

```diff
@@ -30,7 +30,7 @@
 | 06 | [Ticket operations](PHASE-06-ticket-operations.md) | 05 | 07, 08, 09 | Complete |
 | 07 | [Admin app](PHASE-07-admin-app.md) | 02, 06 | 08 (editor UI), 10 | 07 complete (pending merge): 07a merged (PR #9), 07b merged (PR #10), 07c implemented; owner action 7 (Authentik) still open, so P07-T02 stays unticked |
 | 08 | [Knowledge base](PHASE-08-knowledge-base.md) | 06 (07 for the editor UI) | 09 | PHASE-08 complete (pending merge): the API (Tasks 1-8) and the Admin (editor, categories, article picker) are implemented; the owner's manual checks are open |
-| 09 | [Public portal](PHASE-09-public-portal.md) | 02, 06, 08 | 12 | 09a merged (PR #14); 09b complete (pending merge); 09c not started |
+| 09 | [Public portal](PHASE-09-public-portal.md) | 02, 06, 08 | 12 | 09a merged (PR #14); 09b merged (PR #15); 09c complete (pending merge); 09d not started |
 | 10 | [Live updates](PHASE-10-live-updates.md) | 07 | 12 | Not started |
 | 11 | [Client SDK](PHASE-11-client-sdk.md) | 05 | 12 | Not started |
 | 12 | [Release hardening](PHASE-12-release-hardening.md) | all | v1.0.0 | Not started |
```

`docs/architecture/02-ARCHITECTURE.md`

```diff
@@ -339,7 +339,7 @@ Not entry points: `TicketChangePublishingInterceptor` (Infrastructure post-commi
 | `/hubs/tickets` handshake (API) | SignalR framework connection setup; the hub methods are the use cases (section 7.5) | JWT bearer and the Agent policy at handshake only |
 | API host startup: migrate database (advisory lock) and dev data seeder (`IDevelopmentDataSeeder`) | Host startup steps with no request input and no transport outcome; the seeder runs only in Development with `TECHSTRAP_SEED_DEV_DATA=true` | No application workflow; API only |
 
-Admin and Portal framework endpoints (Blazor `_blazor` hub, OIDC callback, antiforgery) are framework-owned and execute no TechStrap workflow. Portal `/sitemap.xml` and `robots.txt` are presentation endpoints that call the API through the typed client (`GetSitemapEntriesRequestHandler` at the API); they hold no business logic.
+Admin and Portal framework endpoints (Blazor `_blazor` hub, OIDC callback, antiforgery) are framework-owned and execute no TechStrap workflow. Portal `/sitemap.xml` and `robots.txt` are presentation endpoints that call the API through the typed client (`GetKbSitemapRequestHandler` and `ListPublicProductsRequestHandler` at the API); they hold no business logic.
 
 ## 8. Razor presentation-boundary tables
 
```

`docs/architecture/04-DECISION-LOG.md`

```diff
@@ -1751,6 +1751,19 @@ The owner's rulings for PHASE-09c, and what the plan's spike proved. They extend
 - **Head and JSON-LD.** `SeoHead` works in static SSR (the layout already has `<HeadOutlet />`). The ld+json block is rendered as `type="application/ld&#x2B;json"`, which a browser reads as `application/ld+json`, so the tests parse the page with an HTML parser. The block is data, not script, so the CSP is unchanged (`script-src 'self'`). The package's `JsonLd` lets a hostile title inject an element (`<img src=x onerror=...>` became part of the page); the converter-based `JsonLdText` keeps the same title inside the string, and the Razor-encoded meta tags and title were already safe. A Razor file cannot hold the text `</script` in a string (the Razor parser reads it as a tag), so the hostile texts live in C#.
 - **The sitemap.** `MapSeoSitemap` accepts a provider that reads an `IMemoryCache` with single-flight: twenty concurrent requests made one build, the result is `application/xml` with each `<loc>` XML-escaped, and `cacheDuration` only sets the client's `Cache-Control`. `MemoryCacheOptions` has no `TimeProvider` (only the obsolete `ISystemClock`), so the cache takes its two lifetimes as constructor values and the tests use short real ones.
 
+**Consequences of the addendum (as built in 09c)**
+- **As built in 09c: the category list and the product list.** `ListPublicKbCategoryArticlesRequestHandler` and `ListPublicProductsRequestHandler` are as ruled; the repository method asks `PublishedIn` for the product's articles in a category of that slug and returns null when there is none, which the handler turns into the one `kb-category-not-found` 404. The Portal also treats a page past the end (a 200 with no items) as the neutral 404, so there is one page per real page number and no empty page for a crawler to find. No migration was needed (`has-pending-model-changes` is clean).
+- **As built in 09c: the pages never fail on a visitor's text.** The `page` and `q` query values are read as text. `KbPaging.Parse` turns anything that is not a whole number of one or more into page one (the framework's own binding to a number answered 500 for `?page=abc`), `KbSearchText.Clean` cuts a text at 200 characters without splitting a surrogate pair, and `PortalRoutes.KbSearch(key, text, page)` escapes the text in every paging link. A key or slug that is not a slug is refused by the client without a call (`KbSlugShape`, like `ProductKeyShape`).
+- **As built in 09c: the cache.** `PortalCachePaths.IsCacheable` is the one predicate: a help-centre page whose `page` value is absent or one to four digits. The search page, the form pages, the suggest adapter, `/t/*`, `/not-found`, the sitemap and every non-200 answer are never kept; the search page also gets `Cache-Control: no-store` from a header rule, and a delivered KB page gets `public, max-age=60` from `PathHeaderRule.SetOnSuccess` (Hosting; the Admin's use of the shared wiring is unchanged and pinned by `PublicCacheHeaderPinTests`). `UsePortalOutputCache` also sets the request's own `X-Correlation-Id` again when the response starts, because a stored copy replays the first request's. `ProgramOrderTests` pins the order of the pipeline.
+- **As built in 09c: structured data.** `JsonLdText` and the Portal's own `BreadcrumbListLd` and `ArticleSchema` records replace the package's `BreadcrumbListSchema` for the article page, because the package's strings cannot carry the converter. The hostile-title tests pin the head, the heading, the breadcrumb and both JSON-LD blocks. `KbPlainText.Describe` takes the description (the summary, else the first sentence of the body's first paragraph as plain text, at most 160 characters).
+- **As built in 09c: the sitemap.** `PortalSitemapBuilder` makes one products call and one sitemap call per product, and fails whole when any call fails (a half sitemap kept for 15 minutes would be worse than none). `PortalSitemapCache` holds the entries for 15 minutes with single-flight, its own cancellation token, a 2-minute build timeout, a failure remembered for 1 minute and the last good sitemap served meanwhile; with none, the request is the 500 error page. The static root entry counts against the 50,000-address limit, so the whole file never exceeds it. The build runs without the request's execution context and with a stand-in `HttpContext` that carries only the first visitor's address.
+- **As built in 09c: the second markup site.** `KbArticleBody` renders the API's sanitized HTML byte for byte (`KbArticleHostTests` compares it with the API's string); `PortalRules.MarkupStringSites` lists exactly it and `CustomerMessageBody`, and a test counts that the word appears in those two files and nowhere else.
+- **Known in 09c: the sitemap build's calls carry the first crawler's address.** The build's `X-Forwarded-For` is the address of the visitor whose request started it, so the API rate-limits that address (120 a minute) for the 1 + N calls of a build; a Portal with more than about 100 products would fail its build. Accepted for the products this install has; a service address for the build is the follow-up.
+- **Known in 09c: a plain-http image in an article is blocked** by `img-src 'self' https: data:` in Production. That is the right posture and is documented, not changed.
+- **Known in 09c: the category page shows no description** (the article list does not carry it, and a second call per page was not worth it), the product home still has no category list, and the visual polish, the no-JS and accessibility pass, the double-send guard and the JavaScript niceties of 09b's known gaps are PHASE-09d.
+- **Known in 09c: the package's `JsonLd` is still unsafe for any other caller.** The Portal avoids it for strings; the problem (a `</script>` in a value ends the block) is to be reported upstream to `SyntaxCircus.Blazor.Seo`.
+- **Resolved in 09c: the product pages no longer link ahead.** The search box, the KB links on the contact and received pages and the sitemap named by robots.txt all answer.
+
 ### Approval
 - **Approved by:** Jon Seeley (owner, PHASE-09 planning)
 - **Approved on:** 2026-10-05
```

`docs/architecture/99-IMPLEMENTATION-ROADMAP.md`

```diff
@@ -25,7 +25,7 @@ Cross-cutting conventions every phase follows (fixed during the consistency revi
 | 06 | [Ticket operations](PHASE-06-ticket-operations.md) | 05 | 07, 08, 09 | Alongside 02 and 11 | D-006, D-008, D-009, D-022, D-035, D-036, D-037, D-038, D-039 | Complete (PRs #6, #7 and #8 merged) |
 | 07 | [Admin app](PHASE-07-admin-app.md) | 02, 06 | 08 (editor UI), 10 | 11 alongside; 08 API work alongside | D-017, D-022, D-040, D-041 | 07 complete (pending merge): 07a merged (PR #9), 07b merged (PR #10), 07c implemented; owner action 7 (Authentik) still open, so P07-T02 stays unticked |
 | 08 | [Knowledge base](PHASE-08-knowledge-base.md) | 06 (07 for the editor UI) | 09 | API tasks T01 to T12 alongside 07; editor tasks wait for 07 | D-011, D-014, D-021, D-044 | PHASE-08 complete (pending merge): the API (Tasks 1-8) and the Admin (editor, categories, article picker) are implemented; the owner's manual checks are open |
-| 09 | [Public portal](PHASE-09-public-portal.md) | 02, 06, 08 | 12 | 10 and 11 alongside | D-002, D-017, D-019, D-045 | 09a merged (PR #14); 09b complete (pending merge); 09c not started |
+| 09 | [Public portal](PHASE-09-public-portal.md) | 02, 06, 08 | 12 | 10 and 11 alongside | D-002, D-017, D-019, D-045 | 09a merged (PR #14); 09b merged (PR #15); 09c complete (pending merge); 09d not started |
 | 10 | [Live updates](PHASE-10-live-updates.md) | 07 | 12 | 08, 09, 11 alongside | D-007, D-018 | Not started |
 | 11 | [Client SDK](PHASE-11-client-sdk.md) | 05 | 12 | Alongside 06 to 10 | D-005, D-020 | Not started |
 | 12 | [Release hardening](PHASE-12-release-hardening.md) | all | v1.0.0 | Last; security, load, restore and UAT tasks can overlap once their inputs exist | D-003, D-022 | Not started |
```

`docs/architecture/PHASE-09-public-portal.md`

```diff
@@ -27,7 +27,7 @@ sitemap, robots) through `SyntaxCircus.Blazor.Seo`.
 - **Abuse controls at the edge:** honeypot field (hidden, `autocomplete=off`, `tabindex=-1`), antiforgery, request body size limit equal to the API limit, client-side file hints only (server enforces size/type allowlist). **Rate limits are enforced by the API**, which must see the real client IP: the portal forwards the original client IP (trusted `X-Forwarded-For` from the reverse proxy) to the API through `.AddForwardedClientIp()`, and the API trusts forwarded headers only from the pinned compose subnet (D-019; `CLIENT_IP_RATE_LIMITING.md`, P01/P05 config).
 - **Attachments for customers:** `/t/{token}/attachments/{id}` is a portal-hosted pass-through adapter that forwards to `GET /api/customer/attachments/{id}` (`GetCustomerAttachmentRequestHandler`, D-038) with `X-Ticket-Token` forwarded server-side and streams the response (no business logic), with `Content-Disposition: attachment` and `nosniff`. Exempt per D-017; same pattern as the admin.
 - **Rendered HTML:** the KB article body arrives sanitized from the API (`PublishedKbArticleDto.Html`) and is rendered through `MarkupString` at exactly one site (`KbArticleBody`); message bodies likewise at `CustomerMessageBody`. Portal does not re-sanitize (single source of truth), but an architecture/lint test restricts `MarkupString` to those two components.
-- **SEO** via `SyntaxCircus.Blazor.Seo`: `AddSyntaxCircusSeo`, `UseCanonicalHost`, `MapRobotsTxt`, `MapSitemap` with entries provided by an `ISitemapEntryProvider` implementation that calls `GetSitemapEntriesRequestHandler` through the API (cached 15 minutes, **Assumption**). `SeoHead` per page; JSON-LD `BreadcrumbListSchema` on KB pages and an article schema (custom POCO) on article pages. Contact/ticket pages are `noindex`.
+- **SEO** via `SyntaxCircus.Blazor.Seo`: `AddSyntaxCircusSeo`, `UseSyntaxCircusSeo`, `MapSeoRobotsTxt`, `MapSeoSitemap` with a provider (`PortalSitemap`) that calls `GetKbSitemapRequestHandler` and the products list through the API (cached 15 minutes in an `IMemoryCache`, **Assumption**). `SeoHead` per page; JSON-LD `BreadcrumbListSchema` on KB pages and an article schema (custom POCO) on article pages. Contact/ticket pages are `noindex`.
 - **Caching:** KB list/article/category pages use ASP.NET output caching with short TTL and vary by route (**Assumption**: 60 s); never cache `/t/*` or form pages.
 - **Accessibility/UX** follow `UX-BRIEF-portal.md` (mobile-first, no JS required, visible focus, error summaries, labels, 4.5:1 contrast with the computed `--ts-on-accent` and `--ts-accent-ink`).
 - **Tests:** bUnit for component logic, `WebApplicationFactory<Portal>` with a fake API handler for page-level SSR output (token headers, noindex, canonical, sitemap). Playwright e2e optional (**Assumption**, P09-T20).
@@ -56,6 +56,16 @@ The 09b rulings; where this page and the addendum differ, the addendum wins.
 - **Message bodies.** `CustomerMessageBody` is the only `MarkupString` site in 09b; `KbArticleBody` follows in 09c.
 - **Received page.** It shows the ticket number only.
 
+### Corrections (D-045 addendum, 2026-10-06, PHASE-09c)
+
+The 09c rulings; where this page and the addendum differ, the addendum wins.
+- **Sitemap names.** The handoff is `GetKbSitemapRequestHandler` (P08, one product) and `ListPublicProductsRequestHandler` (09c, `GET api/public/products`), read through `IPublicKbClient.GetSitemapAsync` and `IPublicProductClient.ListAsync` by `PortalSitemapBuilder`; `GetSitemapEntriesRequestHandler`, `ISitemapEntryProvider` and `ApiSitemapEntryProvider` never existed.
+- **Category page.** `GET api/public/kb/{productKey}/categories/{categorySlug}/articles` (paged, published only, 404 for an unknown, invisible or empty category) feeds it; the search endpoint cannot, because a blank text gives an empty page.
+- **Article JSON-LD.** The breadcrumb list and the article are Portal records whose strings are `JsonLdText`: the package's `JsonLd` writes `<` unescaped, so a `</script>` in a title would end the block.
+- **Caching.** One base policy with a path predicate (not an attribute): 60 seconds, varying by the `page` query value only; a delivered page tells browsers `public, max-age=60`; the search page, the form pages and `/t/*` are never kept.
+- **Empty states.** An empty category is the neutral 404 (the API says so), so the empty state is the help-center home of a product with no article and a search with no result.
+- **Search page.** `/p/{key}/kb/search?q=&page=`; a query makes it `noindex`.
+
 ## Application Boundaries
 
 Follow _template APPLICATION_ARCHITECTURE.md. This phase adds **no new server
@@ -74,8 +84,8 @@ consumed and the portal-hosted framework/adapter endpoints.
 | KB search page and deflection suggestions | `SearchPublicKbArticlesRequestHandler` (P08) | `IPublicKbClient` | `PublicKbClient` | `KbSearchResponse`; empty on no results | Consumed; same use case for search and deflection |
 | KB article page | `GetPublishedKbArticleRequestHandler` (P08) | `IPublicKbClient` | `PublicKbClient` | `PublishedKbArticleDto`; 404 -> NotFound | Consumed; no new server entry point |
 | KB home/category lists | `ListPublicKbCategoriesRequestHandler` (P08) | `IPublicKbClient` | `PublicKbClient` | Category DTOs with counts | Consumed; no new server entry point |
-| `GET /sitemap.xml` entries | `GetSitemapEntriesRequestHandler` (P08) | `ISitemapEntryProvider` (Portal) -> `IPublicKbClient` | `ApiSitemapEntryProvider` | `SitemapEntry[]` | Consumed; the `MapSitemap` endpoint itself is package-owned (`Blazor.Seo`) and runs no application workflow |
-| `GET /robots.txt` | Exempt | `Blazor.Seo` | `MapRobotsTxt` | Static text | Exempt: package-owned, no workflow |
+| `GET /sitemap.xml` entries | `GetKbSitemapRequestHandler` (P08) and `ListPublicProductsRequestHandler` (09c) | `PortalSitemapBuilder` (Portal) -> `IPublicProductClient`, `IPublicKbClient` | `PortalSitemap` and `PortalSitemapCache` | `SitemapEntry[]` | Consumed; the `MapSeoSitemap` endpoint itself is package-owned (`Blazor.Seo`) and runs no application workflow |
+| `GET /robots.txt` | Exempt | `Blazor.Seo` | `MapSeoRobotsTxt` | Static text | Exempt: package-owned, no workflow |
 | `/health/live`, `/health/ready`, static assets (`/_framework`, SCSS output, logos) | Exempt | `SyntaxCircus.AspNetCore.Common` | Package health endpoints | 200/503 | Exempt operational/static endpoints |
 
 ## Razor Component Boundaries
@@ -128,11 +138,11 @@ Not used: `Blazor.Auth` (portal is anonymous), `Blazor.Tracking` (Not applicable
 ## Deliverables
 
 - [x] `TechStrap.Portal` host with `.env.example`, forwarded-headers and client-IP forwarding to the API.
-- [ ] Typed clients for public product, public ticket, customer ticket, public KB.
+- [x] Typed clients for public product, public ticket, customer ticket, public KB.
 - [x] Branded layout with per-product theming and NotFound handling.
 - [x] Contact page with honeypot, attachments, deflection island, submitted page.
 - [x] Customer ticket view, reply (incl. Closed -> follow-up), lost-link, attachment pass-through.
-- [ ] KB home/category/search/article pages with SEO, JSON-LD, sitemap, robots.
+- [x] KB home/category/search/article pages with SEO, JSON-LD, sitemap, robots.
 - [ ] Page-level SSR tests and bUnit tests; portal container healthy under compose.
 
 ## Actionable Tasks
@@ -141,19 +151,21 @@ Not used: `Blazor.Auth` (portal is anonymous), `Blazor.Tracking` (Not applicable
   - **Depends on:** P01 (host skeleton)
   - **Validation:** Options validation unit test fails fast on missing API URL; route constants used by every page (no inline route strings repeated).
   - **09a evidence:** `PortalOptionsValidatorTests` and `PortalOptionsHostTests` (the start fails naming the key), `PortalRoutesTests` and `RouteLiteralTests` (every route once, no inline route string), `ConfigContract.Tests.ps1` and `ProductionBlankTemplateTests` (the keys in every template). The keys are `API__BASEURL`, `TECHSTRAP_PORTAL_PUBLIC_URL` and `TECHSTRAP_PORTAL_DEFAULT_PRODUCT`; `Seo:BaseUrl` is derived from the public URL (D-045).
-- [ ] **P09-T02** Implement typed clients (`IPublicProductClient`, `IPublicTicketClient`, `ICustomerTicketClient`, `IPublicKbClient`) with ProblemDetails -> `Result`, GET-only retry, multipart submit, and `.AddForwardedClientIp()` forwarding the original client IP (D-019)
+- [x] **P09-T02** Implement typed clients (`IPublicProductClient`, `IPublicTicketClient`, `ICustomerTicketClient`, `IPublicKbClient`) with ProblemDetails -> `Result`, GET-only retry, multipart submit, and `.AddForwardedClientIp()` forwarding the original client IP (D-019)
   - **Depends on:** P09-T01, P05, P06, P08 DTOs
   - **Validation:** Stub-handler tests: success/400/404/429/503; POST not retried; `X-Forwarded-For` set from the trusted inbound header; token header never logged (log assertion).
   - **09a:** done: `ApiConnection`, `ProblemMapping`, the token capability and `IPublicProductClient` (`ApiConnectionTests`, `ProblemMappingTests`, `PublicProductClientTests`, `ForwardedClientIpHostTests`, `TicketTokenLeakTests`), with the fake-API harness. The ticket and KB clients arrive with 09b and 09c, so this task stays open.
   - **09b:** the public ticket, customer ticket and KB (search) clients are done (`PublicTicketClientTests`, `CustomerTicketClientTests`, `PublicKbClientTests`, `ApiConnectionStreamTests`); the rest of the KB client arrives with 09c, so this task stays open.
+  - **09c evidence:** the rest of the KB client (categories, a category's articles, the article, the sitemap and a paged search) and `IPublicProductClient.ListAsync` are done (`PublicKbClientTests`, `PublicProductClientTests`, `KbSlugShapeTests`): every call is a read, retried, forwarding the visitor's address, and a key or slug that is not a slug is the uniform not-found without a call.
 - [x] **P09-T03** Implement `BrandingThemeFactory`, `PortalLayout`, header/footer and the product-scope resolution (unknown/inactive -> NotFound)
   - **Depends on:** P09-T02, P02 tokens
   - **Validation:** Theory over accent colors (black, white, mid-gray, brand) asserts the computed `--ts-on-accent` meets 4.5:1 on the accent and `--ts-accent-ink` meets 4.5:1 on white; invalid color falls back to the default; bUnit: unknown key renders NotFound.
   - **09a evidence:** `ProductThemeViewModelTests`, `PortalLayoutTests`, `NeutralPagesGuardTests`, `ProductHomeHostTests`. There is no `BrandingThemeFactory` (D-045); the contrast theory is `ProductAccentContrastTests`.
-- [ ] **P09-T04** Wire `Blazor.Seo` (`AddSyntaxCircusSeo`, `UseCanonicalHost`, `MapRobotsTxt`, `MapSitemap` with `ApiSitemapEntryProvider`) and security headers/CSP
+- [x] **P09-T04** Wire `Blazor.Seo` (`AddSyntaxCircusSeo`, `UseSyntaxCircusSeo`, `MapSeoRobotsTxt`, `MapSeoSitemap` with the `PortalSitemap` provider) and security headers/CSP
   - **Depends on:** P09-T02
   - **Validation:** Host test: `/robots.txt` disallows `/t/`; `/sitemap.xml` contains published KB URLs only and is cached; response headers include CSP and `X-Content-Type-Options`.
   - **09a:** done: Seo wiring, `/robots.txt`, the canonical host and the per-path headers (`SeoHostTests`, `TicketHeaderHostTests`, `PathHeaderRuleHostTests`). The sitemap arrives with 09c, so this task stays open.
+  - **09c evidence:** the sitemap is mapped (`SitemapHostTests`: absolute addresses, a shared article under each product, the single-flight cache, a crawler that goes away, a failure remembered for a minute with the last good sitemap served, the 50,000 cap; `PortalSitemapBuilderTests`, `PortalSitemapCacheTests`) and robots.txt names it.
 - [x] **P09-T05** Build `App`/`Routes`/`NotFoundPage` with `GlobalErrorBoundary` and the product home page
   - **Depends on:** P09-T03
   - **Validation:** bUnit/host test: home renders branded name and KB search box; unmatched route -> NotFound with 404 status.
@@ -173,7 +185,7 @@ Not used: `Blazor.Auth` (portal is anonymous), `Blazor.Tracking` (Not applicable
 - [ ] **P09-T09** Build `CustomerReplyForm` with attachments, including Closed -> follow-up flow handling
   - **Depends on:** P09-T08
   - **Validation:** Host test: reply on Open ticket refreshes thread; reply on Closed ticket shows follow-up ticket link; oversize/disallowed attachment shows error; double-submit guarded.
-  - **09b:** delivered except double-submit (deferred to 09c, D-045 'Known in 09b').
+  - **09b:** delivered except double-submit (deferred to 09d, D-045 'Known in 09b').
   - **09b evidence:** `TicketReplyHostTests`, `FollowUpLinkTests`, `ReplyAndEmailRulesTests`: a reply redirects to the same page, a reply on a Closed ticket redirects to the follow-up's own page on this site, a link that cannot be read gives a generic confirmation.
 - [x] **P09-T10** Build `LostLinkPage` (`/p/{key}/lost-link`)
   - **Depends on:** P09-T03
@@ -183,18 +195,22 @@ Not used: `Blazor.Auth` (portal is anonymous), `Blazor.Tracking` (Not applicable
   - **Depends on:** P09-T08
   - **Validation:** Host test: streams bytes with `attachment` disposition and `nosniff`; other tokens/ids -> uniform 404; no Infrastructure reference (architecture test).
   - **09b evidence:** `TicketAttachmentHostTests`, `ApiConnectionStreamTests`, `TicketUniformNotFoundHostTests`, `TicketHeaderHostTests` (the real route under the sandbox rule).
-- [ ] **P09-T12** Build `KbHomePage` and `KbCategoryPage` with paging and `KbArticleCard`/`KbBreadcrumbs`
+- [x] **P09-T12** Build `KbHomePage` and `KbCategoryPage` with paging and `KbArticleCard`/`KbBreadcrumbs`
   - **Depends on:** P09-T03, P08-T08
   - **Validation:** bUnit: empty category shows empty state; paging links preserve query; shared + product articles appear.
-- [ ] **P09-T13** Build `KbSearchPage` (GET form) and result highlighting using the API snippet (plain text, escaped)
+  - **09c evidence:** `KbHomeHostTests`, `KbCategoryHostTests`, `KbComponentTests`, `KbPagingTests`, `PortalRoutesTests`. The pages are `KbHome`, `KbCategory`, `KbSearch` and `KbArticle` in `Components/Pages`, the shared pieces `KbArticleCard`, `KbBreadcrumbs`, `KbSearchBox`, `Pager` and `StateMessage`, the words `KbCopy`. An empty category is the neutral 404 (the API's ruling), so the empty state is a product with no article; a page past the end is the neutral 404; a shared article appears under the product the visitor is on.
+- [x] **P09-T13** Build `KbSearchPage` (GET form) and result highlighting using the API snippet (plain text, escaped)
   - **Depends on:** P09-T12
   - **Validation:** bUnit: empty query shows prompt; no results shows contact-us link; snippet HTML-encoded (test with `<b>` in data).
-- [ ] **P09-T14** Build `KbArticlePage`/`KbArticleBody` with `SeoHead`, canonical URL, Open Graph, `BreadcrumbListSchema` and article JSON-LD
+  - **09c evidence:** `KbSearchHostTests` (the prompt, the contact link, a snippet with `<b>` and a hostile text shown as text, `noindex` for a query, paging links that keep the escaped text, a cut at 200 characters, a mangled `page` value, never kept) and `KbComponentTests`.
+- [x] **P09-T14** Build `KbArticlePage`/`KbArticleBody` with `SeoHead`, canonical URL, Open Graph, `BreadcrumbListSchema` and article JSON-LD
   - **Depends on:** P09-T04, P09-T12
   - **Validation:** Host test parses the page head: title, description, canonical, `og:*`, valid JSON-LD; unpublished slug -> 404; body markup identical to API HTML (no re-encoding bugs); `MarkupString` only in `KbArticleBody`/`CustomerMessageBody` (architecture test).
-- [ ] **P09-T15** Add output caching for KB pages and sitemap; exclude `/t/*` and forms
+  - **09c evidence:** `KbArticleHostTests` (the head parsed, both JSON-LD blocks parsed, a hostile title, category, summary and product name cannot leave the page or the JSON-LD, the body equals the API's HTML, the neutral 404s), `JsonLdTextTests`, `KbStructuredDataTests`, `KbPlainTextTests`, `PortalRuleTests` (exactly two markup sites).
+- [x] **P09-T15** Add output caching for KB pages and sitemap; exclude `/t/*` and forms
   - **Depends on:** P09-T12, P09-T14
   - **Validation:** Host test: KB responses carry cache headers and hit the fake API once for repeated requests; `/t/*` has `no-store`.
+  - **09c evidence:** `KbPageCacheHostTests` and `OutputCachePipelineTests` (one API call for a repeated request, the request's own headers and correlation id on a hit, a 404 and a 429 never stored, nothing else kept, no key shared between products, no cookie), `PortalCachePathsTests`, `ProgramOrderTests`, `PathHeaderRuleHostTests` and `PublicCacheHeaderPinTests` (the Hosting rule and the unchanged Admin).
 - [ ] **P09-T16** Apply BRAND.md/UX-BRIEF-portal styling: responsive layout, error summaries, focus states, themed accent usage, no-JS verification
   - **Depends on:** P09-T06, P09-T08, P09-T14
   - **Validation:** UX-BRIEF-portal checklist completed; manual run with JavaScript disabled covers contact -> submitted and ticket view -> reply; axe run has no critical findings; Lighthouse accessibility >= 90 (**Assumption**).
@@ -233,7 +249,7 @@ Not used: `Blazor.Auth` (portal is anonymous), `Blazor.Tracking` (Not applicable
 - [ ] Following the emailed `/t/{token}` link shows the public conversation; the customer can reply; replying on a Closed ticket creates and links a follow-up ticket.
 - [ ] Invalid, expired and revoked tokens are indistinguishable (identical 404); lost-link responses are identical for known and unknown emails.
 - [ ] Each product's portal pages use its name, logo and accent color, including readable contrast; an unknown product key shows NotFound.
-- [ ] KB pages are browsable, searchable, SEO-tagged with sitemap/robots as specified; ticket pages are `noindex`/disallowed.
+- [x] KB pages are browsable, searchable, SEO-tagged with sitemap/robots as specified; ticket pages are `noindex`/disallowed.
 - [ ] The contact URL prefills `subject`, `name` and `email` (visible, editable, validated like typed input); "Powered by TechStrap" links to the GitHub repo and disappears when `TECHSTRAP_PORTAL_SHOW_POWERED_BY=false`; agents appear as the resolved public name (D-024).
 - [ ] Portal request logs contain no access tokens; token pages send `no-store` and `no-referrer`.
 - [ ] Rate limits observe the real client IP through the portal.
```

`docs/architecture/UX-BRIEF-portal.md`

```diff
@@ -294,7 +294,7 @@ team should keep them stable for SEO.
 - **Screen/route:** System pages: uniform not-found/invalid link, rate-limited,
   server error, `sitemap.xml`, `robots.txt`
   - **Purpose:** safe, calm failure states; crawler support
-    (`GetSitemapEntriesRequestHandler`). Product-branded where a product is
+    (`GetKbSitemapRequestHandler`). Product-branded where a product is
     known, otherwise neutral. Plain copy: no mascot, no jokes, even on 404.
   - **Primary actions:** go home, search help, request a new link.
   - **Data/state:** none.
@@ -604,7 +604,7 @@ Target: **WCAG 2.2 AA** (the audience is the general public).
   Open Graph/Twitter tags using the product name and logo, and one `h1`
   (`SyntaxCircus.Blazor.Seo`).
 - `sitemap.xml` lists product homes, categories and published articles
-  (`GetSitemapEntriesRequestHandler`); `robots.txt` references it.
+  (`GetKbSitemapRequestHandler`, `ListPublicProductsRequestHandler`); `robots.txt` references it.
 - **`noindex`:** customer ticket pages (`/t/*`), the contact confirmation,
   lost-link, search result pages with query strings (Assumption), error pages.
   Customer ticket pages also send `Referrer-Policy: no-referrer` and `no-store`.
```

`docs/development/PORTAL-APP.md`

````diff
@@ -2,11 +2,12 @@
 
 `TechStrap.Portal` is the public site customers use: a product's own help and support pages, with the product's name, logo and accent. It never touches the database: every page asks the TechStrap API,
 anonymously. This page is for people who run it and people who extend it. How agents work the tickets customers send is in [ADMIN-APP.md](ADMIN-APP.md); the decisions behind the Portal are D-045 in the
-[decision log](../architecture/04-DECISION-LOG.md) (with its 2026-10-06 addendum for 09b) and the spec is [PHASE-09](../architecture/PHASE-09-public-portal.md).
+[decision log](../architecture/04-DECISION-LOG.md) (with its 2026-10-06 addenda for 09b and 09c) and the spec is [PHASE-09](../architecture/PHASE-09-public-portal.md).
 
-PHASE-09 is delivered in three pull requests. **09a** is the foundation: the settings, the API client, the per-product theme and shell, the product home, the root page, the ticket-page headers, robots.txt, log
-redaction and the architecture rules. **09b** (this page describes both) adds the customer flows: the contact form with its suggestions and received page, the ticket page with replies and the Closed follow-up,
-the attachment pass-through and the lost-link page. **09c** adds the knowledge base pages, the sitemap and the polish pass. The routes of all three are in `PortalRoutes`.
+PHASE-09 is delivered in four pull requests. **09a** is the foundation: the settings, the API client, the per-product theme and shell, the product home, the root page, the ticket-page headers, robots.txt, log
+redaction and the architecture rules. **09b** adds the customer flows: the contact form with its suggestions and received page, the ticket page with replies and the Closed follow-up, the attachment pass-through
+and the lost-link page. **09c** (this page describes all three) adds the help center: the knowledge base home, category, search and article pages, the SEO head and structured data, output caching and the sitemap.
+**09d** is the polish pass (styling, accessibility, the no-JS check, the double-send guard and the copy button, counter and sending state). The routes of all of them are in `PortalRoutes`.
 
 ## Run it locally
 
@@ -49,7 +50,7 @@ Every page is static server-side rendering: there is no render mode, no circuit
 - **When the API fails** the page shows "This page could not be loaded." with a Try again link and a 503 (429 when the API is rate limiting), never a stack trace.
 - **Branding is untrusted.** `ProductThemeViewModel` keeps the accent only if `ProductAccent.TryDerive` accepts it and the logo only if it is https (or http to `localhost` or `127.0.0.1` in Development, the same
   rule as the CSP's `img-src`). The product name is always encoded.
-- **Copy** lives in `ShellCopy`, `ProblemCopy`, `FormCopy`, `ContactCopy`, `LostLinkCopy` and `TicketCopy`; routes in `PortalRoutes` (a page declares `@attribute [Route(PortalRoutes.XTemplate)]` and builds every link with a builder).
+- **Copy** lives in `ShellCopy`, `ProblemCopy`, `FormCopy`, `ContactCopy`, `LostLinkCopy`, `TicketCopy` and `KbCopy`; routes in `PortalRoutes` (a page declares `@attribute [Route(PortalRoutes.XTemplate)]` and builds every link with a builder).
 
 ### Forms and uploads
 
@@ -86,6 +87,53 @@ if the file ever contains `innerHTML`. `GET /p/{key}/suggest?q=` (`SuggestEndpoi
 characters, returns at most 5 `{title, snippet, href}` items with the links built by `PortalRoutes.KbArticle`, passes the API's 429 through and answers an empty list for a blank text or any other failure.
 The module is tested with `node --test` (`tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs`, run by `scripts/tests/PortalScripts.Tests.ps1`, skipped without node).
 
+### The help center
+
+Four pages, each on `ProductPageBase`, each static SSR with plain links and a GET form, so everything works without script:
+
+- `/p/{key}/kb` (`KbHome`) lists the categories the product can see with their article counts and descriptions (`IPublicKbClient.ListCategoriesAsync`); a product with no article shows the empty state.
+- `/p/{key}/kb/{category}` (`KbCategory`) is a page of the category's published articles, newest update first, 10 a page (`ListCategoryArticlesAsync`). The pager (`Pager`) is `?page=n` links: page one has no query.
+- `/p/{key}/kb/search?q=&page=` (`KbSearch`) is a GET form (`KbSearchBox`). An empty text shows a prompt and makes no call; no result shows a link to contact support; a snippet is plain text; a text is cut at 200
+  characters; a page with a text is `noindex`; paging links keep the escaped text.
+- `/p/{key}/kb/{category}/{slug}` (`KbArticle`) shows the article: breadcrumbs, the title, the day it changed, the body and a "Still need help?" link. **`KbArticleBody` is the second and last place the Portal turns
+  text into markup** (`PortalRules.MarkupStringSites` lists exactly it and `CustomerMessageBody`): the API renders the Markdown and sanitizes the HTML (D-044), and the Portal passes it on byte for byte
+  (`KbArticleHostTests` compares it with the API's string). A plain-http image in an article is blocked by the CSP's `img-src 'self' https: data:` in Production; that is the intended posture.
+
+Everything an agent wrote or a visitor typed (a name, a title, a summary, a snippet, a search text) is plain text shown by Razor, which encodes it. `/p/{key}/kb/search/{x}` matches the article route with the category
+`search`, which the API reserves, so it is a 404.
+
+**One answer for "not there".** An unknown or inactive product, a category that is unknown, another product's or empty, a page past the end, and an article that is a draft, archived, another product's, in the wrong
+category or unknown are all the neutral 404, byte for byte the page an unknown route gets, with none of the product's theme (`ProductPageBase.Fail` forgets the product first). A key or slug that is not a slug is
+refused by `IPublicKbClient` without a call (`KbSlugShape`, like `ProductKeyShape`). When the API fails the page shows the calm message with a 503 (429 when rate limiting), in the product's theme.
+**The `page` and `q` values are read as text** (`KbPaging.Parse`, `KbSearchText.Clean`): the framework's binding to a number answers 500 for `?page=abc`.
+
+### SEO and structured data
+
+Each page sets its head with `SeoHead` (`SyntaxCircus.Blazor.Seo`): a unique title ("{page} - {product} Help Center", the product being the site because `Seo:SiteName` is global), a description, the canonical
+address (built from `TECHSTRAP_PORTAL_PUBLIC_URL`, never the Host header; a page of a category names its own page, and a shared article is canonical under the product the visitor is on), Open Graph (the product's logo
+when it has an acceptable one, else `/icon-512.png`, never the bare site address) and `NoIndex` for a search with a text. The description of an article is its summary, else the first sentence of the body as plain
+text (`KbPlainText`), else the title and product. The article page also writes a `BreadcrumbList` and an `Article` as JSON-LD.
+
+**JSON-LD must go through `JsonLdText`.** `SyntaxCircus.Blazor.Seo` 0.1.4's `JsonLd` writes its JSON through a markup string with an encoder that leaves `<`, `>` and `&` alone, so a `</script>` in an article title
+ends the block and injects markup. Escaping first would be escaped twice, so every string of the Portal's own structured-data records (`BreadcrumbListLd`, `ArticleSchema`) is a `JsonLdText`, whose converter writes
+`<`, `>`, `&`, the apostrophe, `+` and every non-ASCII character as `\uXXXX`; the JSON reads back as the original. A Razor file cannot hold the text `</script` in a string (the Razor parser reads a tag), so the
+hostile texts live in C# tests. The block is data, not script, so the CSP does not change. Do not use the package's schema records for text a visitor or an agent wrote.
+
+### Caching and the sitemap
+
+The help-centre home, a category page and an article page are kept for 60 seconds by the framework's output cache (`AddPortalOutputCache`, one base policy with the path predicate `PortalCachePaths.IsCacheable`; no
+attribute on a page). The key varies by the `page` query value only and never by host (the framework's default key holds the whole query string and the host, so `?utm=1`, `?utm=2` ... would fill the store); a `page`
+value that is not one to four digits is answered but never kept. The search page, the form pages, `/p/{key}` itself, `/t/*`, the suggest adapter, `/not-found`, the sitemap and every answer that is not a 200 are never kept,
+and the output cache never stores a response that sets a cookie (no help-center page does). A delivered KB page tells browsers `Cache-Control: public, max-age=60` (`PathHeaderRule.SetOnSuccess` in Hosting: a 404,
+429 or 503 never gets it); the search page is `no-store`. `UsePortalOutputCache` goes after the error pages and before the endpoints (`ProgramOrderTests` pins the order), so the shared security headers and the per-path
+rules are applied to a cached answer too, and it sets the request's own `X-Correlation-Id` again when the response starts, because a stored copy replays the first request's.
+
+`/sitemap.xml` is `MapSeoSitemap` with a provider (`PortalSitemap`). A build (`PortalSitemapBuilder`) asks for the active products (`IPublicProductClient.ListAsync`), then for each product's published articles, and
+lists the root page (only when no default product is configured: `/` is then a redirect), each product's home, its help center home, its categories and its articles; a shared article is listed under each product. Every
+address is absolute (from the public URL) and at most 50,000 are listed, the root page included. `PortalSitemapCache` keeps the result for 15 minutes in an `IMemoryCache` with single-flight (twenty concurrent
+requests make one build), builds on its own task with its own cancellation token (a crawler that goes away stops waiting but cannot cancel the build), remembers a failed build for one minute while the last good sitemap is
+served, and fails the request (the 500 page) only when there has never been a good one. The build's calls carry the address of the visitor whose request started it (a stand-in `HttpContext`; known gap below).
+
 ### The ticket page and attachments
 
 `/t/{token}` (`Ticket.razor`) parses the token first (`TicketToken.TryParse`; a malformed one is the uniform 404 and the API is never asked), loads the ticket through `ICustomerTicketClient`, then the ticket's
@@ -115,14 +163,14 @@ asked; a 429 is a calm notice.
 D-019) and have no logging handlers. `ProblemMapping` turns every answer into a `Result`: 400 keeps the API's field codes, 404 is one not-found whatever the API called it, 409 is `reply-conflict`, 413 and 415 are the
 attachment errors, 429 is rate limited, any 5xx or transport error is `api-unavailable`, with fixed sentences from `ProblemCopy`. A call made as a ticket's customer takes a `TicketToken` (43 base64url characters;
 it prints as `[token]`), which becomes the `X-Ticket-Token` header of that request only. The typed clients are `IPublicProductClient`, `IPublicTicketClient` (multipart intake), `ICustomerTicketClient` (view, reply,
-lost link, attachment stream) and `IPublicKbClient` (search only; 09c extends it). `MultipartForm` builds the bodies (text fields first, one `Attachments` part per file, the file streams owned by the request).
+lost link, attachment stream) and `IPublicKbClient` (paged search, categories, a category's articles, the article and the sitemap entries; `IPublicProductClient` also lists the active products for the sitemap). `MultipartForm` builds the bodies (text fields first, one `Attachments` part per file, the file streams owned by the request).
 
 ### Headers, robots.txt and the canonical host
 
 `UseTechStrapWebHost(PortalHeaderRules.Rules)` applies per-path rules after the shared security headers: everything under `/t` gets `Referrer-Policy: no-referrer`, `Cache-Control: no-store` and
 `X-Robots-Tag: noindex`, only `/t/{token}/attachments/{id}` is sandboxed (so the ticket page keeps the normal CSP), and the four form pages of a product (`/p/{key}/contact`, `/contact/received`, `/lost-link` and
-`/suggest`) get `no-store` and `noindex`. `/robots.txt` disallows `/t/` and names `{public url}/sitemap.xml`, which answers 404 until 09c. The canonical-host redirect is an allow-list of legacy hosts and does
-nothing until configured.
+`/suggest`) get `no-store` and `noindex`; a delivered help-center page gets `public, max-age=60` and the search page `no-store` (see Caching). `/robots.txt` disallows `/t/` and names `{public url}/sitemap.xml`. The
+canonical-host redirect is an allow-list of legacy hosts and does nothing until configured.
 
 ### Logs and Sentry
 
@@ -140,18 +188,22 @@ and as `203.0.113.11` (a 200). It never runs `down -v`.
 
 ```text
 src/TechStrap.Portal/
+  Caching/        PortalCachePaths (what is kept), PortalOutputCache (the policy and the pipeline step)
   Clients/        ApiConnection, ProblemMapping and ProblemCopy, TicketToken, ApiClientRegistration (the two named clients), the typed clients (IPublicProductClient, IPublicTicketClient,
                   ICustomerTicketClient, IPublicKbClient), MultipartForm, AttachmentFileName, ApiQuery, ApiDownload
   Components/
+    Kb/           KbArticleBody (the other markup site), KbArticleCard, KbBreadcrumbs and KbCrumb, KbSearchBox, KbPaging, KbSearchText, KbPlainText
     Layout/       PortalLayout, ProductHeader, ProductFooter
-    Pages/        Home (the root), ProductHome, Contact, ContactReceived, Ticket, LostLink, NotFound, Error, StyleGuide (Development only)
-    Tickets/      CustomerMessageBody (the one markup site), MessageThread, TicketStatusBanner
-    Ui/           AccentScope, PoweredByFooter, ProductUnavailable, DevelopmentOnly, ErrorSummary, FieldError, FormField, AttachmentInput, HoneypotField
+    Pages/        Home (the root), ProductHome, KbHome, KbCategory, KbSearch, KbArticle, Contact, ContactReceived, Ticket, LostLink, NotFound, Error, StyleGuide (Development only)
+    Tickets/      CustomerMessageBody (a markup site), MessageThread, TicketStatusBanner
+    Ui/           AccentScope, PoweredByFooter, ProductUnavailable, DevelopmentOnly, ErrorSummary, FieldError, FormField, AttachmentInput, HoneypotField, Pager, StateMessage
+    KbCopy.cs     the words of the help center (beside ShellCopy)
   Forms/          FormError and FormFields, FormCopy, FormFailure, AttachmentRules, EmailRules, ContactFormViewModel and its validator, ReplyForm, LostLinkForm, ContactCopy, ReceivedReference
-  Headers/        PortalHeaderRules (the /t rules, the attachment sandbox and the form pages)
+  Headers/        PortalHeaderRules (the /t rules, the attachment sandbox, the form pages and the help center)
   Products/       ProductThemeViewModel, ProductScope, ProductPageBase
-  Routing/        PortalRoutes, ProductKeyShape
-  Seo/            PortalSeoRegistration (Blazor.Seo: base URL, robots.txt, canonical host)
+  Routing/        PortalRoutes, ProductKeyShape, KbSlugShape
+  Seo/            PortalSeoRegistration (Blazor.Seo: base URL, robots.txt, the sitemap, canonical host), PortalSitemap, PortalSitemapBuilder, PortalSitemapCache, JsonLdText,
+                  KbStructuredData (the breadcrumb and article records), KbSeo
   Settings/       PortalOptions and its validator
   Suggestions/    SuggestEndpoint (GET /p/{key}/suggest)
   Tickets/        CustomerTicketPresenter and its view models, TicketCopy, FollowUpLink, AttachmentPassThrough
@@ -161,20 +213,26 @@ src/TechStrap.Portal/
 ```
 
 Rules the code follows (the architecture tests check them): the Portal references **Contracts and Hosting only**; **components never inject `HttpClient`** (only `Clients/` mentions it); no inline script
-or style; no interactive render mode; `MarkupString` is used in one file, `Components/Tickets/CustomerMessageBody.razor` (09c adds `KbArticleBody`, argued for in its commit); every plain-text DTO field is encoded.
+or style; no interactive render mode; `MarkupString` is used in exactly two files, `Components/Tickets/CustomerMessageBody.razor` and `Components/Kb/KbArticleBody.razor` (a third is a design decision, argued in
+its own commit); every plain-text DTO field is encoded and every JSON-LD string is a `JsonLdText`.
 
 ## Tests
 
 `tests/TechStrap.Portal.Tests` runs the host in memory with a stub API behind its two named clients (`PortalFactory.Api`, `StubApiHandler`), so a test sees what the API would see, including the headers the
 Portal added. `ProxyHopStartupFilter` puts the host behind a trusted reverse proxy and every API-calling host test asserts the visitor's address with `AssertEveryCallBore`; `OkProbeStartupFilter` answers 200 on a
-path no Portal route matches. A test that needs the real server (a request size limit) calls `UseKestrel(0)` and `StartServer()` and reads the address from `IServerAddressesFeature`. bUnit covers the layout.
+path no Portal route matches. A test that needs the real server (a request size limit) calls `UseKestrel(0)` and `StartServer()` and reads the address from `IServerAddressesFeature`. bUnit covers the layout and
+the shared help-centre components; the help-centre host tests (`Kb/`) parse the page with AngleSharp (which bUnit brings) and assert on elements, the JSON-LD is parsed as JSON, and `OutputCachePipelineTests` builds a
+small host with the real wiring to prove what the cache keeps. The cache and sitemap-cache tests that wait use short real lifetimes, because a `MemoryCache` has no `TimeProvider`.
 `tests/TechStrap.Portal.Tests/js` holds the node tests of the browser module. The shared rules are in `TechStrap.Architecture.Tests` (`PortalRules`), the Hosting header-rule mechanism in `TechStrap.Api.Tests`
 (`PathHeaderRuleHostTests`), and the log and Sentry redaction in `TechStrap.Api.Tests`.
 
 ## Known gaps
 
-- `/robots.txt` names `/sitemap.xml`, which answers 404 until 09c maps it. The search box on the product home and the KB links answer 404 until 09c.
-- There is no "copy" button for the ticket number, no live character counter and no "sending" state on the submit button: they need script, and 09b keeps every flow script-free (09c's polish pass decides).
+- The sitemap build's API calls carry the address of the visitor whose request started it, so the API rate-limits that address (the Public limit, 120 a minute) for the 1 + N calls of a build; a Portal with more than
+  about 100 products would fail its build and serve the last good sitemap or the 500 page. A service address for the build is the follow-up.
+- The help centre has no category description on its category page and the product home has no category list; the visual polish, the accessibility and no-JS pass and the double-send guard are PHASE-09d.
+- There is no "copy" button for the ticket number, no live character counter and no "sending" state on the submit button: they need script, and the Portal keeps every flow script-free (09d decides).
+- The package's `JsonLd` component is unsafe for any text an author wrote (see SEO); the Portal does not use it for strings, and the problem is to be reported to `SyntaxCircus.Blazor.Seo`.
 - A chunked post over the size limit is the framework's 400 about an antiforgery token, not a 413 (a browser form post always declares its length).
 - A post to an unknown product is the framework's plain-text 400 ("Cannot submit the form 'contact' because no form on the page currently has that name."), not the 404 page a GET gets: the product page ends in `NotFound()` before the form is rendered, so there is no form to post to. Nothing is created. A post to a malformed or unknown ticket token is the same for the form `reply`, with an identical body, so nothing tells the two apart.
 - A legacy-host redirect decodes percent-escapes in the query string (the package builds the target with `Uri.ToString()`), so a value that holds an encoded `&` or `#` changes meaning. It affects only hosts
@@ -184,4 +242,4 @@ path no Portal route matches. A test that needs the real server (a request size
 - The Portal does not use `GlobalErrorBoundary`: its Try again button needs interactivity, and catching a render error in the page would answer 200 with a branded page instead of the plain 500 error page.
 - Sentry has no general email rule: it masks the `name`, `email`, `subject` and `ref` query values and the `/t/{token}` path only (Serilog's email pattern does catch addresses in logs).
 - If OpenTelemetry tracing were enabled, server spans would carry `url.path=/t/<token>`. It is off by default; masking it is a follow-up.
-- A double click on "Send message" can send twice before the redirect arrives (there is no script to disable the button); the API creates a ticket for each.
+- A double click on "Send message" can send twice before the redirect arrives (there is no script to disable the button); the API creates a ticket for each (09d adds a guard).
````


- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test --project tests/TechStrap.Portal.Tests -c Release --filter-query "/*/*/SitemapHostTests/*"
dotnet test --project tests/TechStrap.Portal.Tests -c Release --filter-query "/*/TechStrap.Portal.Tests.Seo/*/*"
pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal -Path scripts/tests/RepositoryDocs.Tests.ps1
```

Expected: `total: 10 succeeded: 10`; `total: 98 succeeded: 98`; `Tests Passed: 38, Failed: 0`.

- [ ] **Step 5: Prove each pin with a recorded mutation**

Save `$T\specs\m5.py`:

```python
def PT(namespace):
    return ["dotnet", "test", "--project", "tests/TechStrap.Portal.Tests", "-c", "Release", "--filter-query", f"/*/TechStrap.Portal.Tests.{namespace}/*/*"]


SEO = PT("Seo")
PESTER = ["pwsh", "-NoProfile", "-File", "scripts/Invoke-ScriptTests.ps1", "-Output", "Minimal", "-Path", "scripts/tests/RepositoryDocs.Tests.ps1"]
SPEC = "docs/architecture/PHASE-09-public-portal.md"
GUIDE = "docs/development/PORTAL-APP.md"
LOG = "docs/architecture/04-DECISION-LOG.md"

MUTATIONS = [
    ("1 sitemap: not mapped", "src/TechStrap.Portal/Seo/PortalSeoRegistration.cs", [("        app.MapSeoSitemap(PortalSitemap.StaticEntries(app.Services.GetRequiredService<IOptions<PortalOptions>>().Value), PortalSitemap.ProviderAsync, PortalSitemap.ClientCacheDuration);\n", "")], SEO),
    ("2 sitemap: the static entries are not counted", "src/TechStrap.Portal/Seo/PortalSitemap.cs", [("BuildAsync(reserved, buildToken)", "BuildAsync(0, buildToken)")], SEO),
    ("3 builder: the room ignores the static entries", "src/TechStrap.Portal/Seo/PortalSitemapBuilder.cs", [("var room = Math.Max(0, MaxUrls - reservedForStatic);", "var room = MaxUrls;")], SEO),
    ("4 sitemap: the client cache is not sent", "src/TechStrap.Portal/Seo/PortalSeoRegistration.cs", [("PortalSitemap.ProviderAsync, PortalSitemap.ClientCacheDuration);", "PortalSitemap.ProviderAsync);")], SEO),
    ("5 sitemap: the failure lifetime is a millisecond", "src/TechStrap.Portal/Seo/PortalSitemapCache.cs", [("DefaultFailureTtl = TimeSpan.FromMinutes(1)", "DefaultFailureTtl = TimeSpan.FromMilliseconds(1)")], SEO),
    ("6 spec: T12 is not ticked", SPEC, [("- [x] **P09-T12**", "- [ ] **P09-T12**")], PESTER),
    ("7 spec: the corrected handoff is not named", SPEC, [("read through `IPublicKbClient.GetSitemapAsync` and `IPublicProductClient.ListAsync` by `PortalSitemapBuilder`;", "read through the typed clients;")], PESTER),
    ("8 roadmap: 09c is not started", "docs/architecture/99-IMPLEMENTATION-ROADMAP.md", [("09c complete (pending merge); 09d not started", "09c not started")], PESTER),
    ("9 guide: no caching section", GUIDE, [("### Caching and the sitemap", "### Caching")], PESTER),
    ("10 log: no sitemap known gap", LOG, [("**Known in 09c: the sitemap build's calls carry the first crawler's address.**", "**Known in 09c: the build.**")], PESTER),
    ("11 architecture: the old name is back", "docs/architecture/02-ARCHITECTURE.md", [("(`GetKbSitemapRequestHandler` and `ListPublicProductsRequestHandler` at the API)", "(`GetSitemapEntriesRequestHandler` at the API)")], PESTER),
    ("12 spec: an old name without its disclaimer", SPEC, [("| `GET /robots.txt` | Exempt | `Blazor.Seo` | `MapSeoRobotsTxt` | Static text |", "| `GET /robots.txt` | Exempt | `ISitemapEntryProvider` | `MapSeoRobotsTxt` | Static text |")], PESTER),
    ("13 guide: one markup site again", GUIDE, [("`MarkupString` is used in exactly two files,", "`MarkupString` is used in one file,")], PESTER),
    ("14 spec: T09 is deferred to 09c", SPEC, [("deferred to 09d, D-045 'Known in 09b'", "deferred to 09c, D-045 'Known in 09b'")], PESTER),
]
```

Run them in foreground batches and record the results (the first five run the Portal tests, the rest the Pester block):

| # | The mutation | File | Result |
| --- | --- | --- | --- |
| 1 | sitemap: not mapped | `PortalSeoRegistration.cs` | KILLED (12 failing) |
| 2 | sitemap: the static entries are not counted | `PortalSitemap.cs` | KILLED (1 failing) |
| 3 | builder: the room ignores the static entries | `PortalSitemapBuilder.cs` | KILLED (2 failing) |
| 4 | sitemap: the client cache is not sent | `PortalSeoRegistration.cs` | KILLED (1 failing) |
| 5 | sitemap: the failure lifetime is a millisecond | `PortalSitemapCache.cs` | KILLED (2 failing) |
| 6 | spec: T12 is not ticked | `PHASE-09-public-portal.md` | KILLED |
| 7 | spec: the corrected handoff is not named | `PHASE-09-public-portal.md` | KILLED |
| 8 | roadmap: 09c is not started | `99-IMPLEMENTATION-ROADMAP.md` | KILLED |
| 9 | guide: no caching section | `PORTAL-APP.md` | KILLED |
| 10 | log: no sitemap known gap | `04-DECISION-LOG.md` | KILLED |
| 11 | architecture: the old name is back | `02-ARCHITECTURE.md` | KILLED |
| 12 | spec: an old name without its disclaimer | `PHASE-09-public-portal.md` | KILLED |
| 13 | guide: one markup site again | `PORTAL-APP.md` | KILLED |
| 14 | spec: T09 is deferred to 09c | `PHASE-09-public-portal.md` | KILLED |

Every mutation is killed. Row 7 first SURVIVED: the pin asked for `PortalSitemapBuilder` anywhere in the spec, and the handoff table says it too. The pin now reads the corrections block itself (`IPublicKbClient.GetSitemapAsync`, `GetKbSitemapRequestHandler`, `PortalSitemapBuilder`) and the table row separately, and the row is killed.

- [ ] **Step 6: Verify the whole branch**

```bash
dotnet build TechStrap.slnx -c Release --no-incremental
dotnet test --solution TechStrap.CI.slnf -c Release
pwsh -File scripts/Invoke-ScriptTests.ps1
dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api --configuration Release --no-build
```

Expected (recorded in the scratch copy after the five commits): `0 Warning(s)`, `0 Error(s)`; `Test run summary: Passed! total: 6501 failed: 0 succeeded: 6501` (the Portal tests alone are 1434); `Tests Passed: 308, Failed: 0`; `No changes have been made to the model since the last migration.`

- [ ] **Step 7: Commit**

```bash
git add -A
git diff --cached --stat
git commit -m "feat: PHASE-09c sitemap host tests, PORTAL-APP rewrite, PHASE-09 ticks and D-045 as-built notes (D-045)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

**Owner manual checks (not run by the plan; the PR description lists them):**
- Under compose, browse the help center of a product with articles (`/p/<key>/kb`, a category, an article with an image), search it, and page through a category with more than ten articles.
- View the source of an article for the canonical address, the Open Graph tags and the two JSON-LD blocks; confirm the page loads with no console error under the CSP.
- Load `/sitemap.xml` and `/robots.txt` and confirm the sitemap lists the products' pages with the public address and robots.txt names it.
- Confirm a draft or archived article, an unknown category and `/p/<key>/kb/search/anything` all show the same plain "Page not found" page.
