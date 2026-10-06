# PHASE-09a Portal Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `TechStrap.Portal` the foundation PHASE-09b and PHASE-09c build on.
- **Settings and routes:** the validated settings, every route as a constant.
- **API access:** the API client, with the real visitor address and a safe path for a ticket token.
- **Pages and theme:** the per-product theme and shell, the themed product home and the root page.
- **Safety:** the ticket-page headers, `robots.txt`, log redaction and the architecture rules.

**Architecture:**
- **Static server rendering only.** There is no render mode and no circuit. Every page asks the TechStrap API anonymously through `ApiConnection`: reads are retried, writes never are, and both forward the visitor's address.
- **Product scope.** A page under `/p/{key}` loads its product into a per-request `ProductScope`, and `PortalLayout` wraps the page in that product's accent, header and footer. The not-found and error pages are rendered in a fresh scope, so they are never branded; an unknown, inactive or malformed key is answered exactly like an unknown route.
- **Per-path headers.** `UseTechStrapWebHost` (Hosting) gains per-path header rules that run after the shared security headers, so the ticket-page headers and the attachment sandbox are applied to the final response.
- **Shared Hosting redaction.** The PII redactor and the Sentry processors learn the Portal's two new private values: the `name` and `email` query values and the `/t/{token}` path.

**Tech Stack:** .NET 10, ASP.NET Core Blazor static SSR, `SyntaxCircus.Blazor.Seo` 0.1.4, `SyntaxCircus.Common` (`Result`), `SyntaxCircus.Http.Resilience` (Polly retry), Serilog, Sentry, bUnit, xUnit v3, Shouldly, NSubstitute and Pester.

**Spec:** `docs/architecture/PHASE-09-public-portal.md` and `docs/architecture/UX-BRIEF-portal.md` (the shell, the product home, NotFound and the root page), `docs/architecture/04-DECISION-LOG.md` (D-002, D-017, D-019, D-024, D-038, D-040, D-042, D-043, D-044), and the owner decisions of 2026-10-05, recorded as D-045 in Task 1. This plan covers **09a only**: tasks T01, T03, T05, T17, T19 and T22 in full, and the parts of T02 and T04 that do not need a ticket page, a KB page or the 09c products endpoint. 09b (the customer flows) and 09c (the knowledge base, SEO and polish) get their own plans.

### Owner decisions (2026-10-05), recorded as D-045
- **Delivery:** three pull requests, each with its own branch, plan and review: 09a the foundation, 09b the customer flows, 09c the knowledge base, SEO and polish.
- **KB suggestions:** vanilla JS (a `<ts-kb-suggestions>` custom element and a Portal-hosted `GET /p/{key}/kb/suggest` adapter), not an InteractiveServer island. Nothing of it is in 09a.
- **Small public API additions (09c):** a paged list of a category's articles, and `GET api/public/products`.
- **Root page:** `/` redirects to `TECHSTRAP_PORTAL_DEFAULT_PRODUCT` when it is set; otherwise a neutral page with no product list.
- **Ticket theming (09b):** `CustomerTicketDto` gains `ProductKey`.
- **T20 (Playwright) is deferred.**

### Technical decisions this plan makes (D-045; the owner confirms at plan review)
- **API client.** The Portal has its own `ApiConnection` and `ProblemMapping`, modelled on the Admin's, with no code shared (Hosting stays a leaf; the Portal references exactly Contracts and Hosting).
  - A read client retries transport errors, 408 and 502 to 504 twice, honouring `Retry-After` up to 2 seconds, with no circuit breaker. A write client never retries.
  - `ProblemMapping` maps 400 to field errors, 404 to one not-found whatever the API called it, 413 and 415 to the attachment errors, 429 to rate limited, and any 5xx or transport failure to `api-unavailable`, each with a fixed sentence.
  - A customer call takes a `TicketToken` (43 base64url characters, prints as `[token]`) and sets `X-Ticket-Token` on that request only.
  - The ticket and KB clients arrive with their pages (YAGNI): 09a ships `IPublicProductClient` and the token capability.
- **Blazor.Seo, with its real API.** `AddSyntaxCircusSeo` and `UseSyntaxCircusSeo`; `MapSeoRobotsTxt(extraDirectives: ["Disallow: /t/"])`. `Seo:BaseUrl` is derived from `TECHSTRAP_PORTAL_PUBLIC_URL` in code (one key for one value). The `CanonicalHost__*` keys are optional. The sitemap is not mapped in 09a.
- **Per-path headers.** `PathHeaderRule.Set` and `PathHeaderRule.Sandbox`, and `UseTechStrapWebHost(rules, downloadPathPrefixes)`. `/t/*` gets `Referrer-Policy: no-referrer`, `Cache-Control: no-store` and `X-Robots-Tag: noindex`; only `/t/{token}/attachments/{id}` is sandboxed. The Admin's download prefix becomes one such rule with unchanged behaviour.
- **Theming.** No `BrandingThemeFactory`: a thin `ProductThemeViewModel` keeps the accent only if `ProductAccent.TryDerive` accepts it and the logo only if it is https (or loopback http in Development).
- **No-enumeration.** An unknown, inactive or malformed product key calls `NavigationManager.NotFound()` and renders the same neutral 404 page as an unknown route; a malformed key is never sent to the API.
- **Redaction.** The shared PII redactor masks the value of a `name` or `email` query parameter; the Sentry processors mask the same and a 43-character token directly under `/t/`.

### Decisions made while drafting (D-045 records them)
- **Task split.** The five tasks of the brief, unchanged.
- **Namespaces.** Settings live in `TechStrap.Portal.Settings`, not `TechStrap.Portal.Options`: a namespace called `Options` makes `Options.Create(...)` in every Portal test resolve to the namespace.
- **One public-address key.** `Seo:BaseUrl` is a `PostConfigure` over `PortalOptions.PublicBaseUrl`, so a stray `Seo__BaseUrl` can never disagree with it. `CanonicalHost__CANONICALHOST` blank means no redirect, because the package's redirect is an allow-list of legacy hosts; this is why the scope plan's "required in Production" became "optional" for the canonical host (the public URL is the required one).
- **No `Api__TimeoutSeconds` key.** The read and write deadlines are constants (30 s and 300 s); a key for them is added when a deployment needs one.
- **The Portal references `SyntaxCircus.Http.Resilience`** (already pinned; the Admin uses it) and its tests `Microsoft.Extensions.TimeProvider.Testing` (already pinned). No new package.
- **Product scope as a scoped service.** `ProductPageBase` puts the loaded product in `ProductScope` and the layout subscribes to its change event. The layout cannot see the route's `{key}`, and a scoped service gives the neutral pages a fresh, empty scope for free.
- **A product's header is its logo and name as one link to the home page**; the footer carries "Lost your ticket link?". The UX brief's header links to the help articles and the contact page wait for those pages (09b and 09c).
- **`GlobalErrorBoundary` is not used.** Its retry button needs interactivity, and catching a render error inside the page would answer 200 with a branded page instead of the plain 500 error page.
- **The not-found bodies are byte-identical, the headers are not.** `NavigationManager.NotFound()` adds the framework's `blazor-enhanced-nav: allow` header, which the router's own unknown-route 404 does not carry. The tests pin identical bodies against an unknown route and identical bodies, statuses and headers among unknown, inactive and malformed keys; this reveals nothing, because an active product answers 200 anyway.
- **The leak test cannot rely on the redactor.** A 43-character token is exactly what the Serilog redactor masks, so `TicketTokenLeakTests` also pins that the Portal's clients write no log event of their own and the chain has no logging handler, and its negative control uses a 47-character secret the redactor leaves alone.
- **Real-IP tests drive the real pipeline.** `ProxyHopStartupFilter` makes the connection peer a trusted proxy and a probe after the pipeline calls the product client; `OkProbeStartupFilter` answers 200 on a path no Portal route matches, so the `/t` header rules are tested on a real 200 response before 09b has a ticket page.
- **T22 is test-only.** The footer, the option and its validation exist since PHASE-02; `PoweredByHostTests` pins them on every page type, so its first run passes and its evidence is the mutations.
- **A real finding.** The Development-only style guide had an inline `onsubmit` handler, which the new inline-markup rule flags and the CSP would block; Task 5 removes it.
- **Known gaps, recorded in D-045 and `PORTAL-APP.md`:** the product home links to the contact, lost-link and search pages that answer 404 until 09b and 09c; `/robots.txt` names a sitemap that answers 404 until 09c; the package's legacy-host redirect decodes percent-escapes in the query string (affects only listed legacy hosts); a category named `suggest` would be unreachable once 09b serves `/p/{key}/kb/suggest`.
- **Shared files between tasks** (the tasks run in order, so the overlaps are safe): `Program.cs` (Tasks 1 to 4), `PortalFactory.cs` (Tasks 1 and 2), `appsettings.json`, `.env.example`, `deploy/.env.portal.example`, `ConfigContract.Tests.ps1` and `EnvExampleCompletenessTests.cs` (Tasks 1 and 3), `RepositoryDocs.Tests.ps1` and the decision log (Tasks 1 and 5), the PHASE-09 spec, roadmap and discovery rows (Tasks 1 and 5).

## Global Constraints

- **Build.** .NET SDK 10.0.401 targeting `net10.0`, with `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild`. Private fields are `_camelCase`, constants are PascalCase, and namespaces are file-scoped.
- **Packages.** No new package other than the already-pinned `SyntaxCircus.Blazor.Seo` and `SyntaxCircus.Common` (Task 1), `SyntaxCircus.Http.Resilience` (Task 2) and, for the tests, `Microsoft.Extensions.TimeProvider.Testing` (Task 2). Versions are managed centrally in `Directory.Packages.props`.
- **Project references.** The Portal references exactly Contracts and Hosting. Hosting stays a leaf: it may reference only packages, never another TechStrap project.
- **Config (D-043).** Every new key gets four edits: the host's `appsettings.json` (a real default; required strings stay blank), `src/TechStrap.Portal/.env.example`, `deploy/.env.portal.example`, and compose where compose owns it. `scripts/tests/ConfigContract.Tests.ps1` must pass.
- **Portal conventions.**
  - Static SSR only, with no interactive render mode.
  - Components never inject `HttpClient`; only `Clients/` mentions it.
  - Copy lives in `*Copy` constants.
  - No inline script or style.
  - All plain-text DTO fields are encoded.
  - No `MarkupString` in 09a.
  - Every route is a `PortalRoutes` constant or builder.
- **Encoding.** Write non-ASCII as `\u` escapes, using Python or .NET, never GNU sed. The Write tool decodes `\uXXXX`, so grep afterwards. `SourceEncodingTests` and `SourceEscapeTests` must pass. Everything in this plan is ASCII.
- **Secrets and PII.** Never log tokens, emails, names or query text.
- **Tests.**
  - Failing test first, with RED and GREEN recorded.
  - Prove each pin with a recorded mutation.
  - Leak tests run at Verbose.
  - Tests that set environment variables go in `ProcessEnvironmentCollection` (09a adds none).
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
  - `pwsh -File scripts/Invoke-ScriptTests.ps1`: passes.
- **Branch.** Work on `feat/phase-09a-portal-foundation` (cut from main at 98b3e71). Never edit or commit on `main`.

## Review Focus

1. **Token leakage.** The `X-Ticket-Token` capability, `/t/` paths in logs and Sentry, and the contact prefill's `name` and `email` values in logs.
   - A token is a header on one request and nowhere else; it never prints.
   - Pinned in Task 2 (`ApiConnectionTests`, `TicketTokenTests`, `TicketTokenLeakTests`) and Task 5 (`RequestLogRedactionHostTests`, `PiiRedactionQueryValueTests`, `SensitiveQuerySentryProcessorTests`).
2. **Wrong headers on `/t/*`.** The shared middleware overwriting the overrides, or the sandbox reaching the ticket page. Pinned in Task 3 (`PathHeaderRuleHostTests`, `TicketHeaderHostTests`, `PortalHeaderRulesTests`), asserting the final response, including the re-executed 404.
3. **Product enumeration.** Unknown, inactive and malformed keys must be indistinguishable from each other and answer the page an unknown route gets, and NotFound and Error must stay unbranded. Pinned in Task 4 (`NeutralPagesGuardTests`, `ProductHomeHostTests`).
4. **Real client IP.** It must reach the API through the Portal on every call, and a write must never be retried. Pinned in Task 2 (`ApiConnectionTests`, `ForwardedClientIpHostTests`) and Task 4 (`ProductHomeHostTests`, through a real page).
5. **Untrusted branding.** The logo address scheme, and colour values used only when derived. Pinned in Task 4 (`ProductThemeViewModelTests`, `ProductHomeHostTests`).

---
### Task 1: Foundations: D-045 and the spec corrections, the Portal's validated settings, route constants, the D-043 keys and compose, and the test host's settings

**Review Focus pin:** none of the five alone. This task makes the Portal fail fast on a missing or malformed API address or public URL (so no later page runs half configured), puts every route in one place (`RouteLiteralTests` keeps later pages honest) and records D-045.

**Files:**

- Create: `src/TechStrap.Portal/Routing/PortalRoutes.cs`
- Create: `src/TechStrap.Portal/Routing/ProductKeyShape.cs`
- Create: `src/TechStrap.Portal/Settings/PortalOptions.cs`
- Create: `src/TechStrap.Portal/Settings/PortalOptionsRegistration.cs`
- Create: `src/TechStrap.Portal/Settings/PortalOptionsValidator.cs`
- Modify: `deploy/.env.portal.example`
- Modify: `deploy/docker-compose.yml`
- Modify: `docker-compose.yml`
- Modify: `docs/architecture/00-DISCOVERY-INDEX.md`
- Modify: `docs/architecture/03-PACKAGE-MAP.md`
- Modify: `docs/architecture/04-DECISION-LOG.md`
- Modify: `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`
- Modify: `docs/architecture/PHASE-09-public-portal.md`
- Modify: `src/TechStrap.Portal/.env.example`
- Modify: `src/TechStrap.Portal/Components/Pages/Error.razor`
- Modify: `src/TechStrap.Portal/Components/Pages/Home.razor`
- Modify: `src/TechStrap.Portal/Components/Pages/NotFound.razor`
- Modify: `src/TechStrap.Portal/Components/Pages/StyleGuide.razor`
- Modify: `src/TechStrap.Portal/Components/_Imports.razor`
- Modify: `src/TechStrap.Portal/Program.cs`
- Modify: `src/TechStrap.Portal/TechStrap.Portal.csproj`
- Modify: `src/TechStrap.Portal/appsettings.json`
- Test (create): `tests/TechStrap.Portal.Tests/Routing/PortalRoutesTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Routing/ProductKeyShapeTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Routing/RouteLiteralTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Settings/PortalOptionsHostTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Settings/PortalOptionsValidatorTests.cs`
- Test (modify): `scripts/tests/ConfigContract.Tests.ps1`
- Test (modify): `scripts/tests/RepositoryDocs.Tests.ps1`
- Test (modify): `tests/TechStrap.Api.Tests/Config/DevelopmentStartupTests.cs`
- Test (modify): `tests/TechStrap.Api.Tests/Config/ProductionBlankTemplateTests.cs`
- Test (modify): `tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs`
- Test (modify): `tests/TechStrap.Api.Tests/HostFactory.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/PortalFactory.cs`


**Interfaces:**
- Consumes: `Microsoft.Extensions.Options` (`IValidateOptions<T>`), `IHostEnvironment`, the existing `PoweredByOptions` pattern in `Program.cs`, `ConfigFiles` and `ConfigHosts` (Api.Tests), `TechStrap.Tests.Shared.RepositoryRoot`.
- Produces:
  - `TechStrap.Portal.Settings.PortalOptions` (public sealed class): constants `ApiBaseUrlKey = "Api:BaseUrl"`, `PublicUrlKey = "TECHSTRAP_PORTAL_PUBLIC_URL"`, `DefaultProductKey = "TECHSTRAP_PORTAL_DEFAULT_PRODUCT"`; settable `string ApiBaseUrl`, `string PublicUrl`, `string? DefaultProduct`; computed `Uri ApiBaseUri` (trailing slash added), `string PublicBaseUrl` (trailing slash removed, `""` when blank), `string? DefaultProductKeyOrNull`.
  - `PortalOptionsValidator` (internal, `IValidateOptions<PortalOptions>`, ctor `(IHostEnvironment)`): the API address must be absolute http(s); the public URL is required outside Development and must be an absolute http(s) base (no user info, query or fragment) in every environment; the default product, when given, must be a slug. Every failure is reported at once and names the key (`Api:BaseUrl (API__BASEURL)`, `TECHSTRAP_PORTAL_PUBLIC_URL`, `TECHSTRAP_PORTAL_DEFAULT_PRODUCT`).
  - `PortalOptionsRegistration.AddPortalOptions(this IServiceCollection)`: binds the three keys and calls `ValidateOnStart()`.
  - `TechStrap.Portal.Routing.PortalRoutes` (public static): prefixes `ProductPrefix = "/p"`, `TicketPrefix = "/t"`; the 15 templates `HomeTemplate` `/`, `NotFoundTemplate` `/not-found`, `ErrorTemplate` `/error`, `StyleGuideTemplate` `/_styleguide`, `ProductHomeTemplate` `/p/{key}`, `ContactTemplate`, `ContactReceivedTemplate`, `LostLinkTemplate`, `KbHomeTemplate`, `KbCategoryTemplate`, `KbArticleTemplate`, `KbSearchTemplate`, `KbSuggestTemplate`, `TicketTemplate` `/t/{token}`, `TicketAttachmentTemplate` `/t/{token}/attachments/{id}`; builders `ProductHome(key)`, `Contact(key)`, `ContactReceived(key)`, `LostLink(key)`, `KbHome(key)`, `KbCategory(key, category)`, `KbArticle(key, category, slug)`, `KbSearch(key)`, `KbSuggest(key)`, `Ticket(token)`, `TicketAttachment(token, Guid)`, every value escaped with `Uri.EscapeDataString`.
  - `TechStrap.Portal.Routing.ProductKeyShape` (public static): `MaxLength = 40`, `IsWellFormed(string?)` (`\A[a-z0-9]+(?:-[a-z0-9]+)*\z`).
  - Config keys, each with its four D-043 edits: `Api:BaseUrl` (env `API__BASEURL`; compose owns it in both compose files), `TECHSTRAP_PORTAL_PUBLIC_URL` (required outside Development, blank in every template), `TECHSTRAP_PORTAL_DEFAULT_PRODUCT` (optional, blank).
  - The Portal csproj references `SyntaxCircus.Blazor.Seo` and `SyntaxCircus.Common` and has `InternalsVisibleTo TechStrap.Portal.Tests`.
  - Test hosts: `TechStrap.Portal.Tests.PortalFactory(string environment = "Development", IReadOnlyDictionary<string, string?>? settings = null, Action<IServiceCollection>? configureServices = null)` with `ApiBaseUrl = "http://api.test/"` and `PublicUrl = "https://portal.test"` applied before `settings`; the Api.Tests `PortalFactory` supplies `Api:BaseUrl` too.
  - D-045 in the decision log, the spec corrections block, the roadmap and discovery rows "In progress (09a)", and the package-map row for Blazor.Seo.

- [ ] **Step 1: Keep the helper tools outside the repository**

The mutation steps of every task use this small tool. Save it as `mut.py` in a folder outside the repository (for example `C:\tmp\p09a-tools\mut.py`; the plan calls that folder `$T`). It applies one or more replacements to a file, runs a command, reports KILLED or SURVIVED and always puts the file back.

`mut.py`

```python
"""Mutation helper for the PHASE-09a plan. Keep it OUTSIDE the repository (for example in a temp folder).

usage: python mut.py <file> --replace <old> <new> [--replace <old> <new> ...] -- <command ...>

Applies each replacement to <file> (each <old> must match exactly once; "\\n" in an argument means a newline), runs the command in the current
directory, prints the lines that summarise the run, and ALWAYS puts the file back. The mutation is KILLED when the command fails and a SURVIVOR
(exit code 3) when it passes. Run it from the repository root, after `git add`-ing the task's files, so a failed run can also be undone with
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
    print("KILLED (the command failed)" if result.returncode != 0 else "SURVIVED (the command passed)")
    code = 0 if result.returncode != 0 else 3
finally:
    with open(path, "w", encoding="utf-8", newline="") as handle:
        handle.write(original)
sys.exit(code)
```

Run every mutation from the repository root after `git add`-ing the task's files, so `git checkout -- <file>` also restores a file if a run is interrupted. A mutation that SURVIVES is a failed task unless this plan lists it as an honest survivor.

- [ ] **Step 2: Write the failing .NET tests**

Four Portal test files are new, `PortalFactory` is rewritten to take settings, and four Api.Tests files learn that the Portal now has required settings.

`PortalFactory` applies the two required settings before the caller's `settings`, so a test can blank one to prove the start fails. `PortalOptionsValidatorTests` calls the validator directly; `PortalOptionsHostTests` starts the real host. `RouteLiteralTests` scans the Portal sources: every page must declare its route with `@attribute [Route(PortalRoutes.XTemplate)]` and no source outside `PortalRoutes.cs` may spell a `/p` or `/t` route (it passes trivially now and guards every later page).

`tests/TechStrap.Portal.Tests/PortalFactory.cs`

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Tests;

/// <summary>
/// Starts the Portal host in-process in the given environment. A developer's gitignored .env.local must never leak into tests, and Production needs a trusted network to start.
/// The two required settings (the API address and the public URL) get test values; <paramref name="settings"/> is applied on top, so a test can blank one to prove the start fails.
/// </summary>
internal sealed class PortalFactory(
    string environment = "Development",
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<TechStrap.Portal.Program>
{
    public const string ApiBaseUrl = "http://api.test/";
    public const string PublicUrl = "https://portal.test";

    static PortalFactory()
    {
        Environment.SetEnvironmentVariable("DotEnv__Enabled", "false");

        // Production refuses to start without trusted proxies, and that option is bound before the factory can override it.
        Environment.SetEnvironmentVariable("TRUSTEDPROXY__TRUSTEDNETWORKS__0", "192.0.2.0/24");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PortalOptions.ApiBaseUrlKey] = ApiBaseUrl,
                [PortalOptions.PublicUrlKey] = PublicUrl,
            });
            configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
        });
        builder.ConfigureServices(services => configureServices?.Invoke(services));
    }
}
```

`tests/TechStrap.Portal.Tests/Settings/PortalOptionsValidatorTests.cs`

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Tests.Settings;

/// <summary>P09-T01: the Portal fails at start, naming the key, when a required setting is missing or malformed.</summary>
public sealed class PortalOptionsValidatorTests
{
    private static ValidateOptionsResult Validate(PortalOptions options, string environment = "Production")
    {
        var host = Substitute.For<IHostEnvironment>();
        host.EnvironmentName.Returns(environment);
        return new PortalOptionsValidator(host).Validate(null, options);
    }

    private static PortalOptions Valid() => new() { ApiBaseUrl = "http://api/", PublicUrl = "https://support.example.com" };

    [Fact]
    public void A_complete_set_of_settings_is_valid_in_Production()
    {
        Validate(Valid()).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("api")]
    [InlineData("/api/")]
    [InlineData("ftp://api/")]
    [InlineData("javascript:alert(1)")]
    public void The_api_address_must_be_an_absolute_http_or_https_url_and_the_failure_names_the_key(string value)
    {
        var options = Valid();
        options.ApiBaseUrl = value;

        var result = Validate(options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Api:BaseUrl");
        result.FailureMessage.ShouldContain("API__BASEURL");
    }

    [Theory]
    [InlineData("http://api/")]
    [InlineData("https://api.example.com/")]
    [InlineData("http://api")]
    public void An_http_or_https_api_address_is_valid(string value)
    {
        var options = Valid();
        options.ApiBaseUrl = value;

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Production", "")]
    [InlineData("Production", "  ")]
    [InlineData("Staging", "")]
    [InlineData("Staging", "  ")]
    public void The_public_url_is_required_outside_Development(string environment, string value)
    {
        var options = Valid();
        options.PublicUrl = value;

        var result = Validate(options, environment);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("TECHSTRAP_PORTAL_PUBLIC_URL");
    }

    [Fact]
    public void The_public_url_may_be_blank_in_Development()
    {
        var options = Valid();
        options.PublicUrl = "";

        Validate(options, "Development").Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("support.example.com")]
    [InlineData("/relative")]
    [InlineData("ftp://support.example.com")]
    [InlineData("https://support.example.com/?x=1")]
    [InlineData("https://support.example.com/#top")]
    [InlineData("https://support.example.com?")]
    [InlineData("https://user:pw@support.example.com")]
    public void A_public_url_that_is_not_an_absolute_http_base_is_refused_in_every_environment(string value)
    {
        foreach (var environment in new[] { "Development", "Production" })
        {
            var options = Valid();
            options.PublicUrl = value;

            var result = Validate(options, environment);

            result.Failed.ShouldBeTrue($"{value} in {environment}");
            result.FailureMessage.ShouldContain("TECHSTRAP_PORTAL_PUBLIC_URL");
        }
    }

    [Theory]
    [InlineData("https://support.example.com")]
    [InlineData("https://support.example.com/")]
    [InlineData("http://localhost:8082")]
    [InlineData("https://example.com/portal")]
    public void A_public_url_base_is_valid(string value)
    {
        var options = Valid();
        options.PublicUrl = value;

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("paperplane")]
    [InlineData("paper-plane-2")]
    public void The_default_product_is_optional_and_a_slug_when_given(string? value)
    {
        var options = Valid();
        options.DefaultProduct = value;

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Paper")]
    [InlineData("paper plane")]
    [InlineData("paper/plane")]
    [InlineData("-paper")]
    [InlineData("paper-")]
    [InlineData("paper--plane")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void A_default_product_that_is_not_a_slug_is_refused_and_the_failure_names_the_key(string value)
    {
        var options = Valid();
        options.DefaultProduct = value;

        var result = Validate(options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("TECHSTRAP_PORTAL_DEFAULT_PRODUCT");
    }

    [Fact]
    public void Every_failure_is_reported_at_once()
    {
        var result = Validate(new PortalOptions { ApiBaseUrl = "", PublicUrl = "", DefaultProduct = "Bad Key" });

        result.Failures.ShouldNotBeNull();
        result.Failures.Count().ShouldBe(3);
    }

    [Fact]
    public void The_normalised_addresses_drop_a_trailing_slash_from_the_public_url_and_add_one_to_the_api_address()
    {
        var options = new PortalOptions { ApiBaseUrl = "http://api", PublicUrl = "https://support.example.com/" };

        options.ApiBaseUri.ToString().ShouldBe("http://api/");
        options.PublicBaseUrl.ShouldBe("https://support.example.com");
        new PortalOptions { PublicUrl = "" }.PublicBaseUrl.ShouldBe("");
    }
}
```

`tests/TechStrap.Portal.Tests/Settings/PortalOptionsHostTests.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Tests.Settings;

/// <summary>P09-T01 at the host: the settings are bound from their flat and sectioned keys, and a bad one stops the start.</summary>
public sealed class PortalOptionsHostTests
{
    [Fact]
    public async Task The_settings_are_bound_from_their_keys()
    {
        await using var factory = new PortalFactory("Production", new Dictionary<string, string?>
        {
            [PortalOptions.ApiBaseUrlKey] = "http://api",
            [PortalOptions.PublicUrlKey] = "https://support.example.com/",
            [PortalOptions.DefaultProductKey] = "paperplane",
        });
        using var client = factory.CreateClient();

        var options = factory.Services.GetRequiredService<IOptions<PortalOptions>>().Value;

        options.ApiBaseUri.ToString().ShouldBe("http://api/");
        options.PublicBaseUrl.ShouldBe("https://support.example.com");
        options.DefaultProduct.ShouldBe("paperplane");
    }

    [Theory]
    [InlineData("Production", PortalOptions.PublicUrlKey, "", PortalOptions.PublicUrlKey)]
    [InlineData("Production", PortalOptions.ApiBaseUrlKey, "", "API__BASEURL")]
    [InlineData("Development", PortalOptions.ApiBaseUrlKey, "", "API__BASEURL")]
    [InlineData("Development", PortalOptions.DefaultProductKey, "Not A Slug", PortalOptions.DefaultProductKey)]
    public async Task A_missing_or_malformed_setting_fails_the_start_and_names_the_key(string environment, string key, string value, string expectedInMessage)
    {
        await using var factory = new PortalFactory(environment, new Dictionary<string, string?> { [key] = value });

        var failure = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        failure.Message.ShouldContain(expectedInMessage);
    }

    [Fact]
    public async Task The_public_url_may_be_blank_in_Development()
    {
        await using var factory = new PortalFactory("Development", new Dictionary<string, string?> { [PortalOptions.PublicUrlKey] = "" });

        Should.NotThrow(() => factory.CreateClient().Dispose());
    }
}
```

`tests/TechStrap.Portal.Tests/Routing/PortalRoutesTests.cs`

```csharp
using System.Reflection;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Tests.Routing;

/// <summary>P09-T01: every route template and builder lives in <see cref="PortalRoutes"/>, so a page never repeats a route string.</summary>
public sealed class PortalRoutesTests
{
    private static IEnumerable<(string Name, string Value)> Templates() =>
        typeof(PortalRoutes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.Name.EndsWith("Template", StringComparison.Ordinal))
            .Select(field => (field.Name, (string)field.GetRawConstantValue()!));

    [Fact]
    public void Every_template_is_an_absolute_path_and_no_two_are_equal()
    {
        var templates = Templates().ToList();

        templates.Count.ShouldBe(15, "a route was added or removed: update the spec's route list and this count together");
        templates.ShouldAllBe(t => t.Value.StartsWith('/'));
        templates.Select(t => t.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(templates.Count);
    }

    [Fact]
    public void The_templates_match_the_routes_of_the_spec_exactly()
    {
        PortalRoutes.HomeTemplate.ShouldBe("/");
        PortalRoutes.NotFoundTemplate.ShouldBe("/not-found");
        PortalRoutes.ErrorTemplate.ShouldBe("/error");
        PortalRoutes.StyleGuideTemplate.ShouldBe("/_styleguide");
        PortalRoutes.ProductHomeTemplate.ShouldBe("/p/{key}");
        PortalRoutes.ContactTemplate.ShouldBe("/p/{key}/contact");
        PortalRoutes.ContactReceivedTemplate.ShouldBe("/p/{key}/contact/received");
        PortalRoutes.LostLinkTemplate.ShouldBe("/p/{key}/lost-link");
        PortalRoutes.KbHomeTemplate.ShouldBe("/p/{key}/kb");
        PortalRoutes.KbCategoryTemplate.ShouldBe("/p/{key}/kb/{category}");
        PortalRoutes.KbArticleTemplate.ShouldBe("/p/{key}/kb/{category}/{slug}");
        PortalRoutes.KbSearchTemplate.ShouldBe("/p/{key}/kb/search");
        PortalRoutes.KbSuggestTemplate.ShouldBe("/p/{key}/kb/suggest");
        PortalRoutes.TicketTemplate.ShouldBe("/t/{token}");
        PortalRoutes.TicketAttachmentTemplate.ShouldBe("/t/{token}/attachments/{id}");
    }

    [Fact]
    public void The_builders_fill_the_templates()
    {
        PortalRoutes.ProductHome("paperplane").ShouldBe("/p/paperplane");
        PortalRoutes.Contact("paperplane").ShouldBe("/p/paperplane/contact");
        PortalRoutes.ContactReceived("paperplane").ShouldBe("/p/paperplane/contact/received");
        PortalRoutes.LostLink("paperplane").ShouldBe("/p/paperplane/lost-link");
        PortalRoutes.KbHome("paperplane").ShouldBe("/p/paperplane/kb");
        PortalRoutes.KbCategory("paperplane", "guides").ShouldBe("/p/paperplane/kb/guides");
        PortalRoutes.KbArticle("paperplane", "guides", "dark-mode").ShouldBe("/p/paperplane/kb/guides/dark-mode");
        PortalRoutes.KbSearch("paperplane").ShouldBe("/p/paperplane/kb/search");
        PortalRoutes.KbSuggest("paperplane").ShouldBe("/p/paperplane/kb/suggest");
        PortalRoutes.Ticket("abc").ShouldBe("/t/abc");
        PortalRoutes.TicketAttachment("abc", Guid.Parse("11111111-2222-3333-4444-555555555555")).ShouldBe("/t/abc/attachments/11111111-2222-3333-4444-555555555555");
    }

    [Fact]
    public void A_builder_escapes_each_value_so_it_can_never_add_a_segment_a_query_or_a_fragment()
    {
        PortalRoutes.ProductHome("a/b?c#d").ShouldBe("/p/a%2Fb%3Fc%23d");
        PortalRoutes.KbArticle("p", "../x", "y z").ShouldBe("/p/p/kb/..%2Fx/y%20z");
        PortalRoutes.Ticket("a b").ShouldBe("/t/a%20b");
    }

    [Fact]
    public void The_prefixes_cover_the_product_and_ticket_routes()
    {
        PortalRoutes.ProductPrefix.ShouldBe("/p");
        PortalRoutes.TicketPrefix.ShouldBe("/t");
        Templates().Where(t => t.Name.StartsWith("Ticket", StringComparison.Ordinal)).ShouldAllBe(t => t.Value.StartsWith("/t/", StringComparison.Ordinal));
    }
}
```

`tests/TechStrap.Portal.Tests/Routing/ProductKeyShapeTests.cs`

```csharp
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Tests.Routing;

/// <summary>The shape every product key has (the Domain's slug rule, at most 40 characters). A key that does not have it is never sent to the API.</summary>
public sealed class ProductKeyShapeTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("paperplane")]
    [InlineData("paper-plane")]
    [InlineData("p2-x9")]
    [InlineData("1")]
    public void A_slug_is_well_formed(string key) => ProductKeyShape.IsWellFormed(key).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Paper")]
    [InlineData("paper_plane")]
    [InlineData("paper plane")]
    [InlineData("paper/plane")]
    [InlineData("-paper")]
    [InlineData("paper-")]
    [InlineData("paper--plane")]
    [InlineData("paper\n")]
    [InlineData("..")]
    [InlineData("a?b")]
    public void Anything_else_is_not(string? key) => ProductKeyShape.IsWellFormed(key).ShouldBeFalse();

    [Fact]
    public void Forty_characters_is_the_longest()
    {
        ProductKeyShape.IsWellFormed(new string('a', ProductKeyShape.MaxLength)).ShouldBeTrue();
        ProductKeyShape.IsWellFormed(new string('a', ProductKeyShape.MaxLength + 1)).ShouldBeFalse();
        ProductKeyShape.MaxLength.ShouldBe(40);
    }

    [Fact]
    public void A_non_ascii_letter_or_digit_is_not_well_formed()
    {
        ProductKeyShape.IsWellFormed("pap" + char.ConvertFromUtf32(0xE9) + "r").ShouldBeFalse();
        ProductKeyShape.IsWellFormed("paper" + char.ConvertFromUtf32(0x0663)).ShouldBeFalse();
    }
}
```

`tests/TechStrap.Portal.Tests/Routing/RouteLiteralTests.cs`

```csharp
using System.Text.RegularExpressions;
using TechStrap.Tests.Shared;

namespace TechStrap.Portal.Tests.Routing;

/// <summary>
/// P09-T01 validation: "route constants used by every page (no inline route strings repeated)". Every page declares its route with
/// <c>@attribute [Route(PortalRoutes.XTemplate)]</c> and every link is built by a <c>PortalRoutes</c> builder, so a route changes in one place.
/// </summary>
public sealed partial class RouteLiteralTests
{
    private static IEnumerable<(string Path, string Text)> PortalSources() =>
        Directory.EnumerateFiles(RepositoryRoot.Combine("src", "TechStrap.Portal"), "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".razor", StringComparison.Ordinal) || path.EndsWith(".cs", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(path => (Path.GetRelativePath(RepositoryRoot.Combine("src", "TechStrap.Portal"), path).Replace('\\', '/'), File.ReadAllText(path)));

    // A quoted or attribute-quoted string that starts a Portal route: "/p/", "/t/", "/p" and "/t" alone, or the root-relative pages.
    [GeneratedRegex("""["'](/p|/t)(/[^"']*)?["']|href="/(p|t)/""", RegexOptions.CultureInvariant)]
    private static partial Regex RouteLiteral();

    [Fact]
    public void The_scan_sees_the_portal_sources()
    {
        PortalSources().Count().ShouldBeGreaterThan(10);
        PortalSources().ShouldContain(source => source.Path == "Routing/PortalRoutes.cs");
    }

    [Fact]
    public void No_page_declares_its_route_with_a_string_literal()
    {
        var offenders = PortalSources().Where(source => source.Path.EndsWith(".razor", StringComparison.Ordinal) && Regex.IsMatch(source.Text, @"(?m)^@page\s")).Select(source => source.Path);

        offenders.ShouldBeEmpty("use @attribute [Route(PortalRoutes.XTemplate)]");
    }

    [Fact]
    public void No_source_outside_PortalRoutes_spells_a_p_or_t_route()
    {
        var offenders = PortalSources()
            .Where(source => source.Path != "Routing/PortalRoutes.cs")
            .Where(source => RouteLiteral().IsMatch(source.Text))
            .Select(source => source.Path);

        offenders.ShouldBeEmpty("build the address with a PortalRoutes builder");
    }
}
```

The Api.Tests changes: the Portal's `HostFactory` default carries the API address; the Development start needs `Api:BaseUrl`; the Production blank-template tests now expect the Portal to name its two required keys (it used to start); the env-completeness test lists the three new keys.

`tests/TechStrap.Api.Tests/HostFactory.cs`

```diff
--- tests/TechStrap.Api.Tests/HostFactory.cs
+++ tests/TechStrap.Api.Tests/HostFactory.cs
@@ -133,5 +133,11 @@ public sealed class AdminFactory : HostFactory<TechStrap.Admin.Program>
     public StubApiHandler Api { get; }
 }
 
+/// <summary>The Portal host with the API address it requires at start (D-045); <c>settings</c> is applied on top. The public URL comes from the defaults every host gets.</summary>
 public sealed class PortalFactory(string environment = "Development", IReadOnlyDictionary<string, string?>? settings = null)
-    : HostFactory<TechStrap.Portal.Program>(environment, settings);
+    : HostFactory<TechStrap.Portal.Program>(
+        environment,
+        new Dictionary<string, string?> { ["Api:BaseUrl"] = "http://api.test/" }
+            .Concat(settings ?? new Dictionary<string, string?>())
+            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
+            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase));
```

`tests/TechStrap.Api.Tests/Config/DevelopmentStartupTests.cs`

```diff
--- tests/TechStrap.Api.Tests/Config/DevelopmentStartupTests.cs
+++ tests/TechStrap.Api.Tests/Config/DevelopmentStartupTests.cs
@@ -45,7 +45,7 @@ public sealed class DevelopmentStartupTests
         { HostKind.Api, ["TECHSTRAP_PORTAL_PUBLIC_URL=http://localhost:8082", "Storage:Local:RootPath=storage"] },
         { HostKind.Worker, [] },
         { HostKind.Admin, ["Api:BaseUrl=http://localhost:8080/"] },
-        { HostKind.Portal, [] },
+        { HostKind.Portal, ["Api:BaseUrl=http://localhost:8080/"] },
     };
 
     [Theory]
```

`tests/TechStrap.Api.Tests/Config/ProductionBlankTemplateTests.cs`

```diff
--- tests/TechStrap.Api.Tests/Config/ProductionBlankTemplateTests.cs
+++ tests/TechStrap.Api.Tests/Config/ProductionBlankTemplateTests.cs
@@ -23,13 +23,13 @@ public sealed class ProductionBlankTemplateTests
 
     public static TheoryData<HostKind> WebHosts() => new() { HostKind.Api, HostKind.Admin, HostKind.Portal };
 
-    // What each host reports when the template is the only configuration. The Portal reads nothing required yet, so only the trusted proxies (which compose supplies) stop it.
+    // What each host reports when the template is the only configuration. The Portal reports its API address (which compose supplies) and its public URL; the trusted proxies, which compose also supplies, are checked once those are valid.
     public static TheoryData<HostKind, string[]> HostsAndTheKeysTheyReportAlone() => new()
     {
         { HostKind.Api, ["ConnectionStrings:TechStrap", "Authentication:JwtBearer:Authority", "TECHSTRAP_PORTAL_PUBLIC_URL", "TECHSTRAP_API_PUBLIC_URL", "Storage:Local:RootPath"] },
         { HostKind.Worker, ["ConnectionStrings:TechStrap", "Email:Smtp:Host", "Email:Smtp:DefaultFrom"] },
         { HostKind.Admin, ["Auth:Authority", "Auth:ClientId", "Auth:ClientSecret", "Api:BaseUrl"] },
-        { HostKind.Portal, ["TrustedProxy"] },
+        { HostKind.Portal, ["Api:BaseUrl", "TECHSTRAP_PORTAL_PUBLIC_URL"] },
     };
 
     // What remains once compose has set its own values: exactly what the operator must fill in.
@@ -38,6 +38,7 @@ public sealed class ProductionBlankTemplateTests
         { HostKind.Api, ["ConnectionStrings:TechStrap", "Authentication:JwtBearer:Authority", "TECHSTRAP_PORTAL_PUBLIC_URL", "TECHSTRAP_API_PUBLIC_URL"] },
         { HostKind.Worker, ["ConnectionStrings:TechStrap", "Email:Smtp:Host", "Email:Smtp:DefaultFrom"] },
         { HostKind.Admin, ["Auth:Authority", "Auth:ClientId", "Auth:ClientSecret"] },
+        { HostKind.Portal, ["TECHSTRAP_PORTAL_PUBLIC_URL"] },
     };
 
     /// <summary>The blank template as the host sees it, plus the Database setting every test host needs so the Api does not migrate a database that is not there.</summary>
@@ -127,9 +128,10 @@ public sealed class ProductionBlankTemplateTests
             "Auth:ClientId=techstrap-admin",
             "Auth:ClientSecret=not-a-real-secret",
         ],
+        [HostKind.Portal] = ["TECHSTRAP_PORTAL_PUBLIC_URL=https://support.example.com"],
     };
 
-    public static TheoryData<HostKind> FilledHosts() => new() { HostKind.Api, HostKind.Worker, HostKind.Admin };
+    public static TheoryData<HostKind> FilledHosts() => new() { HostKind.Api, HostKind.Worker, HostKind.Admin, HostKind.Portal };
 
     [Theory]
     [MemberData(nameof(FilledHosts))]
@@ -176,15 +178,19 @@ public sealed class ProductionBlankTemplateTests
     }
 
     [Fact]
-    public async Task The_Portal_has_no_required_setting_yet_so_the_blank_template_with_the_compose_trust_starts()
+    public async Task The_Portal_blank_template_with_the_compose_trust_and_address_still_fails_naming_only_the_public_url()
     {
-        // PHASE-09 adds API__BASEURL and TECHSTRAP_PORTAL_PUBLIC_URL to the Portal and to this list; until then the Portal reads only optional settings.
+        // Compose supplies the trusted network and the API address; the operator must set the Portal's public address (D-045).
         using var environment = new ScopedEnvironment(CleanEnvironment);
         using var composeTrust = new ScopedEnvironment(("TrustedProxy__TrustedNetworks__0", ComposeSubnet));
+        var settings = BlankTemplate(HostKind.Portal);
+        settings["Api:BaseUrl"] = "http://api/";
 
-        var failure = await ConfigHosts.TryStartAsync(HostKind.Portal, "Production", BlankTemplate(HostKind.Portal));
+        var failure = await ConfigHosts.TryStartAsync(HostKind.Portal, "Production", settings);
 
-        failure.ShouldBeNull($"The Portal did not start from the blank template with the compose trust: {failure}");
+        failure.ShouldNotBeNull("The Portal started in Production without its public URL");
+        AssertNames(failure, HostKind.Portal, ["TECHSTRAP_PORTAL_PUBLIC_URL"]);
+        string.Join(Environment.NewLine, Messages(failure)).ShouldNotContain("Api:BaseUrl");
     }
 
     [Theory]
@@ -209,8 +215,11 @@ public sealed class ProductionBlankTemplateTests
         // (an empty string that still counts as one configured element), and a whitespace string binds the same way.
         using var environment = new ScopedEnvironment(CleanEnvironment);
         using var blankElement = new ScopedEnvironment(("TrustedProxy__TrustedProxies__0", " "));
+        var settings = BlankTemplate(HostKind.Portal);
+        settings["Api:BaseUrl"] = "http://api/";
+        settings["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://support.example.com";
 
-        var failure = await ConfigHosts.TryStartAsync(HostKind.Portal, "Production", BlankTemplate(HostKind.Portal));
+        var failure = await ConfigHosts.TryStartAsync(HostKind.Portal, "Production", settings);
 
         failure.ShouldBeNull($"A blank element was expected to satisfy the check: {failure}");
     }
```

`tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs`

```diff
--- tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs
+++ tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs
@@ -126,6 +126,9 @@ public sealed partial class EnvExampleCompletenessTests
             [
                 .. ObservabilityKeys(),
                 .. TrustedProxyKeys(),
+                "API__BASEURL",
+                "TECHSTRAP_PORTAL_PUBLIC_URL",
+                "TECHSTRAP_PORTAL_DEFAULT_PRODUCT",
                 "TECHSTRAP_PORTAL_SHOW_POWERED_BY",
                 "DATAPROTECTION__KEYRINGPATH",
             ]
@@ -158,11 +161,10 @@ public sealed partial class EnvExampleCompletenessTests
     [Theory]
     [InlineData("TechStrap.Worker", "STORAGE__LOCAL__ROOTPATH")]
     [InlineData("TechStrap.Worker", "TECHSTRAP_PORTAL_PUBLIC_URL")]
-    [InlineData("TechStrap.Portal", "API__BASEURL")]
-    [InlineData("TechStrap.Portal", "TECHSTRAP_PORTAL_PUBLIC_URL")]
+    [InlineData("TechStrap.Portal", "STORAGE__LOCAL__ROOTPATH")]
     public void Env_example_does_not_document_a_key_the_host_never_reads(string host, string staleKey)
     {
-        // The Worker registers no attachment storage and builds no portal links; the Portal reads no Api address or public URL until PHASE-09 adds them.
+        // The Worker registers no attachment storage and builds no portal links; the Portal never touches storage.
         DocumentedKeys(host).ShouldNotContain(staleKey);
     }
 
```

- [ ] **Step 3: Write the failing Pester tests**

`ConfigContract.Tests.ps1` learns the Portal's compose-owned key and blank-allowed keys, and gains a test that pins the local compose's Portal environment. `RepositoryDocs.Tests.ps1` gains the D-045 block (the later tasks extend it).

`scripts/tests/ConfigContract.Tests.ps1`

```diff
--- scripts/tests/ConfigContract.Tests.ps1
+++ scripts/tests/ConfigContract.Tests.ps1
@@ -30,7 +30,7 @@ BeforeAll {
         Api    = @('STORAGE__LOCAL__ROOTPATH', 'TRUSTEDPROXY__TRUSTEDNETWORKS__0')
         Worker = @()
         Admin  = @('API__BASEURL', 'DATAPROTECTION__KEYRINGPATH', 'TRUSTEDPROXY__TRUSTEDNETWORKS__0')
-        Portal = @('DATAPROTECTION__KEYRINGPATH', 'TRUSTEDPROXY__TRUSTEDNETWORKS__0')
+        Portal = @('API__BASEURL', 'DATAPROTECTION__KEYRINGPATH', 'TRUSTEDPROXY__TRUSTEDNETWORKS__0')
     }
     # Names compose sets that are not appsettings keys (the host environment and the switch that stops a container loading a .env file):
     $script:ComposeNonSettings = @('ASPNETCORE_ENVIRONMENT', 'DOTENV__ENABLED')
@@ -43,7 +43,7 @@ BeforeAll {
         Api    = $script:CommonBlank + @('SECURITYHEADERS__ROBOTSTAG', 'CONNECTIONSTRINGS__TECHSTRAP', 'AUTHENTICATION__JWTBEARER__AUTHORITY', 'TECHSTRAP_PORTAL_PUBLIC_URL', 'TECHSTRAP_API_PUBLIC_URL', 'TECHSTRAP_ADMIN_PUBLIC_URL', 'STORAGE__LOCAL__ROOTPATH')
         Worker = $script:CommonBlank + @('CONNECTIONSTRINGS__TECHSTRAP', 'EMAIL__SMTP__HOST', 'EMAIL__SMTP__USERNAME', 'EMAIL__SMTP__PASSWORD', 'EMAIL__SMTP__DEFAULTFROM', 'EMAIL__SMTP__TLSMODE', 'EMAIL__SMTP__TOTALSENDTIMEOUT', 'EMAILOUTBOX__WORKERID')
         Admin  = $script:CommonBlank + @('SECURITYHEADERS__ROBOTSTAG', 'API__BASEURL', 'AUTH__AUTHORITY', 'AUTH__CLIENTID', 'AUTH__CLIENTSECRET', 'DATAPROTECTION__KEYRINGPATH', 'TECHSTRAP_PORTAL_PUBLIC_URL')
-        Portal = $script:CommonBlank + @('SECURITYHEADERS__ROBOTSTAG', 'DATAPROTECTION__KEYRINGPATH')
+        Portal = $script:CommonBlank + @('SECURITYHEADERS__ROBOTSTAG', 'API__BASEURL', 'DATAPROTECTION__KEYRINGPATH', 'TECHSTRAP_PORTAL_PUBLIC_URL', 'TECHSTRAP_PORTAL_DEFAULT_PRODUCT')
     }
     # A key containing one of these words is secret-shaped: its committed value must be blank, whatever the value looks like (a numeric password is still a password).
     # OPENTELEMETRY__HEADERS is added because the OTLP headers carry a token. KeyRingPath is a directory, not a key.
@@ -350,6 +350,13 @@ Describe 'the config contract of the local compose' {
         @($admin | Sort-Object) | Should -Be @('API__BASEURL', 'ASPNETCORE_ENVIRONMENT', 'DATAPROTECTION__KEYRINGPATH', 'TECHSTRAP_PORTAL_PUBLIC_URL', 'TRUSTEDPROXY__TRUSTEDNETWORKS__0')
     }
 
+    It 'the local compose gives the Portal exactly the API address, the public URL, the trusted proxy and the key ring (D-045)' {
+        $portal = (Get-ComposeEnvironmentKeys -File 'docker-compose.yml')['portal']
+        @($portal | Sort-Object) | Should -Be @('API__BASEURL', 'ASPNETCORE_ENVIRONMENT', 'DATAPROTECTION__KEYRINGPATH', 'TECHSTRAP_PORTAL_PUBLIC_URL', 'TRUSTEDPROXY__TRUSTEDNETWORKS__0')
+        $compose = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'docker-compose.yml') -Raw
+        $compose | Should -Match '(?s)  portal:.*?Api__BaseUrl: http://api/.*?TECHSTRAP_PORTAL_PUBLIC_URL: http://localhost:8082'
+    }
+
     It 'the root .env.example documents the local compose inputs and nothing else' {
         $keys = @(Get-EnvEntries -Path (Join-Path $script:RepoRoot '.env.example') | ForEach-Object { $_.Key } | Sort-Object)
         $keys | Should -Be @('REVERSE_PROXY_CIDR', 'TECHSTRAP_MAILPIT_PORT', 'TECHSTRAP_SEED_DEV_DATA', 'TECHSTRAP_SUBNET')
```

`scripts/tests/RepositoryDocs.Tests.ps1`

```diff
--- scripts/tests/RepositoryDocs.Tests.ps1
+++ scripts/tests/RepositoryDocs.Tests.ps1
@@ -138,6 +138,34 @@ Describe 'D-044 (the knowledge base)' {
     }
 }
 
+Describe 'D-045 (the public portal)' {
+    BeforeAll { $script:Log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md' }
+
+    It 'is in the decision log with its date, its status, a header bullet and an index row' {
+        $script:Log | Should -Match '(?m)^## D-045: PHASE-09: the public portal'
+        $script:Log | Should -Match '(?s)## D-045:.*?- \*\*Status:\*\* Approved \(owner 2026-10-05.*?- \*\*Date:\*\* 2026-10-05'
+        $script:Log | Should -Match '(?m)^\| D-045 \|.*\| 2026-10-05 \|'
+        $script:Log | Should -Match '(?m)^- \*\*Owner decision \(2026-10-05, PHASE-09 planning\):\*\* D-045'
+    }
+
+    It 'records the owner decisions and the technical rulings the three pull requests rely on' {
+        foreach ($phrase in 'Three pull requests', 'vanilla JS', '<ts-kb-suggestions>', 'GET api/public/products', 'TECHSTRAP_PORTAL_DEFAULT_PRODUCT', 'ProductKey', 'T20 (Playwright end-to-end) is deferred',
+                'ApiConnection', 'X-Ticket-Token', 'YAGNI', 'MapSeoRobotsTxt', 'Seo:BaseUrl', 'per-path rules', 'Referrer-Policy: no-referrer', 'byte-identical', '?ref=', 'ProductThemeViewModel', 'Contracts plus Hosting') {
+            $script:Log | Should -Match ([regex]::Escape($phrase)) -Because "D-045 must mention $phrase"
+        }
+    }
+
+    It 'corrects the PHASE-09 spec, the package map and marks the roadmap and discovery rows as in progress (09a)' {
+        $spec = Get-RepoText 'docs/architecture/PHASE-09-public-portal.md'
+        $spec | Should -Match '(?m)^### Corrections \(D-045, 2026-10-05\)'
+        $spec | Should -Match 'MapSeoRobotsTxt'
+        $spec | Should -Match 'ProductThemeViewModel'
+        (Get-RepoText 'docs/architecture/03-PACKAGE-MAP.md') | Should -Match 'MapSeoRobotsTxt'
+        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 09 \|.*D-045.*\| In progress \(09a\)'
+        (Get-RepoText 'docs/architecture/00-DISCOVERY-INDEX.md') | Should -Match '(?m)^\| 09 \|.*\| In progress \(09a\)'
+    }
+}
+
 Describe 'the deployment runbook' {
     BeforeAll { $script:Runbook = Get-RepoText 'docs/self-hosting/DEPLOYMENT.md' }
 
```

- [ ] **Step 4: Run the tests to see them fail**

Run: `dotnet build tests/TechStrap.Portal.Tests -c Release`
Expected: FAIL to compile, 13 distinct errors, the first of them `error CS0234: The type or namespace name 'Settings' does not exist in the namespace 'TechStrap.Portal'` (in `PortalFactory.cs`), then `CS0234 ... 'Routing' does not exist` and `CS0103: The name 'PortalOptions' does not exist in the current context`.

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ConfigContract.Tests.ps1 -Output Minimal`
Expected: FAIL: `Tests Passed: 83, Failed: 3` (the Portal's blank list, the local compose test and the deploy compose test).

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/RepositoryDocs.Tests.ps1 -Output Minimal`
Expected: FAIL: `Tests Passed: 20, Failed: 3` (the three D-045 tests).

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/*/ProductionBlankTemplateTests/*"`
Expected: FAIL: `total: 18, failed: 3, succeeded: 15` (the three Portal cases: the blank template starts today).

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/*/EnvExampleCompletenessTests/*"`
Expected: FAIL: `total: 12, failed: 1` (the Portal's `.env.example` lacks the new keys).

- [ ] **Step 5: Implement the Portal project changes**

The Portal csproj: the two package references and the `InternalsVisibleTo` (validators and clients are internal).

`src/TechStrap.Portal/TechStrap.Portal.csproj`

```diff
--- src/TechStrap.Portal/TechStrap.Portal.csproj
+++ src/TechStrap.Portal/TechStrap.Portal.csproj
@@ -17,8 +17,15 @@
     <PackageReference Include="SyntaxCircus.AspNetCore.Common" />
     <PackageReference Include="SyntaxCircus.AspNetCore.Serilog" />
     <PackageReference Include="SyntaxCircus.Blazor.Components" />
+    <PackageReference Include="SyntaxCircus.Blazor.Seo" />
+    <PackageReference Include="SyntaxCircus.Common" />
     <PackageReference Include="SyntaxCircus.DotEnv" />
     <PackageReference Include="SyntaxCircus.Observability" />
   </ItemGroup>
 
+  <ItemGroup>
+    <!-- Internal options validators, clients and view models are unit-tested directly. -->
+    <InternalsVisibleTo Include="TechStrap.Portal.Tests" />
+  </ItemGroup>
+
 </Project>
```

The route constants and the product key shape:

`src/TechStrap.Portal/Routing/PortalRoutes.cs`

```csharp
namespace TechStrap.Portal.Routing;

/// <summary>
/// Every route the Portal serves, once. A page declares its route with <c>@attribute [Route(PortalRoutes.XTemplate)]</c> and every link, redirect and form action is built by the matching
/// builder, so a route changes in one place (RouteLiteralTests fails on a second spelling). The product key and the ticket token are always escaped by the builders, so a value can never add
/// a path segment, a query or a fragment. The routes of PHASE-09b and 09c are here already, so the three pull requests share one list.
/// </summary>
public static class PortalRoutes
{
    public const string ProductPrefix = "/p";

    /// <summary>The ticket pages. The Portal's header rules and its robots.txt exclusion apply to everything under it.</summary>
    public const string TicketPrefix = "/t";

    public const string HomeTemplate = "/";
    public const string NotFoundTemplate = "/not-found";
    public const string ErrorTemplate = "/error";
    public const string StyleGuideTemplate = "/_styleguide";

    public const string ProductHomeTemplate = "/p/{key}";
    public const string ContactTemplate = "/p/{key}/contact";
    public const string ContactReceivedTemplate = "/p/{key}/contact/received";
    public const string LostLinkTemplate = "/p/{key}/lost-link";
    public const string KbHomeTemplate = "/p/{key}/kb";

    // A literal segment wins over a parameter in endpoint routing: /p/{key}/kb/search and /kb/suggest are not categories. The API reserves the category slug "search" (KbLimits.ReservedCategorySlug).
    public const string KbCategoryTemplate = "/p/{key}/kb/{category}";
    public const string KbArticleTemplate = "/p/{key}/kb/{category}/{slug}";
    public const string KbSearchTemplate = "/p/{key}/kb/search";
    public const string KbSuggestTemplate = "/p/{key}/kb/suggest";

    public const string TicketTemplate = "/t/{token}";
    public const string TicketAttachmentTemplate = "/t/{token}/attachments/{id}";

    public static string ProductHome(string key) => $"{ProductPrefix}/{Escape(key)}";

    public static string Contact(string key) => $"{ProductHome(key)}/contact";

    public static string ContactReceived(string key) => $"{Contact(key)}/received";

    public static string LostLink(string key) => $"{ProductHome(key)}/lost-link";

    public static string KbHome(string key) => $"{ProductHome(key)}/kb";

    public static string KbCategory(string key, string category) => $"{KbHome(key)}/{Escape(category)}";

    public static string KbArticle(string key, string category, string slug) => $"{KbCategory(key, category)}/{Escape(slug)}";

    public static string KbSearch(string key) => $"{KbHome(key)}/search";

    public static string KbSuggest(string key) => $"{KbHome(key)}/suggest";

    public static string Ticket(string token) => $"{TicketPrefix}/{Escape(token)}";

    public static string TicketAttachment(string token, Guid attachmentId) => $"{Ticket(token)}/attachments/{attachmentId}";

    private static string Escape(string value) => Uri.EscapeDataString(value);
}
```

`src/TechStrap.Portal/Routing/ProductKeyShape.cs`

```csharp
using System.Text.RegularExpressions;

namespace TechStrap.Portal.Routing;

/// <summary>
/// The shape of a product key: lowercase ASCII letters and digits in groups joined by single hyphens, at most 40 characters. It is the Domain's slug rule (the Portal cannot reference the Domain),
/// and it is the one test every key passes before it is sent anywhere: a malformed key from a URL is answered as an unknown route without calling the API, so a crafted segment can never
/// reach a request path or a log line.
/// </summary>
public static partial class ProductKeyShape
{
    /// <summary>The Domain's <c>DomainLimits.SlugMaxLength</c>.</summary>
    public const int MaxLength = 40;

    [GeneratedRegex(@"\A[a-z0-9]+(?:-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Slug();

    public static bool IsWellFormed(string? key) => key is { Length: > 0 and <= MaxLength } && Slug().IsMatch(key);
}
```

The options, the validator and the registration. The validator never throws and reports every problem at once; blank is "required" outside Development and "absent" in Development, and a present value is validated everywhere.

`src/TechStrap.Portal/Settings/PortalOptions.cs`

```csharp
namespace TechStrap.Portal.Settings;

/// <summary>
/// What the Portal needs to know about its surroundings (D-043, D-045). The keys are flat or sectioned exactly as the other hosts spell them, so one env file can configure the Api, the Admin and the
/// Portal. Every value is validated when the host starts (<see cref="PortalOptionsValidator"/>), so a missing or malformed one stops the start with the key named.
/// </summary>
public sealed class PortalOptions
{
    /// <summary>The address of the TechStrap API as the Portal's container reaches it (<c>API__BASEURL</c>; the compose files set <c>http://api/</c>). Required.</summary>
    public const string ApiBaseUrlKey = "Api:BaseUrl";

    /// <summary>The Portal's own public address as customers see it (the same key and value as in the Api). Required outside Development. It is also the base of every canonical URL and of the sitemap (<c>Seo:BaseUrl</c> is derived from it, D-045).</summary>
    public const string PublicUrlKey = "TECHSTRAP_PORTAL_PUBLIC_URL";

    /// <summary>The product the Portal's root page sends visitors to. Optional: blank shows a neutral page with no product list.</summary>
    public const string DefaultProductKey = "TECHSTRAP_PORTAL_DEFAULT_PRODUCT";

    public string ApiBaseUrl { get; set; } = string.Empty;

    public string PublicUrl { get; set; } = string.Empty;

    public string? DefaultProduct { get; set; }

    /// <summary>The API address with a trailing slash, so a relative request path is resolved under it. Call only after validation.</summary>
    public Uri ApiBaseUri => new(ApiBaseUrl.Trim().EndsWith('/') ? ApiBaseUrl.Trim() : ApiBaseUrl.Trim() + "/", UriKind.Absolute);

    /// <summary>The public address without a trailing slash (empty when blank), ready to put in front of a path that starts with a slash.</summary>
    public string PublicBaseUrl => PublicUrl.Trim().TrimEnd('/');

    /// <summary>The default product key, or null when none is configured (blank counts as none).</summary>
    public string? DefaultProductKeyOrNull => string.IsNullOrWhiteSpace(DefaultProduct) ? null : DefaultProduct.Trim();
}
```

`src/TechStrap.Portal/Settings/PortalOptionsValidator.cs`

```csharp
using Microsoft.Extensions.Options;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Settings;

/// <summary>Fails the start when the Portal's settings are missing or malformed. Each message names the setting as the operator sets it, so the container's log says what to fix.</summary>
internal sealed class PortalOptionsValidator(IHostEnvironment environment) : IValidateOptions<PortalOptions>
{
    public ValidateOptionsResult Validate(string? name, PortalOptions options)
    {
        var failures = new List<string>();

        if (!IsHttpUrl(options.ApiBaseUrl))
        {
            failures.Add($"{PortalOptions.ApiBaseUrlKey} (API__BASEURL) must be the absolute http or https URL of the TechStrap API, for example http://api/.");
        }

        if (string.IsNullOrWhiteSpace(options.PublicUrl))
        {
            if (!environment.IsDevelopment())
            {
                failures.Add($"{PortalOptions.PublicUrlKey} is required: the public address of the Portal, for example https://support.example.com.");
            }
        }
        else if (!IsHttpBase(options.PublicUrl))
        {
            failures.Add($"{PortalOptions.PublicUrlKey} must be an absolute http or https URL with no query, fragment or user info, for example https://support.example.com.");
        }

        if (!string.IsNullOrWhiteSpace(options.DefaultProduct) && !ProductKeyShape.IsWellFormed(options.DefaultProduct.Trim()))
        {
            failures.Add($"{PortalOptions.DefaultProductKey} must be a product key (lowercase letters, digits and single hyphens, at most {ProductKeyShape.MaxLength} characters), or blank.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    /// <summary>An absolute http(s) address that can be a base: no user info, no query, no fragment. A lone "?" or "#" counts (<see cref="Uri.Query"/> and <see cref="Uri.Fragment"/> keep it).</summary>
    private static bool IsHttpBase(string value) =>
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && string.IsNullOrEmpty(uri.UserInfo)
        && uri.Query.Length == 0
        && uri.Fragment.Length == 0;
}
```

`src/TechStrap.Portal/Settings/PortalOptionsRegistration.cs`

```csharp
using Microsoft.Extensions.Options;

namespace TechStrap.Portal.Settings;

public static class PortalOptionsRegistration
{
    /// <summary>Binds <see cref="PortalOptions"/> from its three keys and validates it at start (not on first use), so a Portal with a missing API address never starts.</summary>
    public static IServiceCollection AddPortalOptions(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<PortalOptions>, PortalOptionsValidator>();
        services.AddOptions<PortalOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                options.ApiBaseUrl = configuration[PortalOptions.ApiBaseUrlKey]?.Trim() ?? string.Empty;
                options.PublicUrl = configuration[PortalOptions.PublicUrlKey]?.Trim() ?? string.Empty;
                options.DefaultProduct = configuration[PortalOptions.DefaultProductKey]?.Trim();
            })
            .ValidateOnStart();
        return services;
    }
}
```

`Program.cs` registers the options; the four pages switch from `@page "..."` to `@attribute [Route(PortalRoutes.XTemplate)]` and `_Imports.razor` imports the routing namespace. (The style guide's inline `onsubmit` handler is a Task 5 finding.)

`src/TechStrap.Portal/Program.cs`

```diff
--- src/TechStrap.Portal/Program.cs
+++ src/TechStrap.Portal/Program.cs
@@ -4,11 +4,11 @@ using TechStrap.Hosting.Security;
 using TechStrap.Hosting.Wiring;
 using TechStrap.Portal.Components;
 using TechStrap.Portal.Components.Ui;
+using TechStrap.Portal.Settings;
 
 const string ServiceName = "techstrap-portal";
 
-// Placeholder shell: this host never touches the database and never migrates. It will call the API
-// through typed clients over TechStrap.Contracts once its UI phase lands.
+// The public portal: static server-side rendering. This host never touches the database and never migrates; it calls the API (TechStrap.Contracts).
 var builder = WebApplication.CreateBuilder(args);
 if (builder.Configuration.ShouldLoadDotEnv(builder.Environment))
 {
@@ -23,6 +23,9 @@ var telemetry = builder.AddTechStrapObservability(ServiceName);
 builder.Services.AddTechStrapWebHost(builder.Configuration, TechStrapCsp.ForBlazorApp(allowLoopbackImages: builder.Environment.IsDevelopment()));
 
 builder.Services.AddRazorComponents();
+
+// The API address, the Portal's public address and the optional default product: validated at start (D-043, D-045).
+builder.Services.AddPortalOptions();
 // Installation-wide switch for the "Powered by TechStrap" footer (D-024); shown unless set to false.
 // A value that is not true or false fails at startup rather than breaking every page.
 builder.Services.AddOptions<PoweredByOptions>()
```

`src/TechStrap.Portal/Components/_Imports.razor`

```diff
--- src/TechStrap.Portal/Components/_Imports.razor
+++ src/TechStrap.Portal/Components/_Imports.razor
@@ -4,3 +4,4 @@
 @using TechStrap.Portal.Components
 @using TechStrap.Portal.Components.Layout
 @using TechStrap.Portal.Components.Ui
+@using TechStrap.Portal.Routing
```

`src/TechStrap.Portal/Components/Pages/Home.razor`

```diff
--- src/TechStrap.Portal/Components/Pages/Home.razor
+++ src/TechStrap.Portal/Components/Pages/Home.razor
@@ -1,4 +1,4 @@
-@page "/"
+@attribute [Route(PortalRoutes.HomeTemplate)]
 
 <PageTitle>TechStrap Portal</PageTitle>
 <section class="shell-placeholder">
```

`src/TechStrap.Portal/Components/Pages/NotFound.razor`

```diff
--- src/TechStrap.Portal/Components/Pages/NotFound.razor
+++ src/TechStrap.Portal/Components/Pages/NotFound.razor
@@ -1,4 +1,4 @@
-@page "/not-found"
+@attribute [Route(PortalRoutes.NotFoundTemplate)]
 @layout PortalLayout
 
 <PageTitle>Not found</PageTitle>
```

`src/TechStrap.Portal/Components/Pages/Error.razor`

```diff
--- src/TechStrap.Portal/Components/Pages/Error.razor
+++ src/TechStrap.Portal/Components/Pages/Error.razor
@@ -1,4 +1,4 @@
-@page "/error"
+@attribute [Route(PortalRoutes.ErrorTemplate)]
 @layout PortalLayout
 
 <PageTitle>Error</PageTitle>
```

`src/TechStrap.Portal/Components/Pages/StyleGuide.razor`

```diff
--- src/TechStrap.Portal/Components/Pages/StyleGuide.razor
+++ src/TechStrap.Portal/Components/Pages/StyleGuide.razor
@@ -1,4 +1,4 @@
-@page "/_styleguide"
+@attribute [Route(PortalRoutes.StyleGuideTemplate)]
 
 <DevelopmentOnly>
     <PageTitle>TechStrap portal style guide</PageTitle>
```

- [ ] **Step 6: Implement the D-043 edits and the compose values**

Three keys, four edits each. `appsettings.json` carries blank strings for the two required or optional strings; the Portal's `.env.example` points `API__BASEURL` at a local API and leaves the others blank; the deploy template lists the operator's keys only (compose owns `API__BASEURL`); the local compose sets the container's two values; the deploy compose sets `API__BASEURL`.

`src/TechStrap.Portal/appsettings.json`

```diff
--- src/TechStrap.Portal/appsettings.json
+++ src/TechStrap.Portal/appsettings.json
@@ -1,4 +1,9 @@
 {
+  "Api": {
+    "BaseUrl": ""
+  },
+  "TECHSTRAP_PORTAL_PUBLIC_URL": "",
+  "TECHSTRAP_PORTAL_DEFAULT_PRODUCT": "",
   "TECHSTRAP_PORTAL_SHOW_POWERED_BY": "true",
   "DataProtection": {
     "KeyRingPath": ""
```

`src/TechStrap.Portal/.env.example`

```diff
--- src/TechStrap.Portal/.env.example
+++ src/TechStrap.Portal/.env.example
@@ -2,7 +2,20 @@
 # In containers, the same names arrive as real environment variables; see docker-compose.yml (local) and deploy/docker-compose.yml.
 # Use SECTION__KEY naming for ASP.NET Core config binding (keys are case-insensitive; ALL_CAPS by convention).
 # Keep this file in sync with appsettings.json and deploy/.env.portal.example: scripts/tests/ConfigContract.Tests.ps1 checks all three.
-# The Portal never touches the database; it calls the API. It reads no API address and no public URL yet: PHASE-09 adds API__BASEURL and TECHSTRAP_PORTAL_PUBLIC_URL here.
+# The Portal never touches the database; it calls the API (D-045).
+
+# --- TechStrap API (required) ---
+# The address the Portal reaches the API at: an absolute http or https URL. Compose sets http://api/ for the container; run outside compose, point it at your API.
+API__BASEURL=http://localhost:8080/
+
+# --- Portal address ---
+# The Portal's public address as customers see it: absolute http or https, no query or fragment. Required outside Development (the Production start fails without it); it is also the base of
+# canonical URLs and the sitemap. Use the same value as TECHSTRAP_PORTAL_PUBLIC_URL in the Api. Example: http://localhost:8082
+TECHSTRAP_PORTAL_PUBLIC_URL=
+
+# --- Default product (optional) ---
+# A product key (lowercase letters, digits and hyphens). When set, the Portal's root page (/) redirects to /p/<key>; blank shows a neutral page with no product list.
+TECHSTRAP_PORTAL_DEFAULT_PRODUCT=
 
 # --- "Powered by TechStrap" mark (D-024) ---
 # Links to https://github.com/Syntax-Circus/techstrap and is shown by default. Set to false to hide it on every portal page and customer email (installation-wide).
```

`deploy/.env.portal.example`

```diff
--- deploy/.env.portal.example
+++ deploy/.env.portal.example
@@ -2,12 +2,20 @@
 # (root-owned, mode 0600). deploy/docker-compose.yml loads it as this container's env_file (format raw: no quotes, no $ interpolation).
 # No inline comments: with raw format, `# ...` after a value becomes part of the value. No surrounding quotes.
 # Keep this file in sync with src/TechStrap.Portal/appsettings.json and src/TechStrap.Portal/.env.example: scripts/tests/ConfigContract.Tests.ps1 checks all three.
-# Compose owns and overrides ASPNETCORE_ENVIRONMENT, DOTENV__ENABLED, DATAPROTECTION__KEYRINGPATH and TRUSTEDPROXY__TRUSTEDNETWORKS__0 (REVERSE_PROXY_CIDR),
-# so they are not listed here. The Portal never touches the database and reads no Api address or public URL yet: PHASE-09 adds API__BASEURL and TECHSTRAP_PORTAL_PUBLIC_URL.
+# Compose owns and overrides ASPNETCORE_ENVIRONMENT, DOTENV__ENABLED, API__BASEURL, DATAPROTECTION__KEYRINGPATH and TRUSTEDPROXY__TRUSTEDNETWORKS__0 (REVERSE_PROXY_CIDR),
+# so they are not listed here. The Portal never touches the database; it calls the Api.
 # A blank required value stops the container at start and its log names the missing key. Never commit a filled copy.
 # A commented-out key (# KEY=) is optional or compose-supplied; an array element (__0) is never left blank, because a blank element still counts as configured.
 
 
+# -- Customer portal address [Api, Admin, Portal] --
+# Required in the Portal: the Portal's public address as customers see it (the same value as in .env.api). Absolute http or https, no query or fragment. It is also the base of canonical URLs and the sitemap.
+TECHSTRAP_PORTAL_PUBLIC_URL=
+
+# -- Default product [Portal] --
+# Optional: a product key. When set, the Portal's root page redirects to /p/<key>; blank shows a neutral page with no product list.
+TECHSTRAP_PORTAL_DEFAULT_PRODUCT=
+
 # -- "Powered by TechStrap" mark [Worker, Portal] (D-024) --
 # Links to https://github.com/Syntax-Circus/techstrap and is shown by default. Set to false to hide it on every portal page and customer email (installation-wide).
 TECHSTRAP_PORTAL_SHOW_POWERED_BY=true
```

`docker-compose.yml`

```diff
--- docker-compose.yml
+++ docker-compose.yml
@@ -109,8 +109,10 @@ services:
         required: false
     environment:
       ASPNETCORE_ENVIRONMENT: Development
+      Api__BaseUrl: http://api/
+      # The Portal's own address, as the browser reaches it (the local port below); the base of canonical URLs and the sitemap.
+      TECHSTRAP_PORTAL_PUBLIC_URL: http://localhost:8082
       # Portal trusts only the reverse proxy. 192.0.2.0/24 is a placeholder; set REVERSE_PROXY_CIDR.
-      # The Portal reads no Api address or public URL yet; PHASE-09 adds Api__BaseUrl and TECHSTRAP_PORTAL_PUBLIC_URL here.
       TRUSTEDPROXY__TRUSTEDNETWORKS__0: ${REVERSE_PROXY_CIDR:-192.0.2.0/24}
       DataProtection__KeyRingPath: /app/dataprotection-keys
     depends_on:
```

`deploy/docker-compose.yml`

```diff
--- deploy/docker-compose.yml
+++ deploy/docker-compose.yml
@@ -89,6 +89,7 @@ services:
     environment:
       ASPNETCORE_ENVIRONMENT: Production
       DOTENV__ENABLED: "false"
+      API__BASEURL: http://api/
       DATAPROTECTION__KEYRINGPATH: /app/dataprotection-keys
       # Portal trusts only the reverse proxy.
       TRUSTEDPROXY__TRUSTEDNETWORKS__0: ${REVERSE_PROXY_CIDR:?set REVERSE_PROXY_CIDR to the reverse proxy address, for example 172.16.31.1/32}
```

- [ ] **Step 7: Record D-045 and correct the docs**

Save the decision text as `d045.md` and the script as `docs_task1.py` in `$T` (outside the repository), then run the script from the repository root. It adds the decision-log bullet, index row and the D-045 section, a "Corrections" block in the PHASE-09 spec (where this page and D-045 differ, D-045 wins), the package-map row, and the roadmap and discovery rows "In progress (09a)".

`d045.md`

```markdown
## D-045: PHASE-09: the public portal (delivery, KB suggestions, API additions, theming, client, headers, SEO)

- **Status:** Approved (owner 2026-10-05; technical rulings at PHASE-09a plan review)
- **Date:** 2026-10-05
- **Owner:** Jon Seeley
- **Related artifacts:** D-002, D-017, D-019, D-024, D-032, D-038, D-040, D-042, D-043, D-044, `docs/architecture/PHASE-09-public-portal.md`, `docs/architecture/UX-BRIEF-portal.md`, `docs/superpowers/plans/2026-10-05-phase-09a-portal-foundation.md`

### Context
PHASE-02, PHASE-06 and PHASE-08 are merged, so PHASE-09 can start. Reading the code before planning found these gaps between the spec and what exists:
- **No public product list.** The root page, the sitemap and any product chooser need to enumerate active products, and the API only answers one key at a time.
- **No list of a category's articles.** `GET api/public/kb/{productKey}/search` returns an empty page for a blank query, so a category page cannot be built.
- **`CustomerTicketDto` has no product.** The ticket page at `/t/{token}` has no product key in its route, so it cannot be themed.
- **The deflection island loses the visitor's IP.** `AddForwardedClientIp()` does nothing without an `HttpContext`, and an InteractiveServer circuit has none, so every suggestion search would share one rate-limit bucket (D-019).
- **The package API differs from the spec.** `SyntaxCircus.Blazor.Seo` 0.1.4 has `AddSyntaxCircusSeo`, `UseSyntaxCircusSeo`, `MapSeoSitemap` and `MapSeoRobotsTxt`; the spec's `UseCanonicalHost`, `MapRobotsTxt`, `MapSitemap` and `ISitemapEntryProvider` do not exist, and the sitemap is not cached server side.
- **The shared security-header middleware overwrites a per-page header.** It sets `Referrer-Policy` and the CSP when the response starts, so an endpoint cannot set them itself.
- **The lost-link timing test conflicts with D-038.** D-038 accepts a residual timing difference of tens of milliseconds between a known and an unknown address.
- **There is no way to carry the ticket number to the "received" page** without a cookie or an access token.
- **`BrandingThemeFactory` would duplicate logic.** `PublicProductDto` already carries the derived accent colours and the Portal already has `AccentScope`.

### Decision
**Owner decisions (2026-10-05)**
- **Delivery.** Three pull requests, each with its own branch, plan and review: 09a the foundation, 09b the customer flows, 09c the knowledge base, SEO and polish.
- **KB suggestions are vanilla JS, not an InteractiveServer island.** There is no framework, no Blazor interactivity and no build step. The server renders a `<ts-kb-suggestions>` custom element with fallback markup inside it (a help-centre search link). A module in `wwwroot/js` defines the element: `connectedCallback` attaches a debounced listener to the subject field and fetches suggestions, and `disconnectedCallback` removes it and aborts any request in flight. It fetches from a Portal-hosted `GET /p/{key}/kb/suggest?q=` adapter, which forwards the real client IP to the API's public search. The adapter is exempt like D-017 (no workflow) and returns plain-text JSON that the module renders with `textContent`. There is no SignalR, no WebAssembly and no CSP change.
- **Two small public API additions** (anonymous, the Public rate limit, the same cache headers as D-044), made in 09c: a paged list of a category's articles (published only, the product's plus shared, newest updated first), and `GET api/public/products` returning the active products (key and display name only), for the sitemap.
- **`/` redirects to `TECHSTRAP_PORTAL_DEFAULT_PRODUCT` when it is set.** Otherwise it shows a neutral page with no product list, so nothing can be enumerated.
- **Ticket theming.** `CustomerTicketDto` gains `ProductKey` (additive), in 09b. `/t/{token}` loads that product's branding. `CustomerDtoShapeTests` is updated.
- **T20 (Playwright end-to-end) is deferred.** It was optional in the spec and stays out of all three pull requests.

**Technical rulings (proposed in the 09a plan; approved when the owner approves it)**
- **Portal API client.** The Portal gets its own small `ApiConnection` and `ProblemMapping`, modelled on the Admin's. It does not share code with the Admin: Hosting stays a leaf and the Portal references exactly Contracts and Hosting. A read client retries transport errors, 408 and 502 to 504 and honours `Retry-After` up to 2 seconds; a write client never retries. Both use `AddForwardedClientIp()`, `RemoveAllLoggers()` and a 5-minute pooled connection lifetime. `ProblemMapping` maps 400 (field errors), 404, 413 and 415 (the attachment errors), 429 (rate limited) and 5xx or a transport error (`api-unavailable`) explicitly. A customer call sets `X-Ticket-Token` on the request itself; the value is never logged and never put in a URL.
- **The ticket and KB clients arrive with their pages.** 09a ships `IPublicProductClient` and the token capability of `ApiConnection`; `IPublicTicketClient` and `ICustomerTicketClient` come with 09b and `IPublicKbClient` with 09c (YAGNI).
- **`SyntaxCircus.Blazor.Seo` 0.1.4, with its real API.** `AddSyntaxCircusSeo(config)` and `UseSyntaxCircusSeo()`; `MapSeoRobotsTxt(extraDirectives: ["Disallow: /t/"])`; in 09c `MapSeoSitemap(static, provider)`, where the provider is backed by the Portal's own 15-minute cache. The article JSON-LD is a local POCO. 03-PACKAGE-MAP and the PHASE-09 spec use the real names.
- **One key for the public address.** `Seo:BaseUrl` is derived in code from `TECHSTRAP_PORTAL_PUBLIC_URL` (a post-configure step), so there is one setting, required outside Development. `CanonicalHost__*` stay their own optional keys (blank means no redirect), because the package's redirect is an allow-list of legacy hosts and a Portal with one address needs none. The sitemap is not mapped in 09a: it needs the 09c products endpoint.
- **Per-path headers.** `UseTechStrapWebHost` accepts per-path rules (a path predicate and header overrides) that run after the shared security headers, so they win. `/t/*` gets `Referrer-Policy: no-referrer`, `Cache-Control: no-store` and `X-Robots-Tag: noindex`; the sandbox CSP applies only to `/t/{token}/attachments/{id}`, so the ticket page keeps the normal CSP. The Admin's download prefix is expressed as one such rule, with unchanged behaviour.
- **Lost link.** The Portal's responses are byte-identical whatever the address. Timing is out of scope: D-038 accepts the residual difference, and the spec's timing test is relaxed to the byte comparison.
- **The "received" page.** The ticket number travels in a data-protection-protected `?ref=` value that expires after 10 minutes. There is no cookie and no access token. An expired or tampered value shows the generic confirmation.
- **Theming.** There is no `BrandingThemeFactory`. A thin `ProductThemeViewModel` reuses `AccentScope` and the DTO's derived colours, and re-checks the logo address (https only, or loopback http in Development, matching `TechStrapCsp.ForBlazorApp`); an unacceptable logo is omitted. NotFound and Error stay neutral (no enumeration, and the PHASE-04 no-brand guard): an unknown, inactive or malformed product key is answered exactly like an unknown route.
- **Shared KB articles.** The canonical URL is the product path the visitor is on, because each product's help centre is its own site. The sitemap lists a shared article under each product.
- **Forms.** Static-SSR forms use `[SupplyParameterFromForm]` and antiforgery. Attachments use a plain `<input type="file" multiple>`, which works without script, and are streamed into the multipart request. The Portal enforces the request size limit with Contracts `IntakeLimits`.
### Alternatives Considered
- **Keep the InteractiveServer island.** Rejected by the owner: it adds a public SignalR circuit, loses the visitor's IP and needs a custom handler to work around it.
- **A configured list of products for the root page and sitemap.** Rejected: it diverges from "active products" and needs a redeploy to change.
- **Share `ApiConnection` and `ProblemMapping` with the Admin through Hosting.** Rejected: it gives Hosting a Contracts and Common reference, and the Admin's version carries a session-expiry concern the Portal does not have.
- **A `BrandingThemeFactory`.** Rejected: `ProductAccent` is the single implementation of the accent rule and the DTO already carries its output.
- **A separate `Seo__BaseUrl` key.** Rejected: two keys for one value would drift.
- **A cookie for the "received" page.** Rejected: a protected, short-lived query value needs no state and no consent notice.

### Consequences
- **The Portal needs two settings in Production.** `API__BASEURL` (set by the deploy compose) and `TECHSTRAP_PORTAL_PUBLIC_URL` (the operator's). The Portal refuses to start without them and names the key.
- **The PHASE-09 spec text was corrected where it named things that do not exist.** The Seo names, `BrandingThemeFactory`, the lost-link timing test and "Portal references Contracts only" (it is Contracts plus Hosting).
- **A malformed or unknown product key is a 404 without calling the API.** Only a well-formed key is sent on.
- **A category named `suggest` would be unreachable.** The Portal serves `/p/{key}/kb/suggest` (09b) beside `/p/{key}/kb/{category}`, and the API reserves only the slug `search`; 09b decides whether to reserve `suggest` too.
- **09a leaves the sitemap unmapped.** `/robots.txt` is served and disallows `/t/`; its `Sitemap:` line points at `/sitemap.xml`, which answers 404 until 09c.

### Approval
- **Approved by:** Jon Seeley (owner, PHASE-09 planning)
- **Approved on:** 2026-10-05
```

`docs_task1.py`

```python
"""Task 1 docs edits for PHASE-09a. Run from the repository root: python docs_task1.py <path to d045.md>."""
import sys


def sub(path, old, new):
    text = open(path, encoding="utf-8", newline="").read()
    newline = "\r\n" if "\r\n" in text else "\n"
    old = old.replace("\n", newline)
    new = new.replace("\n", newline)
    assert text.count(old) == 1, (path, text.count(old), old[:70])
    open(path, "w", encoding="utf-8", newline="").write(text.replace(old, new))


def append(path, addition):
    text = open(path, encoding="utf-8", newline="").read()
    newline = "\r\n" if "\r\n" in text else "\n"
    if not text.endswith(newline):
        text += newline
    open(path, "w", encoding="utf-8", newline="").write(text + newline + addition.replace("\n", newline))


d045 = open(sys.argv[1], encoding="utf-8").read().rstrip("\n") + "\n"
log = "docs/architecture/04-DECISION-LOG.md"

# 1. Header bullet, index row and the decision itself.
sub(
    log,
    "- **Owner decision (2026-10-02, PHASE-02):** D-023 (visual direction)",
    "- **Owner decision (2026-10-05, PHASE-09 planning):** D-045, the owner decisions on the three-PR split, vanilla-JS KB suggestions, the two small public API additions, the root page and ticket theming. Its technical rulings were proposed in the PHASE-09a plan and approved when the owner approved the plan.\n"
    "- **Owner decision (2026-10-02, PHASE-02):** D-023 (visual direction)",
)
text = open(log, encoding="utf-8", newline="").read()
marker = "| D-044 |"
start = text.index(marker)
end = text.index("\n", start) + 1
row = (
    "| D-045 | PHASE-09: three pull requests, vanilla-JS KB suggestions, two small public API additions, a default-product root page, per-product theming from the DTO, a Portal API client, per-path headers, the real Blazor.Seo API | "
    "Approved (owner 2026-10-05; technical rulings at PHASE-09a plan review) | 2026-10-05 | PHASE-09, PHASE-12 |\n"
)
if "\r\n" in text:
    row = row.replace("\n", "\r\n")
open(log, "w", encoding="utf-8", newline="").write(text[:end] + row + text[end:])
append(log, d045)

# 2. The spec: one corrections block, before the boundaries table.
sub(
    "docs/architecture/PHASE-09-public-portal.md",
    "## Application Boundaries\n",
    """### Corrections (D-045, 2026-10-05)

Where this page and D-045 differ, D-045 wins.
- **Delivery.** Three pull requests: 09a (the foundation: T01 to T05, T17, T19, T22), 09b (the customer flows: T06 to T11, T18, T21, T23) and 09c (the knowledge base, SEO and polish: T12 to T16). T20 is deferred.
- **References.** The Portal references `TechStrap.Contracts` and `TechStrap.Hosting` (the shared host wiring, D-042), not Contracts only.
- **KB suggestions.** A vanilla-JS custom element `<ts-kb-suggestions>` and a Portal-hosted `GET /p/{key}/kb/suggest` adapter replace the `KbDeflectionSuggestions` InteractiveServer island. No page has a circuit.
- **API additions.** 09c adds a paged list of a category's articles and `GET api/public/products`; 09b adds `ProductKey` to `CustomerTicketDto`. `/` redirects to `TECHSTRAP_PORTAL_DEFAULT_PRODUCT`, or shows a neutral page.
- **Blazor.Seo.** The real names are `AddSyntaxCircusSeo`, `UseSyntaxCircusSeo`, `MapSeoRobotsTxt(extraDirectives)` and `MapSeoSitemap(staticEntries, provider)`; `UseCanonicalHost`, `MapRobotsTxt`, `MapSitemap` and `ISitemapEntryProvider` do not exist. `Seo:BaseUrl` is derived from `TECHSTRAP_PORTAL_PUBLIC_URL`.
- **Theming.** `BrandingThemeFactory` is replaced by a thin `ProductThemeViewModel` over `AccentScope` and the DTO's derived colours; the logo address is re-checked.
- **Clients.** A hand-written `ApiConnection` and `ProblemMapping` (reads retried, writes never), not `ApiClientBase`; each client arrives with its page.
- **Lost link.** The Portal's responses are byte-identical whatever the address; the timing assertion in P09-T10 is dropped (D-038 accepts the residual difference).
- **Received page.** The ticket number travels in a data-protection-protected `?ref=` value that expires after 10 minutes.
- **Headers.** Per-path header rules in `UseTechStrapWebHost` give `/t/*` its `no-referrer`, `no-store` and `noindex`, and give only `/t/{token}/attachments/{id}` the sandbox CSP.

## Application Boundaries
""",
)

# 3. Package map, roadmap and discovery index.
sub(
    "docs/architecture/03-PACKAGE-MAP.md",
    "Meta tags, canonical, Open Graph and sitemap support for portal KB pages (`GetSitemapEntriesRequestHandler`).",
    "Meta tags, canonical, Open Graph, robots.txt and sitemap support for portal pages (`AddSyntaxCircusSeo`, `UseSyntaxCircusSeo`, `MapSeoRobotsTxt`, `MapSeoSitemap`; D-045).",
)
sub(
    "docs/architecture/99-IMPLEMENTATION-ROADMAP.md",
    "| D-002, D-017, D-019 | Not started |",
    "| D-002, D-017, D-019, D-045 | In progress (09a) |",
)
sub(
    "docs/architecture/00-DISCOVERY-INDEX.md",
    "| 09 | [Public portal](PHASE-09-public-portal.md) | 02, 06, 08 | 12 | Not started |",
    "| 09 | [Public portal](PHASE-09-public-portal.md) | 02, 06, 08 | 12 | In progress (09a) |",
)
```

Run: `python $T/docs_task1.py $T/d045.md`
Expected: no output.

- [ ] **Step 8: Run the tests to see them pass**

Run: `dotnet build tests/TechStrap.Portal.Tests -c Release`
Expected: 0 warnings, 0 errors.

Run: `dotnet test --project tests/TechStrap.Portal.Tests -c Release`
Expected: PASS: `total: 136, failed: 0` (62 before this task).

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release`
Expected: PASS: `total: 831, failed: 0` (the Portal cases of the existing Api.Tests host classes now start with the API address in `HostFactory`).

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal`
Expected: PASS: `Tests Passed: 288, Failed: 0`.

- [ ] **Step 9: Prove each pin with a mutation**

Run `git add -A` first. Every row is run from the repository root with the tool from Step 1; "Expected" is the result of the run.

| # | File | Replace | With | Command (after `--`) | Expected |
| --- | --- | --- | --- | --- | --- |
| 1a | `src/TechStrap.Portal/Settings/PortalOptionsValidator.cs` | `if (!environment.IsDevelopment())` | `if (!environment.IsDevelopment() && environment.IsProduction() && false)` | `dotnet test --project tests/TechStrap.Portal.Tests -c Release --filter-query "/*/*/PortalOptionsValidatorTests/*"` | KILLED, 5 failed of 40 (the public URL is required outside Development) |
| 1b | the same | `if (!environment.IsDevelopment())` | `if (environment is not null)` | the same | KILLED, 1 failed (blank is valid in Development) |
| 2 | `src/TechStrap.Portal/Settings/PortalOptions.cs` | `PublicUrl.Trim().TrimEnd('/')` | `PublicUrl.Trim()` | the same | KILLED, 1 failed |
| 3 | `src/TechStrap.Portal/Routing/ProductKeyShape.cs` | `\A[a-z0-9]+(?:-[a-z0-9]+)*\z` | `\A[a-z0-9-]+\z` | `... --filter-query "/*/*/ProductKeyShapeTests/*"` | KILLED, 3 failed of 20 |
| 4 | `src/TechStrap.Portal/Routing/PortalRoutes.cs` | `private static string Escape(string value) => Uri.EscapeDataString(value);` | `private static string Escape(string value) => value;` | `... --filter-query "/*/*/PortalRoutesTests/*"` | KILLED, 1 failed of 5 |
| 5 | `src/TechStrap.Portal/Components/Pages/Home.razor` | `@attribute [Route(PortalRoutes.HomeTemplate)]` | `@page "/"` | `... --filter-query "/*/*/RouteLiteralTests/*"` | KILLED, 1 failed of 3 |
| 6 | `src/TechStrap.Portal/Components/Pages/Error.razor` | `<a href="/">Back to the start</a>` | `<a href="/p/x">Back to the start</a>` | the same | KILLED, 1 failed |
| 7 | `deploy/docker-compose.yml` | `      API__BASEURL: http://api/\n      DATAPROTECTION__KEYRINGPATH` | `      DATAPROTECTION__KEYRINGPATH` | `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ConfigContract.Tests.ps1 -Output Minimal` | KILLED, 1 failed (compose owns the Portal's API address) |
| 8 | `src/TechStrap.Portal/.env.example` | `API__BASEURL=http://localhost:8080/` | `API__BASEURL=` | `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/*/DevelopmentStartupTests/*"` | KILLED, 1 failed (a developer's copied example must start) |
| 9a | `docs/architecture/04-DECISION-LOG.md` | `(YAGNI)` | `(lazy)` | `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/RepositoryDocs.Tests.ps1 -Output Minimal` | KILLED, 1 failed |
| 9b | `docs/architecture/99-IMPLEMENTATION-ROADMAP.md` | `D-045 \| In progress (09a) \|` | `D-045 \| Not started \|` | the same | KILLED, 1 failed |

Example (row 2), from the repository root:

```bash
python $T/mut.py src/TechStrap.Portal/Settings/PortalOptions.cs --replace "PublicUrl.Trim().TrimEnd('/')" "PublicUrl.Trim()" -- dotnet test --project tests/TechStrap.Portal.Tests -c Release --filter-query "/*/*/PortalOptionsValidatorTests/*"
```

Every row but 1a, 1b, 8 and 9 needs the same call shape; a multi-line replacement writes `\n` for the newline.

- [ ] **Step 10: Verify and commit**

Run: `dotnet build TechStrap.slnx -c Release --no-incremental`
Expected: `0 Warning(s)`, `0 Error(s)`.

```bash
git status --short
git add -A src tests scripts docs deploy docker-compose.yml
git diff --cached --stat
git commit -F - <<'EOF'
feat(portal): validated settings, route constants and the D-045 record (PHASE-09a)

Adds API__BASEURL, TECHSTRAP_PORTAL_PUBLIC_URL and TECHSTRAP_PORTAL_DEFAULT_PRODUCT with their
D-043 edits and the compose values, PortalRoutes and ProductKeyShape, the Portal's host test
settings, D-045 and the spec corrections.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
EOF
```

The commit must not contain `.superpowers/`, `bin/` or `obj/` (they are git-ignored); check `git diff --cached --stat` first.
### Task 2: The API connection, the product client and the fake-API test harness

**Review Focus pin:** Review Focus 1 (token leakage) and Review Focus 4 (real client IP; a write is never retried). Pinned here by `ApiConnectionTests` (reads retried, writes never; the visitor's address on both clients; the token header on one request and never in a URL or body), `ForwardedClientIpHostTests` (behind a trusted proxy the API sees the visitor, an untrusted peer's header is ignored), `TicketTokenTests` (the shape and the `[token]` print) and `TicketTokenLeakTests` (no log event holds the token at Verbose, the clients write no event of their own, and a negative control proves the scan can see a header value).

**Files:**

- Create: `src/TechStrap.Portal/Clients/ApiClientNames.cs`
- Create: `src/TechStrap.Portal/Clients/ApiClientRegistration.cs`
- Create: `src/TechStrap.Portal/Clients/ApiConnection.cs`
- Create: `src/TechStrap.Portal/Clients/ApiErrorCodes.cs`
- Create: `src/TechStrap.Portal/Clients/IPublicProductClient.cs`
- Create: `src/TechStrap.Portal/Clients/ProblemCopy.cs`
- Create: `src/TechStrap.Portal/Clients/ProblemMapping.cs`
- Create: `src/TechStrap.Portal/Clients/PublicProductClient.cs`
- Create: `src/TechStrap.Portal/Clients/TicketToken.cs`
- Modify: `src/TechStrap.Portal/Program.cs`
- Modify: `src/TechStrap.Portal/TechStrap.Portal.csproj`
- Test (create): `tests/TechStrap.Portal.Tests/Api/ApiHarness.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Api/PortalTestApi.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Api/ProxyHopStartupFilter.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Api/StubApiHandler.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Clients/ApiConnectionTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Clients/ForwardedClientIpHostTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Clients/ProblemMappingTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Clients/PublicProductClientTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Clients/ReadRetryAfterTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Clients/TicketTokenLeakTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Clients/TicketTokenTests.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/PortalFactory.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/TechStrap.Portal.Tests.csproj`


**Interfaces:**
- Consumes: `PortalOptions.ApiBaseUri` and `AddPortalOptions` (Task 1), `ProductKeyShape.IsWellFormed` (Task 1), `PortalFactory` (Task 1), Contracts `PublicProductDto` and `HeaderNames.TicketToken`, `SyntaxCircus.Common` `Result`, `Result<T>`, `ResultError`, `ResultErrorKind`, `SyntaxCircus.AspNetCore.Common` `AddForwardedClientIp()`, `Microsoft.Extensions.Http.Resilience` (through `SyntaxCircus.Http.Resilience`).
- Produces:
  - `TechStrap.Portal.Clients.ApiClientNames` (public static): `Read = "techstrap-portal-api-read"`, `Write = "techstrap-portal-api-write"`.
  - `ApiClientRegistration.AddPortalApiClients(this IServiceCollection)`: registers both named clients (no logging handlers, `AddForwardedClientIp()`, a `SocketsHttpHandler` with a 5-minute `PooledConnectionLifetime`; only the read client has the retry), the scoped `ApiConnection` and the scoped `IPublicProductClient`. Constants `ReadRetryCount = 2`, `ReadTimeoutSeconds = 30`, `WriteTimeoutSeconds = 300`, `ReadRetryAfterCap = 2 s`; `internal static TimeSpan? RetryAfterDelay(HttpResponseMessage?, DateTimeOffset)`.
  - `ApiConnection` (internal sealed, scoped, ctor `(IHttpClientFactory)`): `GetAsync<T>(string uri, CancellationToken)`, `GetAsync<T>(string uri, TicketToken token, CancellationToken)`, `SendAsync<T>(HttpMethod, string uri, object? body, [TicketToken token,] CancellationToken)`, `SendAsync(HttpMethod, string uri, object? body, [TicketToken token,] CancellationToken)` (no body in the answer), `SendContentAsync<T>(HttpMethod, string uri, HttpContent, [TicketToken token,] CancellationToken)`; all return `Result<T>` or `Result`; cancellation by the caller propagates and is never a Result; `internal static ResultError? Transport(Exception, CancellationToken)`.
  - `TicketToken` (public readonly struct): `Length = 43`, `static bool TryParse(string?, out TicketToken)` (`\A[A-Za-z0-9_\-]{43}\z`), `Value` (throws for `default`), `ToString()` is always `"[token]"`.
  - `ProblemMapping` (internal static): `Map(HttpStatusCode, string?) : IReadOnlyList<ResultError>`, `NotFound()`, `Unavailable()`, `Unexpected()`. `ApiErrorCodes` (`validation-failed`, `not-found`, `payload-too-large`, `unsupported-media-type`, `rate-limited`, `api-unavailable`, `api-unexpected-response`, `api-error`) and `ProblemCopy` (the fixed sentences).
  - `IPublicProductClient.GetAsync(string key, CancellationToken) : Task<Result<PublicProductDto>>`: a key that is not a slug is the not-found error without any call.
  - Test harness (`TechStrap.Portal.Tests`): `StubApiHandler` and `StubApiRequest(Method, Path, Query, ForwardedFor, TicketToken, ContentType, Body)`; `PortalTestApi.AddStubApi(this IServiceCollection, StubApiHandler)` and `AssertEveryCallBore(this StubApiHandler, string clientIp)`; `ApiHarness.Create(string? clientIp = "203.0.113.9", TimeProvider? time = null)` (an `IDisposable` with `Stub`, `Get<T>()`); `ProxyHopStartupFilter.Add(string proxyAddress = "192.0.2.10")` and `ProbePath`; `PortalFactory.Api` (a `StubApiHandler`), `PortalFactory.LogSink` (a `CollectingSink`) and `PortalFactory.VerboseLogging`.

- [ ] **Step 1: Write the failing tests and the harness**

The fake-API harness is the Portal's own copy of the Admin's: the Admin's stub speaks the Admin's DTOs and bearer token. It replaces the primary handler of both named clients, so the real forwarded-IP and retry handlers still run, and it records the two headers the Portal is responsible for. `PortalFactory` gets the stub, a log sink and a Verbose settings dictionary.

`tests/TechStrap.Portal.Tests/Api/StubApiHandler.cs`

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TechStrap.Contracts.Http;

namespace TechStrap.Portal.Tests.Api;

/// <summary>One request the Portal sent to the stub API.</summary>
public sealed record StubApiRequest(HttpMethod Method, string Path, string Query, string? ForwardedFor, string? TicketToken, string? ContentType, string? Body);

/// <summary>
/// Stands in for the TechStrap API behind the Portal's named HTTP clients (it replaces their primary handler, so the real handler pipeline above it still runs: the forwarded-IP handler
/// adds <c>X-Forwarded-For</c>, the read client's retry handler retries). It records every request, with the two headers the Portal is responsible for, and answers by method and path; an
/// unconfigured request answers 404 with the problem code "stub-not-configured" so a test fails loudly. The Portal's own copy: the Admin's stub speaks the Admin's DTOs and bearer token.
/// </summary>
public sealed class StubApiHandler : HttpMessageHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly List<StubApiRequest> _requests = [];
    private readonly List<(HttpMethod Method, string Path, Func<StubApiRequest, HttpResponseMessage> Respond)> _routes = [];
    private readonly object _gate = new();

    /// <summary>Every request received so far, in order.</summary>
    public IReadOnlyList<StubApiRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>How many requests with this method and path (any query) were received.</summary>
    public int Count(HttpMethod method, string path) => Requests.Count(r => r.Method == method && string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>Answers requests for <paramref name="path"/> (path only, no query) with whatever <paramref name="respond"/> returns. A later route for the same method and path replaces an earlier one.</summary>
    public StubApiHandler On(HttpMethod method, string path, Func<StubApiRequest, HttpResponseMessage> respond)
    {
        lock (_gate)
        {
            _routes.RemoveAll(r => r.Method == method && string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
            _routes.Add((method, path, respond));
        }

        return this;
    }

    public StubApiHandler OnJson<T>(HttpMethod method, string path, T body, HttpStatusCode status = HttpStatusCode.OK) => On(method, path, _ => JsonResponse(status, body));

    public StubApiHandler OnStatus(HttpMethod method, string path, HttpStatusCode status) => On(method, path, _ => new HttpResponseMessage(status));

    /// <summary>An RFC 7807 answer in the shape the API produces: <c>type</c> is the error code, <c>detail</c> the message.</summary>
    public StubApiHandler OnProblem(HttpMethod method, string path, HttpStatusCode status, string type, string detail) => On(method, path, _ => Problem(status, type, detail));

    public static HttpResponseMessage JsonResponse<T>(HttpStatusCode status, T body) => new(status) { Content = JsonContent.Create(body, options: Json) };

    public static HttpResponseMessage Problem(HttpStatusCode status, string type, string detail) => new(status)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { type, title = status.ToString(), status = (int)status, detail }, Json),
            Encoding.UTF8,
            "application/problem+json"),
    };

    /// <summary>
    /// A 400 in the shape the API produces for a validation failure: <c>type</c> "validation-failed", the message per field in <c>errors</c> and the specific code per field in
    /// <c>errorCodes</c>. <paramref name="target"/> is the field exactly as the API sends it, kebab-case; an empty target is a form-level error.
    /// </summary>
    public static HttpResponseMessage ValidationProblem(string target, string code, string message) => ValidationProblem([(target, code, message)]);

    public static HttpResponseMessage ValidationProblem(IReadOnlyList<(string Target, string Code, string Message)> errors)
    {
        var messages = errors.GroupBy(e => e.Target).ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray());
        var codes = errors.GroupBy(e => e.Target).ToDictionary(g => g.Key, g => g.Select(e => e.Code).ToArray());
        return new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(
                    new { type = "validation-failed", title = "One or more validation errors occurred.", status = 400, detail = errors[0].Message, errors = messages, errorCodes = codes },
                    Json),
                Encoding.UTF8,
                "application/problem+json"),
        };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // A real handler gives up on a cancelled token before it sends anything.
        cancellationToken.ThrowIfCancellationRequested();
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var seen = new StubApiRequest(
            request.Method,
            request.RequestUri!.AbsolutePath,
            request.RequestUri.Query,
            Header(request, "X-Forwarded-For"),
            Header(request, HeaderNames.TicketToken),
            request.Content?.Headers.ContentType?.ToString(),
            body);

        Func<StubApiRequest, HttpResponseMessage>? respond;
        lock (_gate)
        {
            _requests.Add(seen);
            respond = _routes.FirstOrDefault(r => r.Method == seen.Method && string.Equals(r.Path, seen.Path, StringComparison.OrdinalIgnoreCase)).Respond;
        }

        return respond is null ? Problem(HttpStatusCode.NotFound, "stub-not-configured", $"{seen.Method} {seen.Path} is not configured in the stub API.") : respond(seen);
    }

    private static string? Header(HttpRequestMessage request, string name) => request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
}
```

`tests/TechStrap.Portal.Tests/Api/PortalTestApi.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Tests.Api;

public static class PortalTestApi
{
    /// <summary>
    /// Makes <paramref name="stub"/> the primary handler of both named API clients. The handlers above it (forwarded client IP, and the read client's retries) are the real ones, so a test
    /// sees what the API would see.
    /// </summary>
    public static IServiceCollection AddStubApi(this IServiceCollection services, StubApiHandler stub)
    {
        services.AddHttpClient(ApiClientNames.Read).ConfigurePrimaryHttpMessageHandler(_ => stub);
        services.AddHttpClient(ApiClientNames.Write).ConfigurePrimaryHttpMessageHandler(_ => stub);
        return services;
    }

    /// <summary>
    /// Fails unless the Portal called the stub at least once and every call carried <c>X-Forwarded-For: {clientIp}</c>: a host test that makes an API call uses this, so a lost forwarded-IP
    /// handler fails the test (Review Focus 4: the API must rate-limit the visitor, not the Portal's container).
    /// </summary>
    public static void AssertEveryCallBore(this StubApiHandler stub, string clientIp)
    {
        var requests = stub.Requests;
        if (requests.Count == 0)
        {
            throw new InvalidOperationException("The Portal made no API call, so there is no X-Forwarded-For header to check.");
        }

        var wrong = requests.FirstOrDefault(r => r.ForwardedFor != clientIp);
        if (wrong is not null)
        {
            throw new InvalidOperationException($"{wrong.Method} {wrong.Path} carried X-Forwarded-For '{wrong.ForwardedFor}', expected the visitor's address {clientIp}.");
        }
    }
}
```

`tests/TechStrap.Portal.Tests/Api/ApiHarness.cs`

```csharp
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Tests.Api;

/// <summary>
/// The Portal's API clients wired exactly as in Program (<c>AddPortalApiClients</c>, so the real forwarded-IP and resilience handlers run) with a <see cref="StubApiHandler"/> as the primary
/// handler and a request-like scope: an <c>HttpContext</c> whose connection address is the visitor's. Inject nothing but the types under test.
/// </summary>
internal sealed class ApiHarness : IDisposable
{
    public const string DefaultClientIp = "203.0.113.9";

    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;

    private ApiHarness(ServiceProvider provider, StubApiHandler stub)
    {
        _provider = provider;
        _scope = provider.CreateAsyncScope();
        Stub = stub;
    }

    public StubApiHandler Stub { get; }

    public IServiceProvider Services => _scope.ServiceProvider;

    public static ApiHarness Create(string? clientIp = DefaultClientIp, TimeProvider? time = null)
    {
        var stub = new StubApiHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        if (time is not null)
        {
            // The resilience pipelines take their clock from the container, so a test can step through the retry delays instead of waiting for them.
            services.AddSingleton(time);
        }

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new PortalOptions { ApiBaseUrl = "http://api.test/", PublicUrl = "https://portal.test" }));
        services.AddPortalApiClients();
        services.AddStubApi(stub);

        var provider = services.BuildServiceProvider();
        var harness = new ApiHarness(provider, stub);
        if (clientIp is not null)
        {
            harness.Services.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { Connection = { RemoteIpAddress = IPAddress.Parse(clientIp) } };
        }

        return harness;
    }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
```

`tests/TechStrap.Portal.Tests/Api/ProxyHopStartupFilter.cs`

```csharp
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Tests.Api;

/// <summary>
/// Puts the host behind a reverse proxy, as in production: the connection's peer is the proxy (an address in the trusted network), so the host's forwarded-headers middleware rewrites the
/// client address from the request's <c>X-Forwarded-For</c>. A probe endpoint after the whole application pipeline then calls the Portal's product client from inside that request, which is
/// what a page does, so a test sees the address the API would see.
/// </summary>
internal sealed class ProxyHopStartupFilter(string proxyAddress) : IStartupFilter
{
    public const string ProbePath = "/__probe/product";

    public static Action<IServiceCollection> Add(string proxyAddress = "192.0.2.10") => services => services.AddSingleton<IStartupFilter>(new ProxyHopStartupFilter(proxyAddress));

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(proxyAddress);
            return nextMiddleware(context);
        });
        next(app);

        // Reached only when no endpoint of the Portal matched the path.
        app.Use(async (context, nextMiddleware) =>
        {
            if (context.Request.Path != ProbePath)
            {
                await nextMiddleware(context);
                return;
            }

            var client = context.RequestServices.GetRequiredService<IPublicProductClient>();
            var result = await client.GetAsync("paperplane", context.RequestAborted);
            context.Response.StatusCode = result.IsSuccess ? StatusCodes.Status200OK : StatusCodes.Status502BadGateway;
            await context.Response.WriteAsync(result.IsSuccess ? "ok" : "failed");
        });
    };
}
```

`tests/TechStrap.Portal.Tests/PortalFactory.cs`

```csharp
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
using TechStrap.Portal.Settings;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests;

/// <summary>Captures every Serilog event the host writes so tests can assert on log lines.</summary>
public sealed class CollectingSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyCollection<LogEvent> Events => _events.ToArray();

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
}

/// <summary>
/// Starts the Portal host in-process in the given environment. A developer's gitignored .env.local must never leak into tests, and Production needs a trusted network to start.
/// The two required settings (the API address and the public URL) get test values; <paramref name="settings"/> is applied on top, so a test can blank one to prove the start fails.
/// A stub API (<see cref="Api"/>) sits behind the Portal's two named HTTP clients, and <see cref="LogSink"/> records every log event.
/// </summary>
internal sealed class PortalFactory(
    string environment = "Development",
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<TechStrap.Portal.Program>
{
    public const string ApiBaseUrl = "http://api.test/";
    public const string PublicUrl = "https://portal.test";

    /// <summary>Every level is captured, including the Trace and Debug lines of System.Net.Http and ASP.NET Core: a secret that only shows at Verbose is still a leak.</summary>
    public static IReadOnlyDictionary<string, string?> VerboseLogging { get; } = new Dictionary<string, string?>
    {
        ["Serilog:MinimumLevel:Default"] = "Verbose",
        ["Serilog:MinimumLevel:Override:Microsoft"] = "Verbose",
        ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore"] = "Verbose",
        ["Serilog:MinimumLevel:Override:System"] = "Verbose",
    };

    static PortalFactory()
    {
        Environment.SetEnvironmentVariable("DotEnv__Enabled", "false");

        // Production refuses to start without trusted proxies, and that option is bound before the factory can override it.
        Environment.SetEnvironmentVariable("TRUSTEDPROXY__TRUSTEDNETWORKS__0", "192.0.2.0/24");
    }

    /// <summary>The stub behind the Portal's API clients. Unconfigured calls answer 404 "stub-not-configured".</summary>
    public StubApiHandler Api { get; } = new();

    public CollectingSink LogSink { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PortalOptions.ApiBaseUrlKey] = ApiBaseUrl,
                [PortalOptions.PublicUrlKey] = PublicUrl,
            });
            configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
        });
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ILogEventSink>(LogSink);
            services.AddStubApi(Api);
            configureServices?.Invoke(services);
        });
    }
}
```

`tests/TechStrap.Portal.Tests/TechStrap.Portal.Tests.csproj`

```diff
--- tests/TechStrap.Portal.Tests/TechStrap.Portal.Tests.csproj
+++ tests/TechStrap.Portal.Tests/TechStrap.Portal.Tests.csproj
@@ -7,6 +7,7 @@
 
   <ItemGroup>
     <PackageReference Include="bunit" />
+    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
     <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
   </ItemGroup>
 
```

The connection tests. They cover success, the retried and never-retried statuses, the visitor's address on both clients, the handler chain (an allow-list: the write chain is exactly the forwarded-IP handler, the read chain adds only the retry handler, and neither has a logging handler), the token header, and a fixed transport message.

`tests/TechStrap.Portal.Tests/Clients/ApiConnectionTests.cs`

```csharp
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using SyntaxCircus.Common;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Clients;

public sealed class ApiConnectionTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly string Token = new('t', TicketToken.Length);

    private static PublicProductDto Product() => new("paperplane", "Paperplane", null, "#F59E0B", "#000000", "#9D6507");

    private static TicketToken ValidToken()
    {
        TicketToken.TryParse(Token, out var token).ShouldBeTrue();
        return token;
    }

    [Fact]
    public async Task A_get_returns_the_json_body_on_success()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, "/api/thing", Product());

        var result = await api.Get<ApiConnection>().GetAsync<PublicProductDto>("api/thing", Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Product());
    }

    [Fact]
    public async Task A_get_is_retried_on_503_and_then_succeeds()
    {
        using var api = ApiHarness.Create();
        var calls = 0;
        api.Stub.On(HttpMethod.Get, "/api/thing", _ => ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : StubApiHandler.JsonResponse(HttpStatusCode.OK, Product()));

        var result = await api.Get<ApiConnection>().GetAsync<PublicProductDto>("api/thing", Ct);

        result.IsSuccess.ShouldBeTrue();
        api.Stub.Count(HttpMethod.Get, "/api/thing").ShouldBe(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task A_get_is_retried_twice_on_a_retryable_status_so_three_calls_in_all(HttpStatusCode status)
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Get, "/api/thing", status);

        var result = await api.Get<ApiConnection>().GetAsync<PublicProductDto>("api/thing", Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(status == HttpStatusCode.RequestTimeout ? ApiErrorCodes.ApiError : ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(HttpMethod.Get, "/api/thing").ShouldBe(1 + ApiClientRegistration.ReadRetryCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task A_get_is_not_retried_on_any_other_status(HttpStatusCode status)
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Get, "/api/thing", status);

        await api.Get<ApiConnection>().GetAsync<PublicProductDto>("api/thing", Ct);

        api.Stub.Count(HttpMethod.Get, "/api/thing").ShouldBe(1);
    }

    [Fact]
    public async Task A_get_is_retried_on_a_transport_failure()
    {
        using var api = ApiHarness.Create();
        var calls = 0;
        api.Stub.On(HttpMethod.Get, "/api/thing", _ => ++calls == 1 ? throw new HttpRequestException("connection refused") : StubApiHandler.JsonResponse(HttpStatusCode.OK, Product()));

        var result = await api.Get<ApiConnection>().GetAsync<PublicProductDto>("api/thing", Ct);

        result.IsSuccess.ShouldBeTrue();
        api.Stub.Count(HttpMethod.Get, "/api/thing").ShouldBe(2);
    }

    // Review Focus 4: a duplicate ticket or reply must never come from a retry.
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task A_write_is_never_retried_on_503(string verb)
    {
        using var api = ApiHarness.Create();
        var method = new HttpMethod(verb);
        api.Stub.OnStatus(method, "/api/thing", HttpStatusCode.ServiceUnavailable);

        var result = await api.Get<ApiConnection>().SendAsync<PublicProductDto>(method, "api/thing", new { note = "x" }, Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(method, "/api/thing").ShouldBe(1);
    }

    [Fact]
    public async Task A_write_with_a_prepared_body_is_never_retried_on_503_or_a_transport_failure()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Post, "/api/thing", HttpStatusCode.BadGateway);
        api.Stub.On(HttpMethod.Put, "/api/other", _ => throw new HttpRequestException("connection reset"));

        var bad = await api.Get<ApiConnection>().SendContentAsync<PublicProductDto>(HttpMethod.Post, "api/thing", new StringContent("a"), Ct);
        var dropped = await api.Get<ApiConnection>().SendContentAsync<PublicProductDto>(HttpMethod.Put, "api/other", new StringContent("a"), Ct);

        bad.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        dropped.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(HttpMethod.Post, "/api/thing").ShouldBe(1);
        api.Stub.Count(HttpMethod.Put, "/api/other").ShouldBe(1);
    }

    [Fact]
    public async Task A_write_without_a_body_in_the_answer_is_a_plain_result()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Post, "/api/thing", HttpStatusCode.Accepted);

        var result = await api.Get<ApiConnection>().SendAsync(HttpMethod.Post, "api/thing", new { email = "a@b.example" }, Ct);

        result.IsSuccess.ShouldBeTrue();
        api.Stub.Requests.ShouldHaveSingleItem().Body!.ShouldContain("a@b.example");
    }

    // Carried ruling: the page shows the message as is, so it is a fixed string and never carries exception text, a host or a port.
    [Fact]
    public async Task A_transport_failure_message_never_carries_exception_text_a_host_or_a_port()
    {
        using var api = ApiHarness.Create();
        api.Stub.On(HttpMethod.Post, "/api/thing", _ => throw new HttpRequestException("No connection could be made because the target machine actively refused it (10.1.2.3:5432)"));
        api.Stub.On(HttpMethod.Put, "/api/thing", _ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 30 seconds elapsing. internal-api.corp:8443", new TimeoutException()));

        var refused = await api.Get<ApiConnection>().SendAsync(HttpMethod.Post, "api/thing", new { }, Ct);
        var timedOut = await api.Get<ApiConnection>().SendAsync(HttpMethod.Put, "api/thing", new { }, Ct);

        foreach (var error in new[] { refused.Errors.Single(), timedOut.Errors.Single() })
        {
            error.Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
            error.Message.ShouldBe(ProblemCopy.ApiUnavailable);
        }
    }

    [Fact]
    public async Task Cancellation_by_the_caller_propagates_and_is_never_a_result()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, "/api/thing", Product());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // The read client's retry handler throws OperationCanceledException itself; the write client's HttpClient throws its subclass TaskCanceledException.
        (await Should.ThrowAsync<Exception>(() => api.Get<ApiConnection>().GetAsync<PublicProductDto>("api/thing", cancelled.Token))).ShouldBeAssignableTo<OperationCanceledException>();
        (await Should.ThrowAsync<Exception>(() => api.Get<ApiConnection>().SendAsync(HttpMethod.Put, "api/thing", new { }, cancelled.Token))).ShouldBeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task A_success_with_a_body_that_is_not_json_is_an_unexpected_response()
    {
        using var api = ApiHarness.Create();
        api.Stub.On(HttpMethod.Get, "/api/thing", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>proxy error</html>") });

        var result = await api.Get<ApiConnection>().GetAsync<PublicProductDto>("api/thing", Ct);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(ApiErrorCodes.UnexpectedResponse);
        error.Message.ShouldBe(ProblemCopy.UnexpectedResponse);
    }

    [Fact]
    public async Task A_success_with_a_json_null_is_an_unexpected_response()
    {
        using var api = ApiHarness.Create();
        api.Stub.On(HttpMethod.Get, "/api/thing", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json") });

        var result = await api.Get<ApiConnection>().GetAsync<PublicProductDto>("api/thing", Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.UnexpectedResponse);
    }

    [Fact]
    public async Task A_400_through_the_pipeline_keeps_every_field_code_target_and_message_and_is_not_retried()
    {
        using var api = ApiHarness.Create();
        api.Stub.On(HttpMethod.Post, "/api/thing", _ => StubApiHandler.ValidationProblem(
        [
            ("email", "email-invalid", "That email address does not look right."),
            ("subject", "subject-required", "Add a subject."),
        ]));

        var result = await api.Get<ApiConnection>().SendContentAsync<PublicProductDto>(HttpMethod.Post, "api/thing", new StringContent("a"), Ct);

        result.Errors.Count.ShouldBe(2);
        result.Errors.ShouldAllBe(e => e.Kind == ResultErrorKind.Validation);
        result.Errors.Single(e => e.Target == "email").Code.ShouldBe("email-invalid");
        result.Errors.Single(e => e.Target == "subject").Message.ShouldBe("Add a subject.");
        api.Stub.Count(HttpMethod.Post, "/api/thing").ShouldBe(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, ApiErrorCodes.PayloadTooLarge)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, ApiErrorCodes.UnsupportedMediaType)]
    [InlineData(HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited)]
    public async Task A_413_a_415_and_a_429_on_a_write_each_get_their_own_code_and_are_not_retried(HttpStatusCode status, string code)
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Post, "/api/thing", status);

        var result = await api.Get<ApiConnection>().SendAsync<PublicProductDto>(HttpMethod.Post, "api/thing", new { }, Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(code);
        api.Stub.Count(HttpMethod.Post, "/api/thing").ShouldBe(1);
    }

    // Review Focus 4: the visitor's address, not the Portal's container, reaches the API on every call, read or write.
    [Fact]
    public async Task Reads_and_writes_both_forward_the_visitors_address()
    {
        using var api = ApiHarness.Create("198.51.100.77");
        api.Stub.OnJson(HttpMethod.Get, "/api/thing", Product()).OnJson(HttpMethod.Post, "/api/thing", Product());
        var connection = api.Get<ApiConnection>();

        (await connection.GetAsync<PublicProductDto>("api/thing", Ct)).IsSuccess.ShouldBeTrue();
        (await connection.SendAsync<PublicProductDto>(HttpMethod.Post, "api/thing", new { }, Ct)).IsSuccess.ShouldBeTrue();

        api.Stub.Requests.Select(r => r.ForwardedFor).ShouldBe(["198.51.100.77", "198.51.100.77"]);
        api.Stub.AssertEveryCallBore("198.51.100.77");
    }

    [Fact]
    public async Task An_ipv4_mapped_ipv6_address_is_forwarded_as_ipv4()
    {
        using var api = ApiHarness.Create("::ffff:203.0.113.5");
        api.Stub.OnJson(HttpMethod.Get, "/api/thing", Product());

        await api.Get<ApiConnection>().GetAsync<PublicProductDto>("api/thing", Ct);

        api.Stub.Requests.ShouldHaveSingleItem().ForwardedFor.ShouldBe("203.0.113.5");
    }

    [Fact]
    public async Task The_assertion_helper_fails_when_a_call_lacks_the_address_or_when_there_was_no_call()
    {
        using var api = ApiHarness.Create(clientIp: null);
        api.Stub.OnJson(HttpMethod.Get, "/api/thing", Product());

        Should.Throw<InvalidOperationException>(() => api.Stub.AssertEveryCallBore("203.0.113.9")).Message.ShouldContain("made no API call");

        await api.Get<ApiConnection>().GetAsync<PublicProductDto>("api/thing", Ct);

        Should.Throw<InvalidOperationException>(() => api.Stub.AssertEveryCallBore("203.0.113.9")).Message.ShouldContain("expected the visitor's address");
    }

    [Fact]
    public void Both_named_clients_forward_the_visitors_address_and_only_the_read_client_retries()
    {
        using var api = ApiHarness.Create();
        var factory = api.Get<IHttpMessageHandlerFactory>();

        string[] Chain(string name)
        {
            var names = new List<string>();
            for (var handler = factory.CreateHandler(name) as DelegatingHandler; handler is not null; handler = handler.InnerHandler as DelegatingHandler)
            {
                names.Add(handler.GetType().Name);
            }

            return [.. names];
        }

        var read = Chain(ApiClientNames.Read);
        var write = Chain(ApiClientNames.Write);

        read.ShouldContain("ForwardedClientIpHandler");
        write.ShouldContain("ForwardedClientIpHandler");

        // The default HttpClient logging writes every request header at Trace, and a customer call carries X-Ticket-Token.
        read.ShouldNotContain(name => name.Contains("Logging", StringComparison.Ordinal));
        write.ShouldNotContain(name => name.Contains("Logging", StringComparison.Ordinal));

        // An allowlist of handler types: the write chain is exactly the forwarded-IP handler, the read chain adds only the retry handler.
        write.ShouldNotContain("ResilienceHandler");
        read.Except(write).ShouldBe(["ResilienceHandler"]);
    }

    [Fact]
    public void The_clients_use_the_api_base_address_and_the_read_and_write_timeouts()
    {
        using var api = ApiHarness.Create();
        var factory = api.Get<IHttpClientFactory>();

        factory.CreateClient(ApiClientNames.Read).BaseAddress.ShouldBe(new Uri("http://api.test/"));
        factory.CreateClient(ApiClientNames.Write).BaseAddress.ShouldBe(new Uri("http://api.test/"));
        factory.CreateClient(ApiClientNames.Read).Timeout.ShouldBe(TimeSpan.FromSeconds(ApiClientRegistration.ReadTimeoutSeconds));
        factory.CreateClient(ApiClientNames.Write).Timeout.ShouldBe(TimeSpan.FromSeconds(ApiClientRegistration.WriteTimeoutSeconds));
    }

    // Review Focus 1: the ticket token is a header on the one request that needs it, on reads and writes, and nowhere else.
    [Fact]
    public async Task A_read_and_a_write_with_a_token_send_it_in_the_ticket_header_and_not_in_the_address()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, "/api/customer/ticket", Product()).OnJson(HttpMethod.Post, "/api/customer/ticket/replies", Product(), HttpStatusCode.Created);
        var connection = api.Get<ApiConnection>();

        (await connection.GetAsync<PublicProductDto>("api/customer/ticket", ValidToken(), Ct)).IsSuccess.ShouldBeTrue();
        (await connection.SendAsync<PublicProductDto>(HttpMethod.Post, "api/customer/ticket/replies", new { body = "hi" }, ValidToken(), Ct)).IsSuccess.ShouldBeTrue();
        (await connection.SendContentAsync<PublicProductDto>(HttpMethod.Post, "api/customer/ticket/replies", new StringContent("hi"), ValidToken(), Ct)).IsSuccess.ShouldBeTrue();

        api.Stub.Requests.Count.ShouldBe(3);
        api.Stub.Requests.ShouldAllBe(r => r.TicketToken == Token);
        api.Stub.Requests.ShouldAllBe(r => !r.Path.Contains(Token) && !r.Query.Contains(Token));
        api.Stub.Requests.Where(r => r.Body is not null).ShouldAllBe(r => !r.Body!.Contains(Token));
    }

    [Fact]
    public async Task A_call_without_a_token_sends_no_ticket_header()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, "/api/thing", Product()).OnJson(HttpMethod.Post, "/api/thing", Product());
        var connection = api.Get<ApiConnection>();

        await connection.GetAsync<PublicProductDto>("api/thing", Ct);
        await connection.SendAsync<PublicProductDto>(HttpMethod.Post, "api/thing", new { }, Ct);

        api.Stub.Requests.ShouldAllBe(r => r.TicketToken == null);
    }

    [Fact]
    public async Task A_token_is_never_carried_over_to_the_next_call_on_the_same_connection()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, "/api/thing", Product());
        var connection = api.Get<ApiConnection>();

        await connection.GetAsync<PublicProductDto>("api/thing", ValidToken(), Ct);
        await connection.GetAsync<PublicProductDto>("api/thing", Ct);

        api.Stub.Requests.Select(r => r.TicketToken).ShouldBe([Token, null]);
    }

    [Fact]
    public async Task A_retried_read_sends_the_token_on_every_attempt()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Get, "/api/customer/ticket", HttpStatusCode.ServiceUnavailable);

        await api.Get<ApiConnection>().GetAsync<PublicProductDto>("api/customer/ticket", ValidToken(), Ct);

        api.Stub.Requests.Count.ShouldBe(1 + ApiClientRegistration.ReadRetryCount);
        api.Stub.Requests.ShouldAllBe(r => r.TicketToken == Token);
    }
}
```

`tests/TechStrap.Portal.Tests/Clients/ProblemMappingTests.cs`

```csharp
using System.Net;
using SyntaxCircus.Common;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Clients;

/// <summary>P09-T02: every status the Portal must treat differently maps to one code, one kind and one fixed, user-safe message.</summary>
public sealed class ProblemMappingTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task<IReadOnlyList<ResultError>> MapAsync(HttpResponseMessage response)
    {
        using (response)
        {
            return ProblemMapping.Map(response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
        }
    }

    [Fact]
    public async Task A_400_yields_every_field_error_with_its_code_message_and_target()
    {
        var errors = await MapAsync(StubApiHandler.ValidationProblem(
        [
            ("email", "email-invalid", "That email address does not look right."),
            ("email", "email-too-long", "That email address is too long."),
            ("subject", "subject-required", "Add a subject."),
            ("", "attachments-too-many", "Attach at most 5 files."),
        ]));

        errors.Count.ShouldBe(4);
        errors.ShouldAllBe(e => e.Kind == ResultErrorKind.Validation);
        errors.Where(e => e.Target == "email").Select(e => e.Code).ShouldBe(["email-invalid", "email-too-long"]);
        errors.Single(e => e.Target == "subject").Message.ShouldBe("Add a subject.");
        errors.Single(e => e.Code == "attachments-too-many").Target.ShouldBeNull();
    }

    [Fact]
    public async Task A_400_without_field_codes_is_one_validation_failure_with_the_detail_as_its_message()
    {
        var errors = await MapAsync(StubApiHandler.Problem(HttpStatusCode.BadRequest, "bad-request", "The request body is not valid."));

        var error = errors.ShouldHaveSingleItem();
        error.Kind.ShouldBe(ResultErrorKind.Validation);
        error.Code.ShouldBe(ApiErrorCodes.ValidationFailed);
        error.Message.ShouldBe("The request body is not valid.");
        error.Target.ShouldBeNull();
    }

    [Fact]
    public async Task A_400_with_no_body_gets_the_fixed_copy()
    {
        var errors = await MapAsync(new HttpResponseMessage(HttpStatusCode.BadRequest));

        errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.ValidationFailed, ProblemCopy.Invalid, ResultErrorKind.Validation));
    }

    // Product enumeration: whatever the API says about a missing thing, the Portal says one thing, with one code.
    [Theory]
    [InlineData("product-not-found")]
    [InlineData("not-found")]
    [InlineData("ticket-not-found")]
    [InlineData("a-code-the-api-may-add-later")]
    public async Task A_404_is_always_the_same_not_found_error_whatever_the_api_called_it(string type)
    {
        var errors = await MapAsync(StubApiHandler.Problem(HttpStatusCode.NotFound, type, "No such Paperplane product."));

        errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound));
    }

    [Fact]
    public async Task A_413_is_payload_too_large_and_a_415_is_unsupported_media_type_with_fixed_copy()
    {
        var tooLarge = await MapAsync(StubApiHandler.Problem(HttpStatusCode.RequestEntityTooLarge, "request-too-large", "Body larger than 26214400 bytes."));
        var unsupported = await MapAsync(StubApiHandler.Problem(HttpStatusCode.UnsupportedMediaType, "unsupported-media-type", "This endpoint accepts multipart/form-data."));

        tooLarge.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.PayloadTooLarge, ProblemCopy.PayloadTooLarge, ResultErrorKind.Failure));
        unsupported.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.UnsupportedMediaType, ProblemCopy.UnsupportedMediaType, ResultErrorKind.Failure));
    }

    [Fact]
    public async Task A_429_is_rate_limited_with_fixed_copy()
    {
        var errors = await MapAsync(StubApiHandler.Problem(HttpStatusCode.TooManyRequests, "rate-limited", "Too many requests."));

        errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.RateLimited, ProblemCopy.RateLimited, ResultErrorKind.Failure));
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    [InlineData(599)]
    public async Task Any_5xx_is_api_unavailable_decided_by_status_never_by_the_apis_own_text(int status)
    {
        var errors = await MapAsync(StubApiHandler.Problem((HttpStatusCode)status, "internal-error", "System.InvalidOperationException at Npgsql host=10.0.0.5"));

        errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.ApiUnavailable, ProblemCopy.ApiUnavailable, ResultErrorKind.Failure));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(409)]
    [InlineData(408)]
    [InlineData(418)]
    public async Task Any_other_status_is_a_generic_api_error_with_fixed_copy(int status)
    {
        var errors = await MapAsync(StubApiHandler.Problem((HttpStatusCode)status, "something", "internal detail"));

        errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.ApiError, ProblemCopy.ApiError, ResultErrorKind.Failure));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"errorCodes\":[1]}")]
    [InlineData("{\"errorCodes\":{\"a\":\"b\"}}")]
    public async Task An_unreadable_problem_body_never_throws(string body)
    {
        foreach (var status in new[] { 400, 404, 413, 429, 500 })
        {
            using var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) };

            var errors = ProblemMapping.Map(response.StatusCode, await response.Content.ReadAsStringAsync(Ct));

            errors.ShouldNotBeEmpty();
        }
    }

    [Fact]
    public void Every_fixed_message_is_plain_text_a_visitor_can_act_on()
    {
        foreach (var message in new[] { ProblemCopy.Invalid, ProblemCopy.NotFound, ProblemCopy.PayloadTooLarge, ProblemCopy.UnsupportedMediaType, ProblemCopy.RateLimited, ProblemCopy.ApiUnavailable, ProblemCopy.ApiError, ProblemCopy.UnexpectedResponse })
        {
            message.ShouldNotBeNullOrWhiteSpace();
            message.ShouldNotContain("<");
            message.ShouldNotContain("API", Case.Sensitive, "the visitor does not know there is one");
        }
    }
}
```

`tests/TechStrap.Portal.Tests/Clients/PublicProductClientTests.cs`

```csharp
using System.Net;
using SyntaxCircus.Common;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Clients;

public sealed class PublicProductClientTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static PublicProductDto Product() => new("paperplane", "Paperplane", "https://cdn.example.com/logo.png", "#F59E0B", "#000000", "#9D6507");

    [Fact]
    public async Task A_known_product_is_returned_with_its_branding()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/products/paperplane", Product());

        var result = await api.Get<IPublicProductClient>().GetAsync("paperplane", Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Product());
        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
    }

    [Fact]
    public async Task An_unknown_or_inactive_product_is_the_uniform_not_found_error()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnProblem(HttpMethod.Get, "/api/public/products/gone", HttpStatusCode.NotFound, "product-not-found", "No such product.");

        var result = await api.Get<IPublicProductClient>().GetAsync("gone", Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Paperplane")]
    [InlineData("paper plane")]
    [InlineData("../admin")]
    [InlineData("paperplane/tickets")]
    [InlineData("paperplane?x=1")]
    [InlineData("paperplane#top")]
    [InlineData("%2e%2e")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task A_key_that_is_not_a_slug_is_not_found_and_no_call_is_made(string? key)
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/products/paperplane", Product());

        var result = await api.Get<IPublicProductClient>().GetAsync(key!, Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound));
        api.Stub.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ApiErrorCodes.ApiUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError, ApiErrorCodes.ApiUnavailable)]
    public async Task A_failure_of_the_api_is_a_result_with_its_own_code(HttpStatusCode status, string code)
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Get, "/api/public/products/paperplane", status);

        var result = await api.Get<IPublicProductClient>().GetAsync("paperplane", Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(code);
    }
}
```

`tests/TechStrap.Portal.Tests/Clients/TicketTokenTests.cs`

```csharp
using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Tests.Clients;

/// <summary>The capability to call the API as a ticket's customer: a value that cannot be printed by accident and cannot carry a character a header or a path could misread.</summary>
public sealed class TicketTokenTests
{
    private static readonly string Valid = "AbC-_0123456789AbC-_0123456789AbC-_0123456789"[..TicketToken.Length];

    [Fact]
    public void A_43_character_base64url_value_is_a_token_and_exposes_its_value()
    {
        TicketToken.TryParse(Valid, out var token).ShouldBeTrue();

        token.Value.ShouldBe(Valid);
        Valid.Length.ShouldBe(43);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    public void Nothing_or_something_short_is_not_a_token(string? text)
    {
        TicketToken.TryParse(text, out _).ShouldBeFalse();
    }

    [Fact]
    public void One_character_too_few_or_too_many_is_not_a_token()
    {
        TicketToken.TryParse(Valid[..42], out _).ShouldBeFalse();
        TicketToken.TryParse(Valid + "A", out _).ShouldBeFalse();
    }

    // The last character of an otherwise valid value: each one could split a header, end a path segment, add a query or be decoded by something downstream.
    [Theory]
    [InlineData(' ')]
    [InlineData('\r')]
    [InlineData('\n')]
    [InlineData('\t')]
    [InlineData('/')]
    [InlineData('\\')]
    [InlineData('%')]
    [InlineData('+')]
    [InlineData('=')]
    [InlineData('.')]
    [InlineData('?')]
    [InlineData('#')]
    [InlineData(';')]
    [InlineData((char)0xE9)]
    public void A_value_with_a_character_outside_base64url_is_not_a_token(char last)
    {
        TicketToken.TryParse(Valid[..42] + last, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_trailing_newline_after_43_good_characters_is_not_a_token()
    {
        TicketToken.TryParse(Valid + "\n", out _).ShouldBeFalse();
    }

    [Fact]
    public void A_token_never_prints_its_value()
    {
        TicketToken.TryParse(Valid, out var token).ShouldBeTrue();

        token.ToString().ShouldBe("[token]");
        $"{token}".ShouldNotContain(Valid);
        string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", token).ShouldNotContain(Valid);
    }

    [Fact]
    public void The_default_token_has_no_value_and_cannot_be_used()
    {
        var token = default(TicketToken);

        Should.Throw<InvalidOperationException>(() => token.Value);
        token.ToString().ShouldBe("[token]");
    }
}
```

`ReadRetryAfterTests` steps a `FakeTimeProvider` instead of waiting for the retry delays.

`tests/TechStrap.Portal.Tests/Clients/ReadRetryAfterTests.cs`

```csharp
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Time.Testing;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Clients;

/// <summary>
/// The read client honours <c>Retry-After</c> but never waits more than <see cref="ApiClientRegistration.ReadRetryAfterCap"/> for it: an overloaded API that says "120" must not freeze
/// a visitor's page on "loading" until the client timeout.
/// </summary>
public sealed class ReadRetryAfterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static HttpResponseMessage WithRetryAfter(RetryConditionHeaderValue? value)
    {
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter = value;
        return response;
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(120, 2)]
    public void A_retry_after_in_seconds_is_honoured_up_to_the_cap(int seconds, int expectedSeconds)
    {
        var delay = ApiClientRegistration.RetryAfterDelay(WithRetryAfter(new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds))), Now);

        delay.ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void A_retry_after_date_is_honoured_up_to_the_cap()
    {
        ApiClientRegistration.RetryAfterDelay(WithRetryAfter(new RetryConditionHeaderValue(Now.AddMilliseconds(1500))), Now).ShouldBe(TimeSpan.FromMilliseconds(1500));
        ApiClientRegistration.RetryAfterDelay(WithRetryAfter(new RetryConditionHeaderValue(Now.AddMinutes(10))), Now).ShouldBe(ApiClientRegistration.ReadRetryAfterCap);
    }

    [Fact]
    public void No_header_a_zero_wait_or_a_date_in_the_past_leaves_the_backoff_in_charge()
    {
        ApiClientRegistration.RetryAfterDelay(WithRetryAfter(null), Now).ShouldBeNull();
        ApiClientRegistration.RetryAfterDelay(WithRetryAfter(new RetryConditionHeaderValue(TimeSpan.Zero)), Now).ShouldBeNull();
        ApiClientRegistration.RetryAfterDelay(WithRetryAfter(new RetryConditionHeaderValue(Now.AddMinutes(-5))), Now).ShouldBeNull();
        ApiClientRegistration.RetryAfterDelay(null, Now).ShouldBeNull();
    }

    [Fact(Timeout = 60000)]
    public async Task A_503_that_asks_for_two_minutes_is_retried_after_at_most_the_cap()
    {
        var time = new FakeTimeProvider(Now);
        using var api = ApiHarness.Create(time: time);
        api.Stub.On(HttpMethod.Get, "/api/thing", _ => WithRetryAfter(new RetryConditionHeaderValue(TimeSpan.FromSeconds(120))));

        var call = api.Get<ApiConnection>().GetAsync<PublicProductDto>("api/thing", TestContext.Current.CancellationToken);
        var stepped = TimeSpan.Zero;
        while (!call.IsCompleted && stepped < TimeSpan.FromSeconds(30))
        {
            time.Advance(TimeSpan.FromMilliseconds(250));
            stepped += TimeSpan.FromMilliseconds(250);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        var finishedInTime = call.IsCompleted;
        time.Advance(TimeSpan.FromMinutes(10)); // a failing run must still end, not wait on the fake clock for ever
        finishedInTime.ShouldBeTrue("the retries must not wait for the 120 seconds the API asked for");
        (await call).Errors[0].Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(HttpMethod.Get, "/api/thing").ShouldBe(1 + ApiClientRegistration.ReadRetryCount);
        stepped.ShouldBeGreaterThanOrEqualTo(ApiClientRegistration.ReadRetryAfterCap, "the header is still honoured, not ignored");
        stepped.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(2 * ApiClientRegistration.ReadRetryCount + 1));
    }
}
```

`ForwardedClientIpHostTests` is the end-to-end proof of Review Focus 4: the host is put behind a proxy (the connection peer is in the trusted network), the visitor's address arrives in `X-Forwarded-For`, and the stub API sees the visitor. `TicketTokenLeakTests` is the token proof at the host, at Verbose.

`tests/TechStrap.Portal.Tests/Clients/ForwardedClientIpHostTests.cs`

```csharp
using System.Net;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Clients;

/// <summary>
/// Review Focus 4 end to end: behind a trusted reverse proxy the Portal sees the visitor's address (from the proxy's <c>X-Forwarded-For</c>), and the API sees the same address from the Portal,
/// not the Portal's container and not the proxy (D-019). An untrusted peer's header is ignored.
/// </summary>
public sealed class ForwardedClientIpHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static void ConfigureProduct(PortalFactory factory) =>
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", new PublicProductDto("paperplane", "Paperplane", null, "#F59E0B", "#000000", "#9D6507"));

    [Fact]
    public async Task The_visitors_address_from_a_trusted_proxy_reaches_the_api()
    {
        await using var factory = new PortalFactory(configureServices: ProxyHopStartupFilter.Add("192.0.2.10"));
        ConfigureProduct(factory);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProxyHopStartupFilter.ProbePath);
        request.Headers.Add("X-Forwarded-For", "203.0.113.9");

        using var response = await client.SendAsync(request, Ct);

        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("ok");
        factory.Api.AssertEveryCallBore("203.0.113.9");
    }

    [Fact]
    public async Task The_last_untrusted_address_in_a_forwarded_chain_is_the_visitor()
    {
        await using var factory = new PortalFactory(configureServices: ProxyHopStartupFilter.Add("192.0.2.10"));
        ConfigureProduct(factory);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProxyHopStartupFilter.ProbePath);
        request.Headers.Add("X-Forwarded-For", "198.51.100.200, 203.0.113.9");

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Api.AssertEveryCallBore("203.0.113.9");
    }

    [Fact]
    public async Task A_forwarded_header_from_an_untrusted_peer_is_ignored_and_the_peer_is_the_visitor()
    {
        await using var factory = new PortalFactory(configureServices: ProxyHopStartupFilter.Add("198.51.100.50"));
        ConfigureProduct(factory);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProxyHopStartupFilter.ProbePath);
        request.Headers.Add("X-Forwarded-For", "203.0.113.9");

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Api.AssertEveryCallBore("198.51.100.50");
    }
}
```

`tests/TechStrap.Portal.Tests/Clients/TicketTokenLeakTests.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Tests.Clients;

/// <summary>
/// Review Focus 1 at the host: a call made as a ticket's customer sends the token in <c>X-Ticket-Token</c>, and the token is in no log event at any level. The calls really go through the host's
/// own client pipeline. Serilog's PII redactor masks a 43-character token, so a clean scan alone would prove little: the real test also pins that the Portal's clients write no log event of their own
/// (the factory's default logging is what writes request headers), and the negative control shows that the same scan does see a header value when that logging is put back.
/// </summary>
public sealed class TicketTokenLeakTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    // 43 base64url characters: the shape the Serilog redactor masks.
    private static readonly string Token = "Zk9_-" + new string('q', TicketToken.Length - 5);

    // Not the shape the redactor masks (47 characters, with a prefix and hyphens), so the control can see it in the sink.
    private const string Secret = "control-secret-0123456789abcdef0123456789abcdef";

    private static string Everything(LogEvent e) => string.Join('\n', [e.RenderMessage(), e.Exception?.ToString() ?? string.Empty, .. e.Properties.Values.Select(v => v.ToString())]);

    private static string SourceContext(LogEvent e) => e.Properties.TryGetValue("SourceContext", out var value) && value is ScalarValue { Value: string context } ? context : string.Empty;

    [Fact]
    public async Task Control_with_the_default_http_client_logging_put_back_a_header_value_reaches_the_Serilog_sink_at_Verbose()
    {
        await using var factory = new PortalFactory(
            settings: PortalFactory.VerboseLogging,
            configureServices: services => services.AddHttpClient(ApiClientNames.Read).AddDefaultLogger());
        factory.Api.OnJson(HttpMethod.Get, "/api/thing", new PublicProductDto("x", "X", null, "#000000", "#FFFFFF", "#000000"));
        using var host = factory.CreateClient(); // starts the host

        using var client = factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient(ApiClientNames.Read);
        client.DefaultRequestHeaders.Add(HeaderNames.TicketToken, Secret);
        using var response = await client.GetAsync("api/thing", Ct);

        response.IsSuccessStatusCode.ShouldBeTrue();
        factory.Api.Requests.ShouldHaveSingleItem().TicketToken.ShouldBe(Secret, "the request must really have carried the header");
        factory.LogSink.Events.ShouldContain(e => SourceContext(e).StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal) && e.Level <= LogEventLevel.Debug, "no Debug-or-lower HttpClient event was captured, so the scan could not see this client");
        factory.LogSink.Events.Select(Everything).ShouldContain(text => text.Contains(Secret, StringComparison.Ordinal), "the factory's default logging must leak here, or the real check below cannot fail");
    }

    [Fact]
    public async Task The_token_appears_in_no_log_event_at_any_level_and_the_clients_write_no_event_of_their_own()
    {
        await using var factory = new PortalFactory(settings: PortalFactory.VerboseLogging);
        factory.Api.OnJson(HttpMethod.Get, "/api/customer/ticket", new PublicProductDto("x", "X", null, "#000000", "#FFFFFF", "#000000"));
        factory.Api.OnStatus(HttpMethod.Post, "/api/customer/ticket/replies", System.Net.HttpStatusCode.ServiceUnavailable);
        using var host = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var connection = scope.ServiceProvider.GetRequiredService<ApiConnection>();
        TicketToken.TryParse(Token, out var token).ShouldBeTrue();

        (await connection.GetAsync<PublicProductDto>("api/customer/ticket", token, Ct)).IsSuccess.ShouldBeTrue();
        (await connection.SendAsync(HttpMethod.Post, "api/customer/ticket/replies", new { body = "hello" }, token, Ct)).IsFailure.ShouldBeTrue();

        factory.Api.Requests.Count.ShouldBe(2, "the calls must really have been sent, or this test proves nothing");
        factory.Api.Requests.ShouldAllBe(r => r.TicketToken == Token);
        factory.LogSink.Events.ShouldContain(e => e.Level <= LogEventLevel.Debug, "the Verbose setting must have taken effect, or this only scanned Information and above");
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text => !text.Contains(Token, StringComparison.Ordinal));
        factory.LogSink.Events.ShouldNotContain(e => SourceContext(e).StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal), "the Portal's API clients must have no logging handlers");
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet build tests/TechStrap.Portal.Tests -c Release`
Expected: FAIL to compile, 13 errors, the first `error CS0234: The type or namespace name 'Clients' does not exist in the namespace 'TechStrap.Portal'` (in `ApiHarness.cs`, `PortalTestApi.cs` and `ProxyHopStartupFilter.cs`), then `CS0246: The type or namespace name 'TicketToken' could not be found`.

- [ ] **Step 3: Implement the clients**

The project reference to `SyntaxCircus.Http.Resilience` brings `Microsoft.Extensions.Http.Resilience` and Polly (the Admin uses the same). `TicketToken` can only be made from a 43-character base64url value and never prints. `ProblemMapping` decides everything but a 400's field codes from the status, so a page never depends on the API's wording. `ApiConnection` is the only place that uses HTTP; the token becomes a header on the request, never on the client.

`src/TechStrap.Portal/TechStrap.Portal.csproj`

```diff
--- src/TechStrap.Portal/TechStrap.Portal.csproj
+++ src/TechStrap.Portal/TechStrap.Portal.csproj
@@ -20,6 +20,7 @@
     <PackageReference Include="SyntaxCircus.Blazor.Seo" />
     <PackageReference Include="SyntaxCircus.Common" />
     <PackageReference Include="SyntaxCircus.DotEnv" />
+    <PackageReference Include="SyntaxCircus.Http.Resilience" />
     <PackageReference Include="SyntaxCircus.Observability" />
   </ItemGroup>
 
```

`src/TechStrap.Portal/Clients/ApiClientNames.cs`

```csharp
namespace TechStrap.Portal.Clients;

/// <summary>The two named HTTP clients every API call goes through (D-045). Both forward the visitor's address; only the read client retries.</summary>
public static class ApiClientNames
{
    /// <summary>Idempotent GETs: retried up to <see cref="ApiClientRegistration.ReadRetryCount"/> times on transport errors, timeouts and 408/502/503/504 (never on 500, no circuit breaker).</summary>
    public const string Read = "techstrap-portal-api-read";

    /// <summary>Every POST, PUT and DELETE: no retry and no circuit breaker, so a transient failure can never duplicate a ticket or a reply.</summary>
    public const string Write = "techstrap-portal-api-write";
}
```

`src/TechStrap.Portal/Clients/ApiErrorCodes.cs`

```csharp
namespace TechStrap.Portal.Clients;

/// <summary>
/// The error codes pages branch on. A 400 keeps the codes the API sends (the entries of <c>errorCodes</c>, one per field); every other code is decided by the Portal from the status, so a page never
/// depends on the API's own wording and a not-found is one code whatever the API called it.
/// </summary>
public static class ApiErrorCodes
{
    public const string ValidationFailed = "validation-failed";
    public const string NotFound = "not-found";
    public const string PayloadTooLarge = "payload-too-large";
    public const string UnsupportedMediaType = "unsupported-media-type";
    public const string RateLimited = "rate-limited";

    /// <summary>The API could not be reached or failed (any 5xx, a transport error or a timeout).</summary>
    public const string ApiUnavailable = "api-unavailable";

    public const string UnexpectedResponse = "api-unexpected-response";

    /// <summary>Any other status (401, 403, 409 and so on): the public routes do not answer them, so a page treats it as a failure.</summary>
    public const string ApiError = "api-error";
}
```

`src/TechStrap.Portal/Clients/ProblemCopy.cs`

```csharp
namespace TechStrap.Portal.Clients;

/// <summary>
/// The fixed, user-safe sentences for what can go wrong between the Portal and the API. They never carry exception text, a host, a port or anything the API said (the API's own detail can name
/// a table or a limit), and they never name the API: a visitor does not know there is one. Pages show these as they are.
/// </summary>
public static class ProblemCopy
{
    public const string Invalid = "Some of what you entered needs another look.";
    public const string NotFound = "We could not find that.";
    public const string PayloadTooLarge = "That is too large to send. Remove a file or two and try again.";
    public const string UnsupportedMediaType = "That could not be sent in that form. Reload the page and try again.";
    public const string RateLimited = "You have sent a lot in a short time. Wait a minute and try again.";
    public const string ApiUnavailable = "We could not reach our support system. Try again in a moment.";
    public const string ApiError = "Something went wrong on our side. Try again in a moment.";
    public const string UnexpectedResponse = "We got an answer we did not expect. Try again in a moment.";
}
```

`src/TechStrap.Portal/Clients/ProblemMapping.cs`

```csharp
using System.Net;
using System.Text.Json;
using SyntaxCircus.Common;

namespace TechStrap.Portal.Clients;

/// <summary>
/// Turns a non-success API response into <see cref="Result"/> errors (D-045). The API answers with RFC 7807 problem details; a validation failure (400) carries the specific codes in the
/// <c>errorCodes</c> extension, keyed by field, and the messages in <c>errors</c>. Every other status is mapped by the status alone, to a fixed code and a fixed sentence (<see cref="ProblemCopy"/>):
/// 404 is one not-found whatever the API called it (so nothing can tell an unknown product from an unknown ticket), 413 and 415 are the attachment errors, 429 is rate limited, and any 5xx is
/// "unavailable" (a write may or may not have been applied, and the API's own text is never shown).
/// </summary>
internal static class ProblemMapping
{
    public static IReadOnlyList<ResultError> Map(HttpStatusCode status, string? body)
    {
        switch ((int)status)
        {
            case 400:
                var problem = Problem.TryParse(body);
                var errors = ValidationErrors(problem);
                return errors.Count > 0
                    ? errors
                    : [new ResultError(ApiErrorCodes.ValidationFailed, problem.Detail ?? ProblemCopy.Invalid, ResultErrorKind.Validation)];
            case 404:
                return [NotFound()];
            case 413:
                return [new ResultError(ApiErrorCodes.PayloadTooLarge, ProblemCopy.PayloadTooLarge, ResultErrorKind.Failure)];
            case 415:
                return [new ResultError(ApiErrorCodes.UnsupportedMediaType, ProblemCopy.UnsupportedMediaType, ResultErrorKind.Failure)];
            case 429:
                return [new ResultError(ApiErrorCodes.RateLimited, ProblemCopy.RateLimited, ResultErrorKind.Failure)];
            case >= 500:
                return [Unavailable()];
            default:
                return [new ResultError(ApiErrorCodes.ApiError, ProblemCopy.ApiError, ResultErrorKind.Failure)];
        }
    }

    /// <summary>The one not-found: also what a client answers for a key or token that is malformed, without calling the API.</summary>
    public static ResultError NotFound() => new(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound);

    public static ResultError Unavailable() => new(ApiErrorCodes.ApiUnavailable, ProblemCopy.ApiUnavailable, ResultErrorKind.Failure);

    public static ResultError Unexpected() => new(ApiErrorCodes.UnexpectedResponse, ProblemCopy.UnexpectedResponse, ResultErrorKind.Failure);

    private static List<ResultError> ValidationErrors(Problem problem)
    {
        var errors = new List<ResultError>();
        foreach (var (field, codes) in problem.ErrorCodes)
        {
            for (var i = 0; i < codes.Length; i++)
            {
                var messages = problem.Errors.GetValueOrDefault(field);
                var message = messages is not null && i < messages.Length ? messages[i] : problem.Detail ?? ProblemCopy.Invalid;
                errors.Add(new ResultError(codes[i], message, ResultErrorKind.Validation, field.Length == 0 ? null : field));
            }
        }

        return errors;
    }

    private sealed record Problem(string? Detail, IReadOnlyDictionary<string, string[]> Errors, IReadOnlyDictionary<string, string[]> ErrorCodes)
    {
        private static readonly Problem Empty = new(null, new Dictionary<string, string[]>(), new Dictionary<string, string[]>());

        public static Problem TryParse(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                return root.ValueKind == JsonValueKind.Object ? new Problem(Text(root, "detail"), Dictionary(root, "errors"), Dictionary(root, "errorCodes")) : Empty;
            }
            catch (JsonException)
            {
                return Empty;
            }
        }

        private static string? Text(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;

        private static Dictionary<string, string[]> Dictionary(JsonElement root, string name)
        {
            var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in value.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Array))
                {
                    result[property.Name] = [.. property.Value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)];
                }
            }

            return result;
        }
    }
}
```

`src/TechStrap.Portal/Clients/TicketToken.cs`

```csharp
using System.Text.RegularExpressions;

namespace TechStrap.Portal.Clients;

/// <summary>
/// The capability to call the API as one ticket's customer (the <c>/t/{token}</c> in a link). It can only be made from a 43-character base64url value, the shape of every access token, so a
/// value that reaches a request header can never carry a character that splits a header or ends a path. It never prints: <see cref="ToString"/> is a fixed marker, so a log call that formats
/// it, an exception message or a debugger line shows no secret. The value is read only where the request header is set (<see cref="ApiConnection"/>); it is never put in a URL, a body or a log.
/// </summary>
public readonly partial struct TicketToken
{
    /// <summary>The length of an access token (the PII redactor masks the same shape in logs).</summary>
    public const int Length = 43;

    private readonly string? _value;

    private TicketToken(string value) => _value = value;

    [GeneratedRegex(@"\A[A-Za-z0-9_\-]{43}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();

    /// <summary>The token text. Throws for <c>default</c>.</summary>
    public string Value => _value ?? throw new InvalidOperationException("This ticket token has no value.");

    public static bool TryParse(string? text, out TicketToken token)
    {
        if (text is not null && Shape().IsMatch(text))
        {
            token = new TicketToken(text);
            return true;
        }

        token = default;
        return false;
    }

    public override string ToString() => "[token]";
}
```

`src/TechStrap.Portal/Clients/ApiConnection.cs`

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using SyntaxCircus.Common;
using TechStrap.Contracts.Http;

namespace TechStrap.Portal.Clients;

/// <summary>
/// The only place the Portal talks HTTP to the API (D-045). It builds requests, sends them through the right named client (reads: retried; writes: never retried) and reads the answer into a
/// <see cref="Result"/>; the handler pipeline of each client forwards the visitor's address. A call made as a ticket's customer takes a <see cref="TicketToken"/>, which is set as the
/// <c>X-Ticket-Token</c> header of that one request: never in a URL, a body or a log (the clients have no logging handlers). Cancellation by the caller propagates as
/// <see cref="OperationCanceledException"/>; it is never turned into a Result. One instance per request scope.
/// </summary>
internal sealed class ApiConnection(IHttpClientFactory httpClients)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // One HttpClient per name for the life of the scope: the factory keeps every client it creates until the scope ends.
    private HttpClient? _read;
    private HttpClient? _write;

    private HttpClient ReadClient => _read ??= httpClients.CreateClient(ApiClientNames.Read);

    private HttpClient WriteClient => _write ??= httpClients.CreateClient(ApiClientNames.Write);

    /// <summary>GET through the retrying read client.</summary>
    public Task<Result<T>> GetAsync<T>(string uri, CancellationToken cancellationToken) => SendAsync<T>(ReadClient, new HttpRequestMessage(HttpMethod.Get, uri), null, cancellationToken);

    /// <summary>GET through the retrying read client as a ticket's customer. Every attempt carries the token.</summary>
    public Task<Result<T>> GetAsync<T>(string uri, TicketToken token, CancellationToken cancellationToken) => SendAsync<T>(ReadClient, new HttpRequestMessage(HttpMethod.Get, uri), token, cancellationToken);

    /// <summary>A POST, PUT or DELETE with an optional JSON body, through the write client; the answer carries a JSON body.</summary>
    public Task<Result<T>> SendAsync<T>(HttpMethod method, string uri, object? body, CancellationToken cancellationToken) =>
        SendAsync<T>(WriteClient, JsonRequest(method, uri, body), null, cancellationToken);

    /// <summary>The same as a ticket's customer: the token is the <c>X-Ticket-Token</c> header of this request.</summary>
    public Task<Result<T>> SendAsync<T>(HttpMethod method, string uri, object? body, TicketToken token, CancellationToken cancellationToken) =>
        SendAsync<T>(WriteClient, JsonRequest(method, uri, body), token, cancellationToken);

    /// <summary>A POST, PUT or DELETE with an optional JSON body, through the write client; success has no body (202 or 204).</summary>
    public Task<Result> SendAsync(HttpMethod method, string uri, object? body, CancellationToken cancellationToken) =>
        SendAsync(WriteClient, JsonRequest(method, uri, body), null, cancellationToken);

    /// <summary>The same as a ticket's customer.</summary>
    public Task<Result> SendAsync(HttpMethod method, string uri, object? body, TicketToken token, CancellationToken cancellationToken) =>
        SendAsync(WriteClient, JsonRequest(method, uri, body), token, cancellationToken);

    /// <summary>A write with a prepared body (a multipart ticket or reply). The content is used once: the caller builds a new one for every attempt.</summary>
    public Task<Result<T>> SendContentAsync<T>(HttpMethod method, string uri, HttpContent content, CancellationToken cancellationToken) =>
        SendAsync<T>(WriteClient, new HttpRequestMessage(method, uri) { Content = content }, null, cancellationToken);

    /// <summary>The same as a ticket's customer.</summary>
    public Task<Result<T>> SendContentAsync<T>(HttpMethod method, string uri, HttpContent content, TicketToken token, CancellationToken cancellationToken) =>
        SendAsync<T>(WriteClient, new HttpRequestMessage(method, uri) { Content = content }, token, cancellationToken);

    private static HttpRequestMessage JsonRequest(HttpMethod method, string uri, object? body) =>
        new(method, uri) { Content = body is null ? null : JsonContent.Create(body, body.GetType(), options: Json) };

    private static async Task<Result<T>> SendAsync<T>(HttpClient client, HttpRequestMessage request, TicketToken? token, CancellationToken cancellationToken)
    {
        using (request)
        {
            Attach(request, token);
            try
            {
                using var response = await client.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var errors = ProblemMapping.Map(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
                    return Result<T>.Failure(errors[0], [.. errors.Skip(1)]);
                }

                var value = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
                return value is null ? Result<T>.Failure(ProblemMapping.Unexpected()) : Result<T>.Success(value);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
            {
                // An answer that cannot be read (bad JSON, a content type or encoding the reader does not support).
                return Result<T>.Failure(ProblemMapping.Unexpected());
            }
            catch (Exception ex) when (Transport(ex, cancellationToken) is { } error)
            {
                return Result<T>.Failure(error);
            }
        }
    }

    private static async Task<Result> SendAsync(HttpClient client, HttpRequestMessage request, TicketToken? token, CancellationToken cancellationToken)
    {
        using (request)
        {
            Attach(request, token);
            try
            {
                using var response = await client.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return Result.Success();
                }

                var errors = ProblemMapping.Map(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
                return Result.Failure(errors[0], [.. errors.Skip(1)]);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                return Result.Failure(ProblemMapping.Unexpected());
            }
            catch (Exception ex) when (Transport(ex, cancellationToken) is { } error)
            {
                return Result.Failure(error);
            }
        }
    }

    /// <summary>The one place a token becomes a header. It is set on the request, not on the client, so it cannot outlive the call or reach another one.</summary>
    private static void Attach(HttpRequestMessage request, TicketToken? token)
    {
        if (token is { } ticketToken)
        {
            request.Headers.Add(HeaderNames.TicketToken, ticketToken.Value);
        }
    }

    /// <summary>
    /// A transport failure (unreachable API, timeout) as a Result error. The message is fixed, user-safe copy, so it never includes exception text, a host or a port. Returns null for anything
    /// else, including a cancellation requested by the caller, which must keep propagating.
    /// </summary>
    internal static ResultError? Transport(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        OperationCanceledException when cancellationToken.IsCancellationRequested => null,
        OperationCanceledException or TimeoutException or HttpRequestException => ProblemMapping.Unavailable(),
        _ => null,
    };
}
```

`src/TechStrap.Portal/Clients/ApiClientRegistration.cs`

```csharp
using System.Net;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Clients;

public static class ApiClientRegistration
{
    /// <summary>Retries after the first attempt: a read is at most 1 + this many calls.</summary>
    public const int ReadRetryCount = 2;

    /// <summary>How long a read may take in all, retries included.</summary>
    public const int ReadTimeoutSeconds = 30;

    /// <summary>A write may carry a multipart ticket or reply with attachments (up to 25 MB), so it gets a longer deadline.</summary>
    public const int WriteTimeoutSeconds = 300;

    /// <summary>
    /// The longest a read waits before a retry, whatever the API's <c>Retry-After</c> asks for. The resilience default honours the header with no limit, so an overloaded API that says "120"
    /// would freeze a page on "loading" until the client timeout. The header is still honoured below this cap.
    /// </summary>
    public static readonly TimeSpan ReadRetryAfterCap = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan ReadRetryBaseDelay = TimeSpan.FromMilliseconds(250);

    // Refresh pooled connections so a DNS change of the API is picked up.
    private static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Registers the two named API clients and the typed clients (D-045). Both named clients forward the visitor's address; only <see cref="ApiClientNames.Read"/> retries. Components inject the
    /// <c>I*Client</c> interfaces, never an HttpClient. The base address comes from the validated <see cref="PortalOptions"/> when a client is first created.
    /// </summary>
    public static IServiceCollection AddPortalApiClients(this IServiceCollection services)
    {
        // Retry only: no circuit breaker. The read client is shared by every visitor and every read, so a breaker opened by one failing endpoint would lock every other visitor out.
        // No logging handlers: the default HttpClient logging writes every request header at Trace, and a customer call carries X-Ticket-Token (the leak tests scan every level).
        services.AddHttpClient(ApiClientNames.Read)
            .RemoveAllLoggers()
            .ConfigureHttpClient((sp, client) => ConfigureClient(sp, client, ReadTimeoutSeconds))
            .AddForwardedClientIp()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = ConnectionLifetime })
            .AddResilienceHandler("techstrap-portal-api-read-retry", builder => builder.AddRetry(ReadRetry()));

        services.AddHttpClient(ApiClientNames.Write)
            .RemoveAllLoggers()
            .ConfigureHttpClient((sp, client) => ConfigureClient(sp, client, WriteTimeoutSeconds))
            .AddForwardedClientIp()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = ConnectionLifetime });

        services.AddScoped<ApiConnection>();
        services.AddScoped<IPublicProductClient, PublicProductClient>();
        return services;
    }

    // Transport errors, timeouts, 408 and 502/503/504. A 500 is the API's own answer to this request and is not retried.
    private static HttpRetryStrategyOptions ReadRetry() => new()
    {
        MaxRetryAttempts = ReadRetryCount,
        Delay = ReadRetryBaseDelay,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,

        // The default honours Retry-After without a limit; this one honours it up to ReadRetryAfterCap and otherwise falls back to the backoff above.
        ShouldRetryAfterHeader = false,
        DelayGenerator = args => ValueTask.FromResult(RetryAfterDelay(args.Outcome.Result, TimeProvider.System.GetUtcNow())),
        ShouldHandle = args => ValueTask.FromResult(args.Outcome switch
        {
            { Exception: HttpRequestException or TimeoutException } => true,
            { Exception: OperationCanceledException } => !args.Context.CancellationToken.IsCancellationRequested,
            { Result.StatusCode: HttpStatusCode.RequestTimeout or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout } => true,
            _ => false,
        }),
    };

    /// <summary>
    /// The wait the API asked for in <c>Retry-After</c> (seconds or an HTTP date), capped at <see cref="ReadRetryAfterCap"/>; null when there is no usable header, so the exponential backoff
    /// applies. A date in the past asks for no wait beyond the backoff.
    /// </summary>
    internal static TimeSpan? RetryAfterDelay(HttpResponseMessage? response, DateTimeOffset now)
    {
        var retryAfter = response?.Headers.RetryAfter;
        TimeSpan? requested = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - now : null);
        if (requested is not { } wait || wait <= TimeSpan.Zero)
        {
            return null;
        }

        return wait < ReadRetryAfterCap ? wait : ReadRetryAfterCap;
    }

    private static void ConfigureClient(IServiceProvider services, HttpClient client, int timeoutSeconds)
    {
        client.BaseAddress = services.GetRequiredService<IOptions<PortalOptions>>().Value.ApiBaseUri;
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
    }
}
```

`src/TechStrap.Portal/Clients/IPublicProductClient.cs`

```csharp
using SyntaxCircus.Common;
using TechStrap.Contracts.Products;

namespace TechStrap.Portal.Clients;

/// <summary>The public branding of a product: what every <c>/p/{key}</c> page needs to look like the product's own (D-045).</summary>
public interface IPublicProductClient
{
    /// <summary>
    /// The branding of the active product with this key. A key that is not a slug, an unknown key and an inactive product are all the same not-found error (code <see cref="ApiErrorCodes.NotFound"/>),
    /// and a malformed key is answered without calling the API.
    /// </summary>
    Task<Result<PublicProductDto>> GetAsync(string key, CancellationToken cancellationToken);
}
```

`src/TechStrap.Portal/Clients/PublicProductClient.cs`

```csharp
using SyntaxCircus.Common;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Clients;

internal sealed class PublicProductClient(ApiConnection api) : IPublicProductClient
{
    public Task<Result<PublicProductDto>> GetAsync(string key, CancellationToken cancellationToken) =>
        ProductKeyShape.IsWellFormed(key)
            ? api.GetAsync<PublicProductDto>($"api/public/products/{key}", cancellationToken)
            : Task.FromResult(Result<PublicProductDto>.Failure(ProblemMapping.NotFound()));
}
```

`Program.cs` registers them.

`src/TechStrap.Portal/Program.cs`

```diff
--- src/TechStrap.Portal/Program.cs
+++ src/TechStrap.Portal/Program.cs
@@ -2,6 +2,7 @@ using SyntaxCircus.AspNetCore.Common;
 using SyntaxCircus.DotEnv;
 using TechStrap.Hosting.Security;
 using TechStrap.Hosting.Wiring;
+using TechStrap.Portal.Clients;
 using TechStrap.Portal.Components;
 using TechStrap.Portal.Components.Ui;
 using TechStrap.Portal.Settings;
@@ -26,6 +27,9 @@ builder.Services.AddRazorComponents();
 
 // The API address, the Portal's public address and the optional default product: validated at start (D-043, D-045).
 builder.Services.AddPortalOptions();
+
+// The two named API clients (reads retried, writes never) and the typed clients: every call forwards the visitor's address (D-019, D-045).
+builder.Services.AddPortalApiClients();
 // Installation-wide switch for the "Powered by TechStrap" footer (D-024); shown unless set to false.
 // A value that is not true or false fails at startup rather than breaking every page.
 builder.Services.AddOptions<PoweredByOptions>()
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet build tests/TechStrap.Portal.Tests -c Release`
Expected: 0 warnings, 0 errors.

Run: `dotnet test --project tests/TechStrap.Portal.Tests -c Release`
Expected: PASS: `total: 244, failed: 0` (136 before this task: `ApiConnectionTests` 33, `ProblemMappingTests` 25, `PublicProductClientTests` 16, `TicketTokenTests` 22, `ReadRetryAfterTests` 7, `ForwardedClientIpHostTests` 3, `TicketTokenLeakTests` 2).

- [ ] **Step 5: Prove each pin with a mutation**

Run `git add -A` first, and run each row from the repository root with `python $T/mut.py <file> --replace "<old>" "<new>" -- <command>`. The commands are `dotnet test --project tests/TechStrap.Portal.Tests -c Release --filter-query "/*/*/<Class>/*"`.

| # | File (under `src/TechStrap.Portal/`) | Replace | With | Class | Expected |
| --- | --- | --- | --- | --- | --- |
| 1 | `Clients/ApiClientRegistration.cs` | the write client's `.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = ConnectionLifetime });` | the same plus `\n            .AddResilienceHandler("x", builder => builder.AddRetry(ReadRetry()));` | `ApiConnectionTests` | KILLED, 5 failed of 33 (a write is never retried) |
| 2 | `Clients/ApiClientRegistration.cs` | `.ConfigureHttpClient((sp, client) => ConfigureClient(sp, client, WriteTimeoutSeconds))\n            .AddForwardedClientIp()` | `.ConfigureHttpClient((sp, client) => ConfigureClient(sp, client, WriteTimeoutSeconds))` | `ApiConnectionTests` | KILLED, 2 failed |
| 3 | `Clients/ApiConnection.cs` | `request.Headers.Add(HeaderNames.TicketToken, ticketToken.Value);` | `request.Headers.Add("X-Other", "x");` | `ApiConnectionTests` | KILLED, 3 failed |
| 4 | `Clients/ApiClientRegistration.cs` | `services.AddHttpClient(ApiClientNames.Read)\n            .RemoveAllLoggers()` | `services.AddHttpClient(ApiClientNames.Read)` | `ApiConnectionTests` | KILLED, 1 failed (the handler chain has a logging handler) |
| 5 | `Clients/ProblemMapping.cs` | `case 404:\n                return [NotFound()];` | `case 404:\n                return [new ResultError("product-not-found", ProblemCopy.NotFound, ResultErrorKind.NotFound)];` | `ProblemMappingTests` | KILLED, 4 failed of 25 |
| 6 | `Clients/ProblemMapping.cs` | `case >= 500:` | `case >= 501:` | `ProblemMappingTests` | KILLED, 1 failed |
| 7 | `Clients/PublicProductClient.cs` | `ProductKeyShape.IsWellFormed(key)` | `true` | `PublicProductClientTests` | KILLED, 11 failed of 16 |
| 8 | `Clients/TicketToken.cs` | `public override string ToString() => "[token]";` | `public override string ToString() => _value ?? "[token]";` | `TicketTokenTests` | KILLED, 1 failed |
| 9 | `Clients/ApiClientRegistration.cs` | `return wait < ReadRetryAfterCap ? wait : ReadRetryAfterCap;` | `return wait;` | `ReadRetryAfterTests` | KILLED, 4 failed of 7 (about a minute: the uncapped run steps the fake clock) |
| 10 | `Clients/ApiClientRegistration.cs` | `HttpStatusCode.RequestTimeout or HttpStatusCode.BadGateway` | `HttpStatusCode.InternalServerError or HttpStatusCode.RequestTimeout or HttpStatusCode.BadGateway` | `ApiConnectionTests` | KILLED, 1 failed (a 500 is not retried) |
| 11 | `Clients/ApiClientRegistration.cs` | the read client's `.ConfigureHttpClient((sp, client) => ConfigureClient(sp, client, ReadTimeoutSeconds))\n            .AddForwardedClientIp()` | `.ConfigureHttpClient((sp, client) => ConfigureClient(sp, client, ReadTimeoutSeconds))` | `ForwardedClientIpHostTests` | KILLED, 3 failed of 3 |
| 11b | `Clients/ProblemMapping.cs` | `case 413:\n                return [new ResultError(ApiErrorCodes.PayloadTooLarge, ProblemCopy.PayloadTooLarge, ResultErrorKind.Failure)];` | `case 413:\n                return [Unavailable()];` | `ApiConnectionTests` | KILLED, 1 failed of 33 (a 413 is the attachment error, through the pipeline) |
| 12 | `src/TechStrap.Hosting/Wiring/BrowserHostExtensions.cs` | `        app.UseForwardedHeaders();\n        app.UseCorrelationId();` | `        app.UseCorrelationId();` | `ForwardedClientIpHostTests` | KILLED, 2 failed of 3 (the proxy's address would be forwarded) |

Honest survivor, kept on purpose: removing `.RemoveAllLoggers()` from the read client (row 4) is killed only by the chain test. `TicketTokenLeakTests` still passes, because Hosting's host-wide `AddTechStrapHttpClientDefaults` also strips the logging handlers (defence in depth, pinned by `HostWiringTests.The_Portal_drops_the_default_HttpClient_logging`).

- [ ] **Step 6: Verify and commit**

Run: `dotnet build TechStrap.slnx -c Release --no-incremental`
Expected: `0 Warning(s)`.

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release`
Expected: PASS: `total: 831, failed: 0`.

```bash
git add -A src tests
git diff --cached --stat
git commit -F - <<'EOF'
feat(portal): the API connection, the product client and the fake-API harness (PHASE-09a)

Adds ApiConnection with a retried read client and a never-retried write client that both forward
the visitor's address, ProblemMapping with fixed sentences, TicketToken and the per-request
X-Ticket-Token capability, IPublicProductClient, and the Portal's stub API, proxy-hop and leak tests.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
EOF
```
### Task 3: Per-path security headers in Hosting, and the Seo wiring

**Review Focus pin:** Review Focus 2 (wrong headers on `/t/*`). Pinned here by `PathHeaderRuleHostTests` (the mechanism: a rule overrides the shared headers and an endpoint's own value, applies to the re-executed 404, applies only to its paths, and the sandbox is 2xx only), `TicketHeaderHostTests` (the Portal's final responses: `/t/*` carries `no-referrer`, `no-store` and `noindex` on a 404 and on a delivered page, only the attachment shape is sandboxed, and nothing outside `/t` is touched) and `PortalHeaderRulesTests` (the two path predicates).

**Files:**

- Create: `src/TechStrap.Hosting/Wiring/PathHeaderRule.cs`
- Create: `src/TechStrap.Portal/Headers/PortalHeaderRules.cs`
- Create: `src/TechStrap.Portal/Seo/PortalSeoRegistration.cs`
- Modify: `deploy/.env.portal.example`
- Modify: `src/TechStrap.Hosting/Wiring/BrowserHostExtensions.cs`
- Modify: `src/TechStrap.Portal/.env.example`
- Modify: `src/TechStrap.Portal/Program.cs`
- Modify: `src/TechStrap.Portal/appsettings.json`
- Test (create): `tests/TechStrap.Api.Tests/Hosting/PathHeaderRuleHostTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Api/OkProbeStartupFilter.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Headers/PortalHeaderRulesTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Headers/TicketHeaderHostTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Seo/SeoHostTests.cs`
- Test (modify): `scripts/tests/ConfigContract.Tests.ps1`
- Test (modify): `tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs`


**Interfaces:**
- Consumes: `BrowserHostExtensions.UseTechStrapWebHost`, `WithSandbox` and `AddTechStrapWebHost` (Hosting), `PortalOptions.PublicBaseUrl` and `AddPortalOptions` (Task 1), `PortalRoutes.TicketPrefix` (Task 1), `PortalFactory` and `StubApiHandler` (Task 2), `SyntaxCircus.Blazor.Seo` (`AddSyntaxCircusSeo`, `UseSyntaxCircusSeo`, `MapSeoRobotsTxt`, `SeoOptions`).
- Produces:
  - `TechStrap.Hosting.Wiring.PathHeaderRule` (public sealed): `Func<PathString, bool> Matches`; `static PathHeaderRule Set(Func<PathString, bool> matches, params (string Name, string Value)[] headers)` (sets each header on every response for a matching path, any status); `static PathHeaderRule Sandbox(Func<PathString, bool> matches)` (appends a bare `sandbox` directive to the Content-Security-Policy of a 2xx response only). Both throw for a null predicate; `Set` throws for no headers or a blank name.
  - `BrowserHostExtensions.UseTechStrapWebHost(this WebApplication app, IReadOnlyList<PathHeaderRule> rules, params string[] downloadPathPrefixes)`. The rules are decided from the request path as it arrived (before a 404 is re-executed) and applied after the shared security headers, in the order given, then the download prefixes as one more sandbox rule. The existing `UseTechStrapWebHost(params string[] downloadPathPrefixes)` calls it with no rules, so the Admin is unchanged.
  - `TechStrap.Portal.Headers.PortalHeaderRules` (internal static): `Rules` (the `/t` header rule, then the attachment sandbox), `IsTicketPath(PathString)`, `IsTicketAttachmentPath(PathString)`, constants `ReferrerPolicy = "no-referrer"`, `CacheControl = "no-store"`, `RobotsTag = "noindex"`.
  - `TechStrap.Portal.Seo.PortalSeoRegistration`: `AddPortalSeo(this IServiceCollection, IConfiguration)` (calls `AddSyntaxCircusSeo` and derives `SeoOptions.BaseUrl` from `PortalOptions.PublicBaseUrl` in a `PostConfigure`), `UsePortalSeo(this WebApplication)` (the canonical-host redirect and indexing headers), `MapPortalSeo(this WebApplication)` (`MapSeoRobotsTxt(extraDirectives: [DisallowTickets])`), constant `DisallowTickets = "Disallow: /t/"`. The sitemap is not mapped.
  - Config keys with their D-043 edits: `CanonicalHost:CanonicalHost` (blank), `CanonicalHost:LegacyHosts` (empty list, `__0` commented out in the templates), `CanonicalHost:ForceHttps` (`false`), `CanonicalHost:Permanent` (`true`).
  - Test helper: `OkProbeStartupFilter.Add(Func<PathString, bool> answers)` (answers 200 "probe" after the whole pipeline for the paths named).

- [ ] **Step 1: Write the failing tests**

`PathHeaderRuleHostTests` builds a small host (TestServer) with the real wiring, so it pins the mechanism for any host. The Portal tests use `OkProbeStartupFilter` to get a real 200 under `/t` (the Portal has no ticket page until 09b); the probe paths `/t/probe-token/probe-page` and `/t/probe-token/attachments/probe-id` cannot be matched by a later Portal route (the page is `/t/{token}` and the attachment id is a Guid). `SeoHostTests` pins robots.txt, the derived base URL, the canonical-host allow-list, and that the sitemap is not mapped.

`tests/TechStrap.Api.Tests/Hosting/PathHeaderRuleHostTests.cs`

```csharp
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Hosting.Wiring;

namespace TechStrap.Api.Tests.Hosting;

/// <summary>
/// P09-T04 / D-045: <c>UseTechStrapWebHost</c> accepts per-path header rules that run after the shared security headers. The shared middleware sets <c>Referrer-Policy</c> and the
/// Content-Security-Policy when the response starts, so a value an endpoint sets itself would be overwritten; a rule runs after it and wins. These tests build a small host (TestServer) with the
/// real wiring, so they pin the mechanism for any host; the Portal's own rules are pinned in <c>TechStrap.Portal.Tests</c>.
/// </summary>
public sealed class PathHeaderRuleHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static bool UnderT(PathString path) => path.StartsWithSegments("/t");

    private static bool Attachment(PathString path) => path.StartsWithSegments("/t", out var rest) && rest.Value!.Split('/', StringSplitOptions.RemoveEmptyEntries) is [_, "attachments", _];

    private static IReadOnlyList<PathHeaderRule> Rules() =>
    [
        PathHeaderRule.Set(UnderT, ("Referrer-Policy", "no-referrer"), ("Cache-Control", "no-store"), ("X-Robots-Tag", "noindex")),
        PathHeaderRule.Sandbox(Attachment),
    ];

    private static async Task<WebApplication> StartAsync(Action<WebApplication> configure)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Services.AddTechStrapWebHost(builder.Configuration, "default-src 'self'; frame-ancestors 'none'");
        var app = builder.Build();
        configure(app);
        await app.StartAsync(Ct);
        return app;
    }

    private static void Pipeline(WebApplication app, IReadOnlyList<PathHeaderRule> rules, params string[] downloadPrefixes)
    {
        app.UseTechStrapWebHost(rules, downloadPrefixes);
        app.UseTechStrapErrorPages();
        app.MapGet("/t/page", (HttpContext context) =>
        {
            // What a page might set for itself: the shared middleware would overwrite the policy, and the rule must overwrite both.
            context.Response.Headers.CacheControl = "max-age=60";
            context.Response.Headers["Referrer-Policy"] = "unsafe-url";
            return "page";
        });
        app.MapGet("/t/{token}/attachments/{id}", (string id) => id == "missing" ? Results.NotFound() : Results.Text("file"));
        app.MapGet("/other", () => "other");
        app.MapGet("/not-found", () => "the not-found page");
    }

    private static string[] Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? [.. values] : [];

    private static string[] Policy(HttpResponseMessage response) => Header(response, "Content-Security-Policy").Single().Split(';', StringSplitOptions.TrimEntries);

    [Fact]
    public async Task A_rule_overrides_what_the_shared_headers_set_and_what_the_endpoint_set()
    {
        await using var app = await StartAsync(a => Pipeline(a, Rules()));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/t/page", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Header(response, "Referrer-Policy").ShouldBe(["no-referrer"], "the shared middleware's value and the endpoint's own value must both lose");
        Header(response, "Cache-Control").ShouldBe(["no-store"]);
        Header(response, "X-Robots-Tag").ShouldBe(["noindex"]);
        Policy(response).ShouldNotContain("sandbox", "only the attachment route is sandboxed, so a page keeps the normal policy");
        Policy(response).ShouldContain("default-src 'self'");
    }

    [Fact]
    public async Task A_path_no_rule_matches_keeps_the_shared_headers_exactly()
    {
        await using var app = await StartAsync(a => Pipeline(a, Rules()));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/other", Ct);

        Header(response, "Referrer-Policy").ShouldBe(["strict-origin-when-cross-origin"]);
        Header(response, "Cache-Control").ShouldBeEmpty();
        Header(response, "X-Robots-Tag").ShouldBeEmpty();
        Policy(response).ShouldNotContain("sandbox");
    }

    [Theory]
    [InlineData("/t/page")]
    [InlineData("/T/PAGE")]
    [InlineData("/t/page/")]
    public async Task A_rule_matches_without_regard_to_case_or_a_trailing_slash(string path)
    {
        await using var app = await StartAsync(a => Pipeline(a, Rules()));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(path, Ct);

        Header(response, "Referrer-Policy").ShouldBe(["no-referrer"], path);
        Header(response, "Cache-Control").ShouldBe(["no-store"], path);
    }

    [Fact]
    public async Task The_sandbox_rule_appends_a_bare_sandbox_directive_to_a_delivered_file_only()
    {
        await using var app = await StartAsync(a => Pipeline(a, Rules()));
        using var client = app.GetTestClient();

        using var file = await client.GetAsync("/t/abc/attachments/1", Ct);
        using var page = await client.GetAsync("/t/page", Ct);

        Policy(file).Count(d => d == "sandbox").ShouldBe(1);
        Policy(file).ShouldContain("default-src 'self'", "the page policy is still there, with sandbox on top");
        Header(file, "Referrer-Policy").ShouldBe(["no-referrer"], "the file is under /t, so the /t rule applies to it as well");
        Policy(page).ShouldNotContain("sandbox");
    }

    [Fact]
    public async Task The_sandbox_rule_does_not_sandbox_an_error_answer_but_the_other_rules_still_apply_to_it()
    {
        await using var app = await StartAsync(a => Pipeline(a, Rules()));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/t/abc/attachments/missing", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        Policy(response).ShouldNotContain("sandbox");
        Header(response, "Cache-Control").ShouldBe(["no-store"]);
    }

    // The 404 for an unknown /t path is re-executed at /not-found, where the request path is no longer /t/...: the rule must be decided from the path the visitor asked for.
    [Fact]
    public async Task The_rules_apply_to_the_re_executed_404_page_of_a_matching_path_and_not_to_other_404s()
    {
        await using var app = await StartAsync(a => Pipeline(a, Rules()));
        using var client = app.GetTestClient();

        using var ticket = await client.GetAsync("/t/no-such-thing", Ct);
        using var elsewhere = await client.GetAsync("/no-such-thing", Ct);

        ticket.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ticket.Content.ReadAsStringAsync(Ct)).ShouldBe("the not-found page");
        Header(ticket, "Referrer-Policy").ShouldBe(["no-referrer"]);
        Header(ticket, "Cache-Control").ShouldBe(["no-store"]);
        Header(ticket, "X-Robots-Tag").ShouldBe(["noindex"]);
        elsewhere.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        Header(elsewhere, "Referrer-Policy").ShouldBe(["strict-origin-when-cross-origin"]);
        Header(elsewhere, "Cache-Control").ShouldBeEmpty();
    }

    [Fact]
    public async Task Several_matching_rules_apply_in_the_order_given_and_a_later_one_wins()
    {
        IReadOnlyList<PathHeaderRule> rules =
        [
            PathHeaderRule.Set(UnderT, ("X-Probe", "first"), ("Cache-Control", "no-store")),
            PathHeaderRule.Set(UnderT, ("X-Probe", "second")),
        ];
        await using var app = await StartAsync(a => Pipeline(a, rules));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/t/page", Ct);

        Header(response, "X-Probe").ShouldBe(["second"]);
        Header(response, "Cache-Control").ShouldBe(["no-store"]);
    }

    [Fact]
    public async Task The_download_prefix_argument_still_works_as_a_sandbox_rule_without_any_other_rule()
    {
        await using var app = await StartAsync(a => Pipeline(a, [], "/t"));
        using var client = app.GetTestClient();

        using var file = await client.GetAsync("/t/abc/attachments/1", Ct);
        using var other = await client.GetAsync("/other", Ct);

        Policy(file).Count(d => d == "sandbox").ShouldBe(1);
        Policy(other).ShouldNotContain("sandbox");
        Header(file, "Referrer-Policy").ShouldBe(["strict-origin-when-cross-origin"], "no header rule was given");
    }

    [Fact]
    public async Task The_existing_signature_without_rules_is_unchanged()
    {
        await using var app = await StartAsync(a =>
        {
            a.UseTechStrapWebHost("/t");
            a.MapGet("/t/{token}/attachments/{id}", () => "file");
            a.MapGet("/other", () => "other");
        });
        using var client = app.GetTestClient();

        using var file = await client.GetAsync("/t/abc/attachments/1", Ct);
        using var other = await client.GetAsync("/other", Ct);

        Policy(file).Count(d => d == "sandbox").ShouldBe(1);
        Policy(other).ShouldNotContain("sandbox");
    }

    [Fact]
    public void A_rule_needs_a_predicate_and_at_least_one_header()
    {
        Should.Throw<ArgumentNullException>(() => PathHeaderRule.Set(null!, ("A", "b")));
        Should.Throw<ArgumentNullException>(() => PathHeaderRule.Sandbox(null!));
        Should.Throw<ArgumentException>(() => PathHeaderRule.Set(UnderT));
        Should.Throw<ArgumentException>(() => PathHeaderRule.Set(UnderT, (" ", "b")));
    }
}
```

`tests/TechStrap.Portal.Tests/Api/OkProbeStartupFilter.cs`

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace TechStrap.Portal.Tests.Api;

/// <summary>
/// Answers 200 "probe" for the paths a test names, after the whole application pipeline (so only a path no Portal endpoint matched reaches it). The Portal has no 200 answer under <c>/t</c>
/// until PHASE-09b, and the header rules for a delivered page or file must be tested on a real 200 response: a probe path is chosen so that no later Portal route can match it.
/// </summary>
internal sealed class OkProbeStartupFilter(Func<PathString, bool> answers) : IStartupFilter
{
    public static Action<IServiceCollection> Add(Func<PathString, bool> answers) => services => services.AddSingleton<IStartupFilter>(new OkProbeStartupFilter(answers));

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        app.Use(async (context, nextMiddleware) =>
        {
            if (!answers(context.Request.Path))
            {
                await nextMiddleware(context);
                return;
            }

            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync("probe");
        });
    };
}
```

`tests/TechStrap.Portal.Tests/Headers/PortalHeaderRulesTests.cs`

```csharp
using Microsoft.AspNetCore.Http;
using TechStrap.Portal.Headers;

namespace TechStrap.Portal.Tests.Headers;

/// <summary>The two path predicates behind the Portal's header rules: everything under <c>/t</c>, and only the attachment route under it.</summary>
public sealed class PortalHeaderRulesTests
{
    private const string Token = "AbC-_0123456789AbC-_0123456789AbC-_01234567";
    private const string Id = "11111111-2222-3333-4444-555555555555";

    [Theory]
    [InlineData("/t")]
    [InlineData("/t/")]
    [InlineData("/T")]
    [InlineData("/t/" + Token)]
    [InlineData("/T/" + Token + "/")]
    [InlineData("/t/" + Token + "/attachments/" + Id)]
    [InlineData("/t/" + Token + "/anything/at/all")]
    [InlineData("/t//" + Token)]
    public void A_path_under_t_is_a_ticket_path(string path) => PortalHeaderRules.IsTicketPath(path).ShouldBeTrue(path);

    [Theory]
    [InlineData("/")]
    [InlineData("/tx")]
    [InlineData("/t-x")]
    [InlineData("/ticket")]
    [InlineData("/p/paperplane")]
    [InlineData("/p/t/x")]
    [InlineData("/not-found")]
    [InlineData("/robots.txt")]
    [InlineData("")]
    public void Any_other_path_is_not(string path) => PortalHeaderRules.IsTicketPath(path).ShouldBeFalse(path);

    [Theory]
    [InlineData("/t/" + Token + "/attachments/" + Id)]
    [InlineData("/T/" + Token + "/ATTACHMENTS/" + Id)]
    [InlineData("/t/" + Token + "/attachments/" + Id + "/")]
    [InlineData("/t/" + Token + "/attachments/not-a-guid")]
    public void The_attachment_route_under_t_is_an_attachment_path(string path) => PortalHeaderRules.IsTicketAttachmentPath(path).ShouldBeTrue(path);

    [Theory]
    [InlineData("/t")]
    [InlineData("/t/" + Token)]
    [InlineData("/t/" + Token + "/")]
    [InlineData("/t/" + Token + "/attachments")]
    [InlineData("/t/" + Token + "/attachments/")]
    [InlineData("/t/" + Token + "/attachments/" + Id + "/more")]
    [InlineData("/t/" + Token + "/files/" + Id)]
    [InlineData("/t/attachments/" + Id)]
    [InlineData("/attachments/" + Id)]
    [InlineData("/p/paperplane/attachments/" + Id)]
    public void The_ticket_page_and_everything_else_is_not(string path) => PortalHeaderRules.IsTicketAttachmentPath(path).ShouldBeFalse(path);

    [Fact]
    public void The_rules_are_the_ticket_headers_and_the_attachment_sandbox_and_nothing_else()
    {
        PortalHeaderRules.Rules.Count.ShouldBe(2);
        PortalHeaderRules.Rules[0].Matches(new PathString("/t/x")).ShouldBeTrue();
        PortalHeaderRules.Rules[0].Matches(new PathString("/p/x")).ShouldBeFalse();
        PortalHeaderRules.Rules[1].Matches(new PathString("/t/" + Token + "/attachments/" + Id)).ShouldBeTrue();
        PortalHeaderRules.Rules[1].Matches(new PathString("/t/" + Token)).ShouldBeFalse();
    }
}
```

`tests/TechStrap.Portal.Tests/Headers/TicketHeaderHostTests.cs`

```csharp
using System.Net;
using Microsoft.AspNetCore.Http;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Headers;

/// <summary>
/// Review Focus 2 at the host: whatever the Portal answers under <c>/t</c> carries <c>Referrer-Policy: no-referrer</c>, <c>Cache-Control: no-store</c> and <c>X-Robots-Tag: noindex</c>, and
/// only the attachment route is sandboxed, so the ticket page keeps the normal policy. The shared security-header middleware overwrites a value a page sets itself; these assert the final
/// response. There is no ticket page until PHASE-09b, so the 404 for an unknown <c>/t</c> path is checked (its headers must apply anyway), and a probe answers 200 on paths no later route can match.
/// </summary>
public sealed class TicketHeaderHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string Token = "AbC-_0123456789AbC-_0123456789AbC-_01234567";
    private static readonly string AttachmentPath = $"/t/{Token}/attachments/{Guid.NewGuid()}";

    // A page under /t that no Portal route will ever match (the ticket page is /t/{token}), and the real shape of the attachment route with an id the pass-through (a Guid route) cannot match.
    private static readonly Action<Microsoft.Extensions.DependencyInjection.IServiceCollection> Probes = OkProbeStartupFilter.Add(path =>
        path.StartsWithSegments("/t/probe-token/probe-page") || path.StartsWithSegments("/t/probe-token/attachments/probe-id"));

    private static string[] Header(HttpResponseMessage response, string name) => response.Headers.TryGetValues(name, out var values) ? [.. values] : [];

    private static string[] Policy(HttpResponseMessage response) => Header(response, "Content-Security-Policy").Single().Split(';', StringSplitOptions.TrimEntries);

    private static void AssertTicketHeaders(HttpResponseMessage response, string where)
    {
        Header(response, "Referrer-Policy").ShouldBe(["no-referrer"], where);
        Header(response, "Cache-Control").ShouldBe(["no-store"], where);
        Header(response, "X-Robots-Tag").ShouldBe(["noindex"], where);
        Header(response, "X-Content-Type-Options").ShouldBe(["nosniff"], where);
        Header(response, "X-Frame-Options").ShouldBe(["DENY"], where);
    }

    [Theory]
    [InlineData("/t/x")]
    [InlineData("/T/X")]
    [InlineData("/t")]
    [InlineData("/t/")]
    [InlineData("/t/AbC-_0123456789AbC-_0123456789AbC-_01234567")]
    public async Task An_unknown_ticket_path_is_the_neutral_404_with_the_ticket_headers(string path)
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
        html.ShouldContain("Page not found");
        AssertTicketHeaders(response, path);
        Policy(response).ShouldNotContain("sandbox", "the not-found page is an ordinary page");
        Policy(response).ShouldContain("script-src 'self'");
    }

    [Fact]
    public async Task A_delivered_page_under_t_has_the_ticket_headers_and_keeps_the_normal_policy()
    {
        await using var factory = new PortalFactory(configureServices: Probes);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/t/probe-token/probe-page", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("probe", "the probe must really have answered, or this proves nothing");
        AssertTicketHeaders(response, "page");
        Policy(response).ShouldNotContain("sandbox");
        Policy(response).ShouldContain("script-src 'self'");
        Policy(response).ShouldContain("form-action 'self'");
    }

    [Fact]
    public async Task A_delivered_attachment_has_the_ticket_headers_and_a_sandbox_on_top_of_the_page_policy()
    {
        await using var factory = new PortalFactory(configureServices: Probes);
        using var client = factory.CreateClient();

        // The probe answers the attachment shape; the id is not a guid, which a later pass-through route will not match, so the response is the probe's 200.
        using var response = await client.GetAsync("/t/probe-token/attachments/probe-id", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("probe");
        AssertTicketHeaders(response, "attachment");
        Policy(response).Count(directive => directive == "sandbox").ShouldBe(1);
        Policy(response).ShouldContain("script-src 'self'", "the page policy is still there, with sandbox on top");
    }

    [Fact]
    public async Task A_missing_attachment_is_the_404_page_with_the_ticket_headers_and_is_not_sandboxed()
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(AttachmentPath, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        AssertTicketHeaders(response, "missing attachment");
        Policy(response).ShouldNotContain("sandbox");
    }

    [Theory]
    [InlineData("/p/paperplane/contact", HttpStatusCode.NotFound)]
    [InlineData("/p/paperplane/kb/guides/dark-mode", HttpStatusCode.NotFound)]
    [InlineData("/no-such-page", HttpStatusCode.NotFound)]
    [InlineData("/not-found", HttpStatusCode.OK)]
    [InlineData("/error", HttpStatusCode.OK)]
    [InlineData("/tx", HttpStatusCode.NotFound)]
    [InlineData("/ticket/x", HttpStatusCode.NotFound)]
    [InlineData("/health/live", HttpStatusCode.OK)]
    public async Task Everything_outside_t_keeps_the_shared_headers_and_is_never_sandboxed_or_forced_no_store(string path, HttpStatusCode status)
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(status, path);
        Header(response, "Referrer-Policy").ShouldBe(["strict-origin-when-cross-origin"], path);
        Header(response, "X-Robots-Tag").ShouldBeEmpty(path);
        Header(response, "Cache-Control").ShouldNotContain("no-store", path);
        Policy(response).ShouldNotContain("sandbox", path);
    }

    [Fact]
    public async Task The_header_rules_do_not_depend_on_the_environment()
    {
        foreach (var environment in new[] { "Development", "Production" })
        {
            await using var factory = new PortalFactory(environment, configureServices: Probes);
            using var client = factory.CreateClient();

            using var page = await client.GetAsync("/t/probe-token/probe-page", Ct);
            using var missing = await client.GetAsync("/t/x", Ct);

            AssertTicketHeaders(page, environment + " page");
            AssertTicketHeaders(missing, environment + " 404");
        }
    }
}
```

`tests/TechStrap.Portal.Tests/Seo/SeoHostTests.cs`

```csharp
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SyntaxCircus.Blazor.Seo;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Tests.Seo;

/// <summary>
/// P09-T04 (09a part): <c>SyntaxCircus.Blazor.Seo</c> with its real API. <c>Seo:BaseUrl</c> is derived from <c>TECHSTRAP_PORTAL_PUBLIC_URL</c> (one setting for one value), robots.txt disallows the ticket
/// pages, and the canonical-host redirect is an allow-list that does nothing while it is not configured. The sitemap is not mapped until PHASE-09c (it needs the products endpoint), which a test pins.
/// </summary>
public sealed class SeoHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Robots_txt_disallows_the_ticket_pages_and_names_the_sitemap_under_the_public_address()
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/robots.txt", Ct);
        var lines = (await response.Content.ReadAsStringAsync(Ct)).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
        lines.ShouldBe(["User-agent: *", "Allow: /", "Disallow: /t/", $"Sitemap: {PortalFactory.PublicUrl}/sitemap.xml"]);
    }

    [Fact]
    public async Task Robots_txt_is_served_in_Production_too_and_in_Development_without_a_public_url()
    {
        await using var production = new PortalFactory("Production");
        await using var development = new PortalFactory("Development", new Dictionary<string, string?> { [PortalOptions.PublicUrlKey] = "" });
        using var productionClient = production.CreateClient();
        using var developmentClient = development.CreateClient();

        var prod = await productionClient.GetStringAsync("/robots.txt", Ct);
        var dev = await developmentClient.GetStringAsync("/robots.txt", Ct);

        prod.ShouldContain("Disallow: /t/");
        dev.ShouldContain("Disallow: /t/");
    }

    [Fact]
    public async Task The_sitemap_is_not_mapped_in_09a()
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/sitemap.xml", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, "09c maps the sitemap once the products endpoint exists; until then robots.txt names a sitemap that answers 404");
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
    }

    [Theory]
    [InlineData("https://support.example.com", "https://support.example.com")]
    [InlineData("https://support.example.com/", "https://support.example.com")]
    [InlineData("http://localhost:8082", "http://localhost:8082")]
    public async Task The_seo_base_url_is_the_public_url_without_a_trailing_slash(string publicUrl, string expected)
    {
        await using var factory = new PortalFactory(settings: new Dictionary<string, string?> { [PortalOptions.PublicUrlKey] = publicUrl });
        using var client = factory.CreateClient();

        factory.Services.GetRequiredService<IOptions<SeoOptions>>().Value.BaseUrl.ShouldBe(expected);
    }

    [Fact]
    public async Task A_seo_base_url_set_by_hand_never_wins_over_the_public_url()
    {
        await using var factory = new PortalFactory(settings: new Dictionary<string, string?> { ["Seo:BaseUrl"] = "https://elsewhere.example.com" });
        using var client = factory.CreateClient();

        factory.Services.GetRequiredService<IOptions<SeoOptions>>().Value.BaseUrl.ShouldBe(PortalFactory.PublicUrl);
        (await client.GetStringAsync("/robots.txt", Ct)).ShouldContain($"Sitemap: {PortalFactory.PublicUrl}/sitemap.xml");
    }

    private static PortalFactory Canonical(bool forceHttps = false, string? canonical = "portal.example.com", string environment = "Development") => new(
        environment,
        new Dictionary<string, string?>
        {
            ["CanonicalHost:CanonicalHost"] = canonical,
            ["CanonicalHost:LegacyHosts:0"] = "old.example.com",
            ["CanonicalHost:LegacyHosts:1"] = "www.old.example.com",
            ["CanonicalHost:ForceHttps"] = forceHttps ? "true" : "false",
        });

    private static async Task<HttpResponseMessage> GetAsync(PortalFactory factory, string host, string path)
    {
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = host;
        return await client.SendAsync(request, Ct);
    }

    [Theory]
    [InlineData("old.example.com")]
    [InlineData("WWW.OLD.EXAMPLE.COM")]
    public async Task A_legacy_host_is_redirected_permanently_to_the_canonical_host_with_its_path_and_query(string host)
    {
        await using var factory = Canonical();

        using var response = await GetAsync(factory, host, "/p/paperplane/contact?subject=Hi&name=Jo");

        response.StatusCode.ShouldBe(HttpStatusCode.MovedPermanently);
        response.Headers.Location!.ToString().ShouldBe("http://portal.example.com/p/paperplane/contact?subject=Hi&name=Jo");
    }

    [Fact]
    public async Task Force_https_redirects_to_https()
    {
        await using var factory = Canonical(forceHttps: true);

        using var response = await GetAsync(factory, "old.example.com", "/p/paperplane");

        response.Headers.Location!.ToString().ShouldBe("https://portal.example.com/p/paperplane");
    }

    [Theory]
    [InlineData("portal.example.com")]
    [InlineData("localhost")]
    [InlineData("anything.else.example")]
    public async Task A_host_that_is_not_a_legacy_host_is_never_redirected_because_the_list_is_an_allow_list(string host)
    {
        await using var factory = Canonical();

        using var response = await GetAsync(factory, host, "/robots.txt");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Location.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task With_no_canonical_host_configured_nothing_is_redirected_even_when_legacy_hosts_are_listed(string? canonical)
    {
        await using var factory = Canonical(canonical: canonical);

        using var response = await GetAsync(factory, "old.example.com", "/robots.txt");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Location.ShouldBeNull();
    }

    [Fact]
    public async Task Nothing_is_configured_by_default_so_no_host_is_redirected_in_Development()
    {
        await using var factory = new PortalFactory();

        using var response = await GetAsync(factory, "old.example.com", "/robots.txt");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_redirect_target_is_the_configured_host_never_the_requested_one()
    {
        await using var factory = Canonical();

        using var response = await GetAsync(factory, "old.example.com", "//evil.example/path");

        response.Headers.Location!.Host.ShouldBe("portal.example.com");
    }
}
```

The config contract learns the blank canonical host, and the env-completeness test lists the four `CanonicalHost` keys.

`scripts/tests/ConfigContract.Tests.ps1`

```diff
--- scripts/tests/ConfigContract.Tests.ps1
+++ scripts/tests/ConfigContract.Tests.ps1
@@ -43,7 +43,7 @@ BeforeAll {
         Api    = $script:CommonBlank + @('SECURITYHEADERS__ROBOTSTAG', 'CONNECTIONSTRINGS__TECHSTRAP', 'AUTHENTICATION__JWTBEARER__AUTHORITY', 'TECHSTRAP_PORTAL_PUBLIC_URL', 'TECHSTRAP_API_PUBLIC_URL', 'TECHSTRAP_ADMIN_PUBLIC_URL', 'STORAGE__LOCAL__ROOTPATH')
         Worker = $script:CommonBlank + @('CONNECTIONSTRINGS__TECHSTRAP', 'EMAIL__SMTP__HOST', 'EMAIL__SMTP__USERNAME', 'EMAIL__SMTP__PASSWORD', 'EMAIL__SMTP__DEFAULTFROM', 'EMAIL__SMTP__TLSMODE', 'EMAIL__SMTP__TOTALSENDTIMEOUT', 'EMAILOUTBOX__WORKERID')
         Admin  = $script:CommonBlank + @('SECURITYHEADERS__ROBOTSTAG', 'API__BASEURL', 'AUTH__AUTHORITY', 'AUTH__CLIENTID', 'AUTH__CLIENTSECRET', 'DATAPROTECTION__KEYRINGPATH', 'TECHSTRAP_PORTAL_PUBLIC_URL')
-        Portal = $script:CommonBlank + @('SECURITYHEADERS__ROBOTSTAG', 'API__BASEURL', 'DATAPROTECTION__KEYRINGPATH', 'TECHSTRAP_PORTAL_PUBLIC_URL', 'TECHSTRAP_PORTAL_DEFAULT_PRODUCT')
+        Portal = $script:CommonBlank + @('SECURITYHEADERS__ROBOTSTAG', 'API__BASEURL', 'DATAPROTECTION__KEYRINGPATH', 'TECHSTRAP_PORTAL_PUBLIC_URL', 'TECHSTRAP_PORTAL_DEFAULT_PRODUCT', 'CANONICALHOST__CANONICALHOST')
     }
     # A key containing one of these words is secret-shaped: its committed value must be blank, whatever the value looks like (a numeric password is still a password).
     # OPENTELEMETRY__HEADERS is added because the OTLP headers carry a token. KeyRingPath is a directory, not a key.
```

`tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs`

```diff
--- tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs
+++ tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs
@@ -129,6 +129,10 @@ public sealed partial class EnvExampleCompletenessTests
                 "API__BASEURL",
                 "TECHSTRAP_PORTAL_PUBLIC_URL",
                 "TECHSTRAP_PORTAL_DEFAULT_PRODUCT",
+                "CANONICALHOST__CANONICALHOST",
+                "CANONICALHOST__LEGACYHOSTS__0",
+                "CANONICALHOST__FORCEHTTPS",
+                "CANONICALHOST__PERMANENT",
                 "TECHSTRAP_PORTAL_SHOW_POWERED_BY",
                 "DATAPROTECTION__KEYRINGPATH",
             ]
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet build tests/TechStrap.Api.Tests -c Release`
Expected: FAIL to compile: `error CS0246: The type or namespace name 'PathHeaderRule' could not be found` (twice, in `PathHeaderRuleHostTests.cs`).

Run: `dotnet build tests/TechStrap.Portal.Tests -c Release`
Expected: FAIL to compile: `error CS0234: The type or namespace name 'Headers' does not exist in the namespace 'TechStrap.Portal'` (and the `TechStrap.Portal.Seo` types).

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ConfigContract.Tests.ps1 -Output Minimal`
Expected: FAIL: `Tests Passed: 85, Failed: 1` (`CANONICALHOST__CANONICALHOST` is expected blank but is not in `appsettings.json`).

- [ ] **Step 3: Implement the Hosting rules**

The shared security-headers middleware sets `Referrer-Policy` and the Content-Security-Policy when the response starts, so a value an endpoint sets itself is overwritten. The rules middleware is registered before it, and start callbacks run last-registered-first, so it runs after it and wins. The matching rules are chosen **before** `next()`, because a 404 is re-executed at `/not-found`, where `Request.Path` is no longer the path the visitor asked for.

`src/TechStrap.Hosting/Wiring/PathHeaderRule.cs`

```csharp
using Microsoft.AspNetCore.Http;

namespace TechStrap.Hosting.Wiring;

/// <summary>
/// A response-header rule for the requests whose path matches (D-045). <see cref="BrowserHostExtensions.UseTechStrapWebHost(Microsoft.AspNetCore.Builder.WebApplication, IReadOnlyList{PathHeaderRule}, string[])"/>
/// applies the rules after the shared security headers, in the order given, so a rule's value wins over the shared one and over a value the endpoint set itself (the shared middleware
/// would overwrite an endpoint's <c>Referrer-Policy</c> or Content-Security-Policy when the response starts).
/// The path is decided from the request as it arrived, before any re-execution, so the page that replaces a 404 under a matching path carries the rule's headers as well.
/// </summary>
public sealed class PathHeaderRule
{
    private PathHeaderRule(Func<PathString, bool> matches, IReadOnlyList<(string Name, Func<string, string> Change)> changes, bool successOnly)
    {
        Matches = matches;
        Changes = changes;
        SuccessOnly = successOnly;
    }

    /// <summary>True for the request path this rule is for. Called once per request, with the path as it arrived.</summary>
    public Func<PathString, bool> Matches { get; }

    /// <summary>The header changes, in order: the header name and a function from the current value (empty when the header is absent) to the new one.</summary>
    internal IReadOnlyList<(string Name, Func<string, string> Change)> Changes { get; }

    /// <summary>When true the changes are made to a successful (2xx) response only.</summary>
    internal bool SuccessOnly { get; }

    /// <summary>Sets each header to the given value on every response for a matching path, whatever status it has.</summary>
    public static PathHeaderRule Set(Func<PathString, bool> matches, params (string Name, string Value)[] headers)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(headers);
        if (headers.Length == 0 || headers.Any(h => string.IsNullOrWhiteSpace(h.Name)))
        {
            throw new ArgumentException("A rule sets at least one header, and every header has a name.", nameof(headers));
        }

        return new PathHeaderRule(matches, [.. headers.Select(h => (h.Name, (Func<string, string>)(_ => h.Value)))], successOnly: false);
    }

    /// <summary>
    /// Adds a bare <c>sandbox</c> directive to the Content-Security-Policy of a successful (2xx) response for a matching path, so a file that is opened rather than saved cannot run script in the
    /// app's origin. An error answer is an ordinary app page that needs its script, and the full page policy still applies to it.
    /// </summary>
    public static PathHeaderRule Sandbox(Func<PathString, bool> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        return new PathHeaderRule(matches, [("Content-Security-Policy", BrowserHostExtensions.WithSandbox)], successOnly: true);
    }
}
```

`src/TechStrap.Hosting/Wiring/BrowserHostExtensions.cs`

```diff
--- src/TechStrap.Hosting/Wiring/BrowserHostExtensions.cs
+++ src/TechStrap.Hosting/Wiring/BrowserHostExtensions.cs
@@ -54,29 +54,47 @@ public static class BrowserHostExtensions
     /// </summary>
     /// <param name="downloadPathPrefixes">
     /// Paths that stream a user's file (the Admin's <c>/attachments</c>). Their successful (2xx) responses get <c>sandbox</c> appended to the Content-Security-Policy, so a file that is
-    /// opened rather than saved cannot run script in the app's origin. The shared security-headers middleware sets the policy when the response starts and would
-    /// overwrite a value the endpoint set itself; this step is registered before it, and start callbacks run last-registered-first, so it runs after it and appends.
+    /// opened rather than saved cannot run script in the app's origin. It is <see cref="PathHeaderRule.Sandbox"/> over a path prefix; see the overload with rules for how it is applied.
     /// </param>
-    public static WebApplication UseTechStrapWebHost(this WebApplication app, params string[] downloadPathPrefixes)
+    public static WebApplication UseTechStrapWebHost(this WebApplication app, params string[] downloadPathPrefixes) => app.UseTechStrapWebHost([], downloadPathPrefixes);
+
+    /// <summary>
+    /// The same, with per-path header rules (D-045). The shared security-headers middleware sets <c>Referrer-Policy</c> and the Content-Security-Policy when the response starts and would
+    /// overwrite a value an endpoint set itself; the rules are applied by a step registered before it, and start callbacks run last-registered-first, so they run after it and win. Each
+    /// request's matching rules are decided from the path as it arrived (before a 404 is re-executed), and are applied in the order given: first the rules, then the download prefixes.
+    /// </summary>
+    /// <param name="rules">Header rules for paths, for example the Portal's <c>/t/*</c> headers. May be empty.</param>
+    /// <param name="downloadPathPrefixes">As in the overload without rules.</param>
+    public static WebApplication UseTechStrapWebHost(this WebApplication app, IReadOnlyList<PathHeaderRule> rules, params string[] downloadPathPrefixes)
     {
         ArgumentNullException.ThrowIfNull(app);
+        ArgumentNullException.ThrowIfNull(rules);
         app.UseForwardedHeaders();
         app.UseCorrelationId();
+        var applied = new List<PathHeaderRule>(rules);
         if (downloadPathPrefixes.Length > 0)
         {
             var prefixes = downloadPathPrefixes.Select(prefix => new PathString(prefix)).ToArray();
+            applied.Add(PathHeaderRule.Sandbox(path => prefixes.Any(prefix => path.StartsWithSegments(prefix))));
+        }
+
+        if (applied.Count > 0)
+        {
             app.Use(async (context, next) =>
             {
-                if (prefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix)))
+                var matched = applied.Where(rule => rule.Matches(context.Request.Path)).ToArray();
+                if (matched.Length > 0)
                 {
                     context.Response.OnStarting(() =>
                     {
-                        // Only a delivered file (2xx) is sandboxed, as in the Api's AttachmentSandbox. An error answer (the 404 page is re-executed on this path) is an ordinary app page
-                        // that needs its script, and the full page policy still applies to it.
-                        if (context.Response.StatusCode is >= StatusCodes.Status200OK and < StatusCodes.Status300MultipleChoices)
+                        var headers = context.Response.Headers;
+                        var delivered = context.Response.StatusCode is >= StatusCodes.Status200OK and < StatusCodes.Status300MultipleChoices;
+                        foreach (var rule in matched.Where(rule => delivered || !rule.SuccessOnly))
                         {
-                            var headers = context.Response.Headers;
-                            headers.ContentSecurityPolicy = WithSandbox(headers.ContentSecurityPolicy.ToString());
+                            foreach (var (name, change) in rule.Changes)
+                            {
+                                headers[name] = change(headers[name].ToString());
+                            }
                         }
 
                         return Task.CompletedTask;
```

- [ ] **Step 4: Implement the Portal rules and the Seo wiring**

`IsTicketAttachmentPath` is exactly three segments after `/t` with the middle one `attachments`, so the ticket page keeps the normal policy. The canonical-host redirect is the package's allow-list (`CanonicalHost:LegacyHosts`); nothing happens until it is configured.

`src/TechStrap.Portal/Headers/PortalHeaderRules.cs`

```csharp
using Microsoft.AspNetCore.Http;
using TechStrap.Hosting.Wiring;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Headers;

/// <summary>
/// The Portal's per-path response headers (D-045), applied by <c>UseTechStrapWebHost</c> after the shared security headers so nothing a page sets can undo them.
/// Everything under <c>/t</c> (the ticket page, its attachments, and the 404 that replaces an unknown ticket path) is a private page: the access token is in its address, so the browser must send no
/// referrer from it, keep no copy of it and not index it. Only the attachment route is sandboxed, because the ticket page itself is an ordinary page that needs its normal policy.
/// </summary>
internal static class PortalHeaderRules
{
    public const string ReferrerPolicy = "no-referrer";
    public const string CacheControl = "no-store";
    public const string RobotsTag = "noindex";

    /// <summary>The rules for <c>UseTechStrapWebHost</c>: the ticket headers, then the attachment sandbox.</summary>
    public static IReadOnlyList<PathHeaderRule> Rules { get; } =
    [
        PathHeaderRule.Set(IsTicketPath, ("Referrer-Policy", ReferrerPolicy), ("Cache-Control", CacheControl), ("X-Robots-Tag", RobotsTag)),
        PathHeaderRule.Sandbox(IsTicketAttachmentPath),
    ];

    /// <summary><c>/t</c> and everything under it, without regard to case or a trailing slash.</summary>
    public static bool IsTicketPath(PathString path) => path.StartsWithSegments(PortalRoutes.TicketPrefix);

    /// <summary>Exactly <c>/t/{token}/attachments/{id}</c>: the three segments after <c>/t</c>, the middle one <c>attachments</c>.</summary>
    public static bool IsTicketAttachmentPath(PathString path) =>
        path.StartsWithSegments(PortalRoutes.TicketPrefix, out var rest)
        && rest.Value!.Split('/', StringSplitOptions.RemoveEmptyEntries) is [_, var middle, _]
        && middle.Equals("attachments", StringComparison.OrdinalIgnoreCase);
}
```

`src/TechStrap.Portal/Seo/PortalSeoRegistration.cs`

```csharp
using Microsoft.Extensions.Options;
using SyntaxCircus.Blazor.Seo;
using TechStrap.Portal.Routing;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Seo;

/// <summary>
/// <c>SyntaxCircus.Blazor.Seo</c> in the Portal (D-045). <c>Seo:BaseUrl</c> is derived from <c>TECHSTRAP_PORTAL_PUBLIC_URL</c>, always, so there is one setting for one value and a stray
/// <c>Seo__BaseUrl</c> can never disagree with it. robots.txt disallows the ticket pages. The sitemap is not mapped in 09a: it needs the products endpoint PHASE-09c adds.
/// </summary>
public static class PortalSeoRegistration
{
    /// <summary>The robots.txt line that keeps crawlers away from every ticket page (the token is in the address).</summary>
    public const string DisallowTickets = $"Disallow: {PortalRoutes.TicketPrefix}/";

    public static IServiceCollection AddPortalSeo(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSyntaxCircusSeo(configuration);
        services.AddOptions<SeoOptions>().PostConfigure<IOptions<PortalOptions>>((seo, portal) => seo.BaseUrl = portal.Value.PublicBaseUrl);
        return services;
    }

    /// <summary>The canonical-host redirect (an allow-list; nothing happens until <c>CanonicalHost:CanonicalHost</c> and <c>LegacyHosts</c> are set) and the search-indexing headers.</summary>
    public static WebApplication UsePortalSeo(this WebApplication app)
    {
        app.UseSyntaxCircusSeo();
        return app;
    }

    /// <summary>robots.txt (and, from 09c, the sitemap).</summary>
    public static WebApplication MapPortalSeo(this WebApplication app)
    {
        app.MapSeoRobotsTxt(extraDirectives: [DisallowTickets]);
        return app;
    }
}
```

`src/TechStrap.Portal/Program.cs`

```diff
--- src/TechStrap.Portal/Program.cs
+++ src/TechStrap.Portal/Program.cs
@@ -5,6 +5,8 @@ using TechStrap.Hosting.Wiring;
 using TechStrap.Portal.Clients;
 using TechStrap.Portal.Components;
 using TechStrap.Portal.Components.Ui;
+using TechStrap.Portal.Headers;
+using TechStrap.Portal.Seo;
 using TechStrap.Portal.Settings;
 
 const string ServiceName = "techstrap-portal";
@@ -30,6 +32,9 @@ builder.Services.AddPortalOptions();
 
 // The two named API clients (reads retried, writes never) and the typed clients: every call forwards the visitor's address (D-019, D-045).
 builder.Services.AddPortalApiClients();
+
+// Meta tags, robots.txt and the canonical-host redirect; Seo:BaseUrl comes from the public address (D-045).
+builder.Services.AddPortalSeo(builder.Configuration);
 // Installation-wide switch for the "Powered by TechStrap" footer (D-024); shown unless set to false.
 // A value that is not true or false fails at startup rather than breaking every page.
 builder.Services.AddOptions<PoweredByOptions>()
@@ -44,11 +49,14 @@ builder.Services.AddOptions<PoweredByOptions>()
 var app = builder.Build();
 telemetry.LogStartupWarning(app.Logger);
 
-// Forwarded headers, correlation id and security headers first, then the plain error page and the not-found page (only 404 is re-executed).
-app.UseTechStrapWebHost();
+// Forwarded headers, correlation id and security headers first (with the ticket pages' own header rules, which win over the shared ones), then the canonical-host redirect, then the
+// plain error page and the not-found page (only 404 is re-executed).
+app.UseTechStrapWebHost(PortalHeaderRules.Rules);
+app.UsePortalSeo();
 app.UseTechStrapErrorPages();
 app.UseAntiforgery();
 app.MapStandardHealthChecks();
+app.MapPortalSeo();
 app.MapRazorComponentsWithStaticAssets<App>();
 
 app.Run();
```

- [ ] **Step 5: Implement the D-043 edits for the canonical host**

`src/TechStrap.Portal/appsettings.json`

```diff
--- src/TechStrap.Portal/appsettings.json
+++ src/TechStrap.Portal/appsettings.json
@@ -5,6 +5,12 @@
   "TECHSTRAP_PORTAL_PUBLIC_URL": "",
   "TECHSTRAP_PORTAL_DEFAULT_PRODUCT": "",
   "TECHSTRAP_PORTAL_SHOW_POWERED_BY": "true",
+  "CanonicalHost": {
+    "CanonicalHost": "",
+    "LegacyHosts": [],
+    "ForceHttps": false,
+    "Permanent": true
+  },
   "DataProtection": {
     "KeyRingPath": ""
   },
```

`src/TechStrap.Portal/.env.example`

```diff
--- src/TechStrap.Portal/.env.example
+++ src/TechStrap.Portal/.env.example
@@ -17,6 +17,14 @@ TECHSTRAP_PORTAL_PUBLIC_URL=
 # A product key (lowercase letters, digits and hyphens). When set, the Portal's root page (/) redirects to /p/<key>; blank shows a neutral page with no product list.
 TECHSTRAP_PORTAL_DEFAULT_PRODUCT=
 
+# --- Canonical host redirect (optional) ---
+# Redirects a request whose host is in LEGACYHOSTS to the same path and query on CANONICALHOST (a host name, no scheme). Only the listed hosts are redirected, so any other host is left alone,
+# and a blank CANONICALHOST turns the redirect off. FORCEHTTPS makes the target https; PERMANENT chooses 301 (true) or 302 (false). Add more legacy hosts as __1, __2 and so on.
+CANONICALHOST__CANONICALHOST=
+# CANONICALHOST__LEGACYHOSTS__0=old.example.com
+CANONICALHOST__FORCEHTTPS=false
+CANONICALHOST__PERMANENT=true
+
 # --- "Powered by TechStrap" mark (D-024) ---
 # Links to https://github.com/Syntax-Circus/techstrap and is shown by default. Set to false to hide it on every portal page and customer email (installation-wide).
 TECHSTRAP_PORTAL_SHOW_POWERED_BY=true
```

`deploy/.env.portal.example`

```diff
--- deploy/.env.portal.example
+++ deploy/.env.portal.example
@@ -16,6 +16,14 @@ TECHSTRAP_PORTAL_PUBLIC_URL=
 # Optional: a product key. When set, the Portal's root page redirects to /p/<key>; blank shows a neutral page with no product list.
 TECHSTRAP_PORTAL_DEFAULT_PRODUCT=
 
+# -- Canonical host redirect [Portal] (optional) --
+# Redirects a request whose host is in LEGACYHOSTS to the same path and query on CANONICALHOST (a host name, no scheme). Only the listed hosts are redirected, and a blank CANONICALHOST turns it off.
+# FORCEHTTPS makes the target https; PERMANENT chooses 301 (true) or 302 (false). Add more legacy hosts as __1, __2 and so on.
+CANONICALHOST__CANONICALHOST=
+# CANONICALHOST__LEGACYHOSTS__0=
+CANONICALHOST__FORCEHTTPS=false
+CANONICALHOST__PERMANENT=true
+
 # -- "Powered by TechStrap" mark [Worker, Portal] (D-024) --
 # Links to https://github.com/Syntax-Circus/techstrap and is shown by default. Set to false to hide it on every portal page and customer email (installation-wide).
 TECHSTRAP_PORTAL_SHOW_POWERED_BY=true
```

- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/*/PathHeaderRuleHostTests/*"`
Expected: PASS: `total: 12, failed: 0`.

Run: `dotnet test --project tests/TechStrap.Portal.Tests -c Release`
Expected: PASS: `total: 310, failed: 0` (244 before this task: `PortalHeaderRulesTests` 32, `TicketHeaderHostTests` 17, `SeoHostTests` 17).

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release`
Expected: PASS: `total: 843, failed: 0` (the Admin's attachment and header tests are unchanged and green: `ContentSecurityPolicyHostTests`, `SecurityHeadersHostTests`, `AttachmentPassThroughTests` in Admin.Tests).

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release`
Expected: PASS: `total: 1960, failed: 0`.

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal`
Expected: PASS: `Tests Passed: 288, Failed: 0`.

- [ ] **Step 7: Prove each pin with a mutation**

Run `git add -A` first. Commands: `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/*/PathHeaderRuleHostTests/*"` for rows H1 to H4, and `dotnet test --project tests/TechStrap.Portal.Tests -c Release --filter-query "/*/*/<Class>/*"` for the rest.

| # | File | Replace | With | Class | Expected |
| --- | --- | --- | --- | --- | --- |
| H1 | `src/TechStrap.Hosting/Wiring/BrowserHostExtensions.cs` | `headers[name] = change(headers[name].ToString());` | `if (!headers.ContainsKey(name)) { headers[name] = change(string.Empty); }` | `PathHeaderRuleHostTests` | KILLED, 9 failed of 12 (a rule must overwrite the shared and the endpoint's value) |
| H3 | the same | `foreach (var rule in matched.Where(rule => delivered \|\| !rule.SuccessOnly))` | `foreach (var rule in matched)` | `PathHeaderRuleHostTests` | KILLED, 1 failed (an error answer must not be sandboxed) |
| H4 | the same | `        app.UseForwardedHeaders();\n        app.UseCorrelationId();\n        var applied` | `        app.UseForwardedHeaders();\n        app.UseCorrelationId();\n        app.UseSecurityHeaders();\n        var applied` | `PathHeaderRuleHostTests` | KILLED, 8 failed (the rules must run after the shared headers) |
| P2 | `src/TechStrap.Portal/Headers/PortalHeaderRules.cs` | `PathHeaderRule.Sandbox(IsTicketAttachmentPath)` | `PathHeaderRule.Sandbox(IsTicketPath)` | `TicketHeaderHostTests` | KILLED, 1 failed of 17 (the ticket page would be sandboxed) |
| P3 | the same | `, ("Cache-Control", CacheControl)` | `` | `TicketHeaderHostTests` | KILLED, 9 failed |
| P7 | the same | `&& rest.Value!.Split('/', StringSplitOptions.RemoveEmptyEntries) is [_, var middle, _]\n        && middle.Equals("attachments", StringComparison.OrdinalIgnoreCase);` | `&& rest.Value!.Contains("attachments", StringComparison.OrdinalIgnoreCase);` | `PortalHeaderRulesTests` | KILLED, 4 failed of 32 |
| P8 | `src/TechStrap.Portal/Program.cs` | `app.UseTechStrapWebHost(PortalHeaderRules.Rules);` | `app.UseTechStrapWebHost();` | `TicketHeaderHostTests` | KILLED, 9 failed |
| P4 | `src/TechStrap.Portal/Seo/PortalSeoRegistration.cs` | `app.MapSeoRobotsTxt(extraDirectives: [DisallowTickets]);` | `app.MapSeoRobotsTxt();` | `SeoHostTests` | KILLED, 2 failed of 17 |
| P5 | the same | `services.AddOptions<SeoOptions>().PostConfigure<IOptions<PortalOptions>>((seo, portal) => seo.BaseUrl = portal.Value.PublicBaseUrl);` | `` | `SeoHostTests` | KILLED, 5 failed |
| P6 | the same | `app.UseSyntaxCircusSeo();` | `` | `SeoHostTests` | KILLED, 4 failed (the canonical-host tests) |

In a table cell a backslash before a pipe stands for the plain pipe character; pass the plain character to the tool. Row H2 (the path decided at response start instead of before `next()`) needs two replacements in one run:

```bash
python $T/mut.py src/TechStrap.Hosting/Wiring/BrowserHostExtensions.cs --replace "var matched = applied.Where(rule => rule.Matches(context.Request.Path)).ToArray();" "var matched = applied.ToArray();" --replace "foreach (var rule in matched.Where(rule => delivered || !rule.SuccessOnly))" "foreach (var rule in matched.Where(rule => (delivered || !rule.SuccessOnly) && rule.Matches(context.Request.Path)))" -- dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/*/PathHeaderRuleHostTests/*"
```

Expected: KILLED, 2 failed of 12 (the re-executed 404 of a `/t` path loses its headers).

- [ ] **Step 8: Verify and commit**

Run: `dotnet build TechStrap.slnx -c Release --no-incremental`
Expected: `0 Warning(s)`.

```bash
git add -A src tests scripts deploy
git diff --cached --stat
git commit -F - <<'EOF'
feat(portal): per-path security headers in Hosting and the Seo wiring (PHASE-09a)

UseTechStrapWebHost accepts PathHeaderRule values that run after the shared security headers. The
Portal's /t/* pages get no-referrer, no-store and noindex, and only /t/{token}/attachments/{id}
is sandboxed. Adds Blazor.Seo (derived base URL, robots.txt that disallows /t/, the canonical
host allow-list) and the CanonicalHost settings.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
EOF
```
### Task 4: The product theme, the layout, the product scope, the shell and the pages

**Review Focus pin:** Review Focus 3 (product enumeration) and Review Focus 5 (untrusted branding), and Review Focus 4 again through a real page. Pinned here by `NeutralPagesGuardTests` (an unknown, an inactive and a malformed key give identical answers and the page an unknown route gets; a malformed key makes no API call; the not-found and error pages are never branded, even after a product was resolved; a product page never themes a later request), `ProductThemeViewModelTests` (the logo scheme rule and the accent that only the derivation rule can produce), `ProductHomeHostTests` (the themed home, the encoded name, the logo and accent rules at the host, the calm failure page, the visitor's address through a real page) and `PortalLayoutTests` (bUnit).

**Files:**

- Create: `src/TechStrap.Portal/Components/Layout/PortalLayout.razor.cs`
- Create: `src/TechStrap.Portal/Components/Layout/ProductFooter.razor`
- Create: `src/TechStrap.Portal/Components/Layout/ProductHeader.razor`
- Create: `src/TechStrap.Portal/Components/Pages/Home.razor.cs`
- Create: `src/TechStrap.Portal/Components/Pages/ProductHome.razor`
- Create: `src/TechStrap.Portal/Components/ShellCopy.cs`
- Create: `src/TechStrap.Portal/Components/Ui/ProductUnavailable.razor`
- Create: `src/TechStrap.Portal/Products/ProductPageBase.cs`
- Create: `src/TechStrap.Portal/Products/ProductScope.cs`
- Create: `src/TechStrap.Portal/Products/ProductThemeViewModel.cs`
- Modify: `src/TechStrap.Portal/Components/Layout/PortalLayout.razor`
- Modify: `src/TechStrap.Portal/Components/Pages/Error.razor`
- Modify: `src/TechStrap.Portal/Components/Pages/Home.razor`
- Modify: `src/TechStrap.Portal/Components/Pages/NotFound.razor`
- Modify: `src/TechStrap.Portal/Components/_Imports.razor`
- Modify: `src/TechStrap.Portal/Program.cs`
- Modify: `src/TechStrap.Portal/Styles/_components.scss`
- Test (create): `tests/TechStrap.Portal.Tests/Components/PortalLayoutTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Products/NeutralPagesGuardTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Products/ProductHomeHostTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Products/ProductThemeViewModelTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Products/RootPageHostTests.cs`
- Test (modify): `tests/TechStrap.Api.Tests/ShellHostTests.cs`
- Test (modify): `tests/TechStrap.Portal.Tests/PortalHostTests.cs`


**Interfaces:**
- Consumes: `IPublicProductClient` and `ApiErrorCodes.RateLimited` (Task 2), `ProductKeyShape` and `PortalRoutes` (Task 1), `PortalOptions.DefaultProductKeyOrNull` (Task 1), `AccentScope`, `PoweredByFooter` (PHASE-02), Contracts `PublicProductDto`, `ProductAccent.TryDerive`, `BrandingRules.IsAcceptableLogoUrl`, `KbLimits.MaxSearchTextChars`, `PortalFactory`, `StubApiHandler`, `ProxyHopStartupFilter` (Tasks 1 and 2).
- Produces:
  - `TechStrap.Portal.Products.ProductThemeViewModel(string Key, string DisplayName, string? Accent, string? LogoUrl)` (public sealed record) with `static From(PublicProductDto product, bool allowLoopbackImages)`: the accent only in `ProductAccent.TryDerive`'s own spelling (else null), the logo only if `BrandingRules.IsAcceptableLogoUrl` accepts it and it is https, or http to loopback when `allowLoopbackImages` (else null), the name trimmed (blank falls back to the key). The DTO's own on-accent and ink strings are never carried.
  - `ProductScope` (public sealed, scoped): `ProductThemeViewModel? Theme`, `event Action? Changed`, `void Set(ProductThemeViewModel)`.
  - `ProductPageBase : ComponentBase` (public abstract): `[Parameter] string Key`; protected `Theme`, `UnavailableMessage` (a fixed `ProblemCopy` sentence or null) and `RetryHref`. In `OnInitializedAsync`: a key that is not a slug, or a not-found answer, calls `NavigationManager.NotFound()`; a success sets the scope; any other failure sets `UnavailableMessage` and the status (429 when rate limited, else 503).
  - `PortalLayout` (with `.razor.cs`, `IDisposable`): wraps the body in `AccentScope`, adds `ProductHeader` and `ProductFooter` when a product is in scope, always `PoweredByFooter`; re-renders when the scope changes. `ProductHeader` and `ProductFooter` take `[Parameter, EditorRequired] ProductThemeViewModel Theme`; `ProductUnavailable` takes `Message` and `RetryHref`.
  - Pages: `ProductHome` at `PortalRoutes.ProductHomeTemplate` (the search form, a GET to `PortalRoutes.KbSearch(key)` with `name="q"` and `maxlength=KbLimits.MaxSearchTextChars`, and the "Contact support" button), `Home` at `/` (a 302 to the default product, else a neutral page), `NotFound` and `Error` (unchanged text, now from `ShellCopy`).
  - `TechStrap.Portal.Components.ShellCopy` (public static): the shell sentences, `ProductTitle(string name)`.

- [ ] **Step 1: Write the failing tests**

`ProductThemeViewModelTests` is the Review Focus 5 table (every scheme, loopback only in Development, percent-encoded breakout characters, the 500-character limit, a blank name). `PortalLayoutTests` renders the layout with bUnit, including the late `Set` that SSR needs and the unsubscribe on dispose. The host tests put a stub API behind the Portal; `NeutralPagesGuardTests` uses a startup filter that resolves a product and then throws, to prove the error page is rendered in a fresh scope.

`tests/TechStrap.Portal.Tests/Products/ProductThemeViewModelTests.cs`

```csharp
using TechStrap.Contracts.Products;
using TechStrap.Portal.Products;

namespace TechStrap.Portal.Tests.Products;

/// <summary>
/// Review Focus 5 (untrusted branding): a stored product is data an agent typed, and a logo stored before the Admin validated it was never checked. The view model keeps only what is safe to
/// render: an accent that the one derivation rule accepts (the three colours are derived from it by <c>AccentScope</c>, never taken from the DTO's own strings), and a logo address that is https,
/// or http to loopback in Development only (the same rule as the Content-Security-Policy's <c>img-src</c>).
/// </summary>
public sealed class ProductThemeViewModelTests
{
    private static PublicProductDto Dto(string accent = "#F59E0B", string? logo = "https://cdn.example.com/paperplane.png", string name = "Paperplane", string onAccent = "#000000", string ink = "#9D6507") =>
        new("paperplane", name, logo, accent, onAccent, ink);

    [Fact]
    public void A_product_becomes_its_key_name_normalised_accent_and_logo()
    {
        var theme = ProductThemeViewModel.From(Dto(accent: "#f59e0b"), allowLoopbackImages: false);

        theme.Key.ShouldBe("paperplane");
        theme.DisplayName.ShouldBe("Paperplane");
        theme.Accent.ShouldBe("#F59E0B", "the single derivation rule's own spelling");
        theme.LogoUrl.ShouldBe("https://cdn.example.com/paperplane.png");
    }

    [Theory]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#123456;background:url(//evil.example/x)")]
    [InlineData("#123456\"><script>")]
    [InlineData("expression(alert(1))")]
    public void An_accent_the_rule_rejects_is_not_carried_so_the_stylesheet_fallbacks_apply(string accent)
    {
        ProductThemeViewModel.From(Dto(accent: accent), allowLoopbackImages: false).Accent.ShouldBeNull();
    }

    [Fact]
    public void The_dtos_own_derived_colours_are_never_carried()
    {
        var theme = ProductThemeViewModel.From(Dto(onAccent: "red;x:y", ink: "url(//evil.example)"), allowLoopbackImages: false);

        theme.ToString().ShouldNotContain("evil");
        theme.ToString().ShouldNotContain("red;x:y");
        typeof(ProductThemeViewModel).GetProperties().Select(p => p.Name).ShouldBe(["Key", "DisplayName", "Accent", "LogoUrl"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("https://cdn.example.com/logo.png")]
    [InlineData("https://CDN.EXAMPLE.COM/a/b.svg?v=2")]
    [InlineData("https://localhost/logo.png")]
    [InlineData("https://127.0.0.1:8443/logo.png")]
    public void An_https_logo_is_kept_in_every_environment(string logo)
    {
        ProductThemeViewModel.From(Dto(logo: logo), allowLoopbackImages: false).LogoUrl.ShouldNotBeNull();
        ProductThemeViewModel.From(Dto(logo: logo), allowLoopbackImages: true).LogoUrl.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("http://localhost:5000/logo.png")]
    [InlineData("http://127.0.0.1/logo.png")]
    public void A_loopback_http_logo_is_kept_in_Development_only(string logo)
    {
        ProductThemeViewModel.From(Dto(logo: logo), allowLoopbackImages: true).LogoUrl.ShouldBe(logo);
        ProductThemeViewModel.From(Dto(logo: logo), allowLoopbackImages: false).LogoUrl.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://cdn.example.com/logo.png")]
    [InlineData("http://localhost.evil.example/logo.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("//cdn.example.com/logo.png")]
    [InlineData("/logo.png")]
    [InlineData("logo.png")]
    [InlineData("file:///c:/logo.png")]
    [InlineData("ftp://cdn.example.com/logo.png")]
    [InlineData("https://user:secret@cdn.example.com/logo.png")]
    [InlineData("https:///logo.png")]
    [InlineData("https://cdn.example.com/a b.png")]
    [InlineData("https://cdn.example.com/a\nb.png")]
    public void Any_other_logo_is_omitted_in_every_environment(string? logo)
    {
        ProductThemeViewModel.From(Dto(logo: logo), allowLoopbackImages: false).LogoUrl.ShouldBeNull(logo);
        ProductThemeViewModel.From(Dto(logo: logo), allowLoopbackImages: true).LogoUrl.ShouldBeNull(logo);
    }

    [Fact]
    public void Characters_that_could_break_out_of_an_attribute_are_percent_encoded_in_a_kept_logo()
    {
        var logo = ProductThemeViewModel.From(Dto(logo: "https://cdn.example.com/a\"onerror=\"alert(1)<b>.png"), allowLoopbackImages: false).LogoUrl;

        logo.ShouldNotBeNull();
        logo.ShouldNotContain("\"");
        logo.ShouldNotContain("<");
        logo.ShouldNotContain(">");
    }

    [Fact]
    public void A_logo_over_500_characters_is_omitted()
    {
        var logo = "https://cdn.example.com/" + new string('a', 480) + ".png";

        logo.Length.ShouldBeGreaterThan(500);
        ProductThemeViewModel.From(Dto(logo: logo), allowLoopbackImages: false).LogoUrl.ShouldBeNull();
    }

    [Theory]
    [InlineData("Paperplane", "Paperplane")]
    [InlineData("  Paperplane  ", "Paperplane")]
    [InlineData("", "paperplane")]
    [InlineData("   ", "paperplane")]
    public void A_blank_name_falls_back_to_the_key_and_text_is_otherwise_kept_as_is(string name, string expected)
    {
        ProductThemeViewModel.From(Dto(name: name), allowLoopbackImages: false).DisplayName.ShouldBe(expected);
    }

    [Fact]
    public void Markup_in_the_name_stays_text_because_every_renderer_encodes_it()
    {
        ProductThemeViewModel.From(Dto(name: "<b>Paper</b> & \"plane\""), allowLoopbackImages: false).DisplayName.ShouldBe("<b>Paper</b> & \"plane\"");
    }
}
```

`tests/TechStrap.Portal.Tests/Components/PortalLayoutTests.cs`

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TechStrap.Portal.Components.Layout;
using TechStrap.Portal.Components.Ui;
using TechStrap.Portal.Products;

namespace TechStrap.Portal.Tests.Components;

public sealed class PortalLayoutTests : BunitContext
{
    private static ProductThemeViewModel Theme(string name = "Paperplane", string? accent = "#F59E0B", string? logo = "https://cdn.example.com/paperplane.png") =>
        new("paperplane", name, accent, logo);

    private ProductScope Scope { get; } = new();

    public PortalLayoutTests()
    {
        Services.AddSingleton(Options.Create(new PoweredByOptions()));
        Services.AddSingleton(Scope);
    }

    private IRenderedComponent<PortalLayout> RenderLayout() => Render<PortalLayout>(p => p.Add(l => l.Body, "<p id=\"page\">the page</p>"));

    [Fact]
    public void Without_a_product_the_layout_is_neutral_no_header_no_product_footer_no_accent_and_the_powered_by_line()
    {
        var cut = RenderLayout();

        cut.Find("#page").TextContent.ShouldBe("the page");
        cut.FindAll("header").ShouldBeEmpty();
        cut.FindAll("footer.ts-product-footer").ShouldBeEmpty();
        cut.Find("div.ts-accent-scope").HasAttribute("style").ShouldBeFalse();
        cut.FindAll("footer.ts-powered").Count.ShouldBe(1);
        cut.Markup.ShouldNotContain("--ts-accent");
        cut.Markup.ShouldNotContain("<img src=\"https");
    }

    [Fact]
    public void With_a_product_the_layout_gets_its_header_footer_and_accent_around_the_page_and_keeps_one_powered_by_line()
    {
        Scope.Set(Theme());

        var cut = RenderLayout();

        cut.Find("div.ts-accent-scope").GetAttribute("style").ShouldBe("--ts-accent:#F59E0B;--ts-on-accent:#000000;--ts-accent-ink:#9D6507");
        var header = cut.Find("header.ts-product-header");
        header.QuerySelector("a.ts-product-name")!.TextContent.Trim().ShouldBe("Paperplane");
        header.QuerySelector("a.ts-product-name")!.GetAttribute("href").ShouldBe("/p/paperplane");
        header.QuerySelector("img.ts-product-logo")!.GetAttribute("src").ShouldBe("https://cdn.example.com/paperplane.png");
        cut.Find("main #page").TextContent.ShouldBe("the page");
        cut.Find("footer.ts-product-footer a").GetAttribute("href").ShouldBe("/p/paperplane/lost-link");
        cut.FindAll("footer.ts-powered").Count.ShouldBe(1);
        cut.Markup.IndexOf("ts-product-header", StringComparison.Ordinal).ShouldBeLessThan(cut.Markup.IndexOf("id=\"page\"", StringComparison.Ordinal));
        cut.Markup.IndexOf("id=\"page\"", StringComparison.Ordinal).ShouldBeLessThan(cut.Markup.IndexOf("ts-product-footer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_page_that_sets_the_product_after_the_layout_rendered_updates_the_layout()
    {
        var cut = RenderLayout();
        cut.FindAll("header").ShouldBeEmpty();

        await cut.InvokeAsync(() => Scope.Set(Theme()));

        cut.Find("header.ts-product-header a.ts-product-name").TextContent.Trim().ShouldBe("Paperplane");
        cut.Find("div.ts-accent-scope").GetAttribute("style")!.ShouldContain("--ts-accent:#F59E0B");
    }

    [Fact]
    public void The_logo_is_decorative_because_the_name_sits_beside_it_and_is_omitted_when_the_theme_has_none()
    {
        Scope.Set(Theme());
        var withLogo = RenderLayout();
        withLogo.Find("img.ts-product-logo").GetAttribute("alt").ShouldBe(string.Empty);

        Scope.Set(Theme(logo: null));
        var withoutLogo = RenderLayout();
        withoutLogo.FindAll("img.ts-product-logo").ShouldBeEmpty();
    }

    [Fact]
    public void The_product_name_is_text_never_markup()
    {
        Scope.Set(Theme(name: "<img src=x onerror=alert(1)>Paper & \"plane\""));

        var cut = RenderLayout();

        cut.FindAll("header img[onerror]").ShouldBeEmpty();
        cut.Find("a.ts-product-name").TextContent.Trim().ShouldBe("<img src=x onerror=alert(1)>Paper & \"plane\"");
        cut.Markup.ShouldNotContain("<img src=x");
    }

    [Fact]
    public void An_unusable_accent_gives_no_style_but_the_header_still_shows()
    {
        Scope.Set(Theme(accent: null));

        var cut = RenderLayout();

        cut.Find("div.ts-accent-scope").HasAttribute("style").ShouldBeFalse();
        cut.Find("header.ts-product-header").ShouldNotBeNull();
    }

    [Fact]
    public async Task Disposing_the_layout_stops_it_listening_to_the_scope()
    {
        var cut = RenderLayout();

        SubscriberCount(Scope).ShouldBe(1, "the rendered layout listens");
        await DisposeComponentsAsync();

        SubscriberCount(Scope).ShouldBe(0, "a disposed layout must not stay subscribed to a scope that outlives it");
        Should.NotThrow(() => Scope.Set(Theme()));
        cut.ShouldNotBeNull();
    }

    private static int SubscriberCount(ProductScope scope) =>
        ((Delegate?)typeof(ProductScope).GetField(nameof(ProductScope.Changed), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(scope))?.GetInvocationList().Length ?? 0;

    [Fact]
    public void The_powered_by_line_follows_the_installation_setting_and_the_product_pages_keep_their_own_footer()
    {
        Services.AddSingleton(Options.Create(new PoweredByOptions { Show = false }));
        Scope.Set(Theme());

        var cut = RenderLayout();

        cut.FindAll("footer.ts-powered").ShouldBeEmpty();
        cut.FindAll("footer.ts-product-footer").Count.ShouldBe(1);
    }
}
```

`tests/TechStrap.Portal.Tests/Products/ProductHomeHostTests.cs`

```csharp
using System.Net;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Products;

/// <summary>P09-T03 and T05 at the host, with a fake API behind the Portal: the themed product home, the logo rule and the accent that only the derivation rule can produce.</summary>
public sealed class ProductHomeHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static PublicProductDto Product(string name = "Paperplane", string? logo = "https://cdn.example.com/paperplane.png", string accent = "#F59E0B", string onAccent = "#000000", string ink = "#9D6507") =>
        new("paperplane", name, logo, accent, onAccent, ink);

    private static async Task<(HttpResponseMessage Response, string Html)> GetAsync(PortalFactory factory, string path)
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync(path, Ct);
        return (response, await response.Content.ReadAsStringAsync(Ct));
    }

    private static PortalFactory WithProduct(PublicProductDto product, string environment = "Development", Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? configure = null)
    {
        var factory = new PortalFactory(environment, configureServices: configure);
        factory.Api.OnJson(HttpMethod.Get, $"/api/public/products/{product.Key}", product);
        return factory;
    }

    [Fact]
    public async Task A_known_product_gets_a_themed_home_with_its_name_logo_search_and_contact_link()
    {
        await using var factory = WithProduct(Product());

        var (response, html) = await GetAsync(factory, "/p/paperplane");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<title>Paperplane Support</title>");
        html.ShouldContain("--ts-accent:#F59E0B;--ts-on-accent:#000000;--ts-accent-ink:#9D6507");
        html.ShouldContain("class=\"ts-product-name\"");
        html.ShouldContain(">Paperplane</a>");
        html.ShouldContain("<img class=\"ts-product-logo\" src=\"https://cdn.example.com/paperplane.png\"");
        html.ShouldContain("<h1>How can we help?</h1>");
        html.ShouldContain("<form method=\"get\" action=\"/p/paperplane/kb/search\"");
        html.ShouldContain("name=\"q\"");
        html.ShouldContain("maxlength=\"200\"");
        html.ShouldContain("<a class=\"btn btn-primary\" href=\"/p/paperplane/contact\">Contact support</a>");
        html.ShouldContain("href=\"/p/paperplane/lost-link\"");
        html.ShouldNotContain("TechStrap Portal");
        factory.Api.Count(HttpMethod.Get, "/api/public/products/paperplane").ShouldBe(1);
    }

    [Fact]
    public async Task The_search_box_is_labelled_and_posts_nothing_it_is_a_get_form_with_no_script()
    {
        await using var factory = WithProduct(Product());

        var (_, html) = await GetAsync(factory, "/p/paperplane");

        html.ShouldContain("<label for=\"kb-search\"");
        html.ShouldContain("id=\"kb-search\"");
        html.ShouldContain("type=\"search\"");
        html.ShouldContain("role=\"search\"");
        html.ShouldNotContain("<form method=\"post\"");
        html.ShouldNotContain("<script>");
    }

    [Fact]
    public async Task The_product_name_is_encoded_wherever_it_appears()
    {
        await using var factory = WithProduct(Product(name: "<img src=x onerror=alert(1)>Paper & \"plane\""));

        var (_, html) = await GetAsync(factory, "/p/paperplane");

        html.ShouldNotContain("<img src=x");
        html.ShouldNotContain("onerror=alert(1)>Paper");
        html.ShouldContain("&lt;img src=x onerror=alert(1)&gt;Paper &amp; &quot;plane&quot;");
    }

    [Theory]
    [InlineData("http://cdn.example.com/logo.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=")]
    [InlineData("//cdn.example.com/logo.png")]
    [InlineData("https://user:pw@cdn.example.com/logo.png")]
    [InlineData("http://localhost:5000/logo.png")]
    public async Task A_logo_that_is_not_https_is_not_rendered_in_Production(string logo)
    {
        await using var factory = WithProduct(Product(logo: logo), "Production");

        var (response, html) = await GetAsync(factory, "/p/paperplane");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldNotContain("ts-product-logo");
        html.ShouldNotContain(logo.Replace("&", "&amp;"));
        html.ShouldContain(">Paperplane</a>", Case.Sensitive, "the name still shows");
    }

    [Fact]
    public async Task A_loopback_http_logo_is_rendered_in_Development_only()
    {
        await using var development = WithProduct(Product(logo: "http://localhost:5000/logo.png"), "Development");
        await using var production = WithProduct(Product(logo: "http://localhost:5000/logo.png"), "Production");

        var (_, devHtml) = await GetAsync(development, "/p/paperplane");
        var (_, prodHtml) = await GetAsync(production, "/p/paperplane");

        devHtml.ShouldContain("src=\"http://localhost:5000/logo.png\"");
        prodHtml.ShouldNotContain("localhost:5000");
    }

    [Fact]
    public async Task A_product_without_a_logo_still_shows_its_name()
    {
        await using var factory = WithProduct(Product(logo: null));

        var (_, html) = await GetAsync(factory, "/p/paperplane");

        html.ShouldNotContain("ts-product-logo");
        html.ShouldContain(">Paperplane</a>");
    }

    [Theory]
    [InlineData("#F59E0B;background:url(//evil.example/x)")]
    [InlineData("red")]
    [InlineData("")]
    public async Task An_accent_that_is_not_a_hex_colour_sets_no_style_and_nothing_reaches_the_page(string accent)
    {
        await using var factory = WithProduct(Product(accent: accent));

        var (response, html) = await GetAsync(factory, "/p/paperplane");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldNotContain("--ts-accent");
        html.ShouldNotContain("evil.example");
        html.ShouldNotContain("style=");
    }

    [Fact]
    public async Task The_colours_come_only_from_the_derivation_rule_not_from_the_dtos_own_derived_fields()
    {
        await using var factory = WithProduct(Product(accent: "#4B7D87", onAccent: "red;x:y", ink: "url(//evil.example/x)"));

        var (_, html) = await GetAsync(factory, "/p/paperplane");

        html.ShouldContain("--ts-accent:#4B7D87;--ts-on-accent:#FFFFFF;--ts-accent-ink:#4B7D87");
        html.ShouldNotContain("evil.example");
        html.ShouldNotContain("red;x:y");
    }

    [Fact]
    public async Task The_visitors_address_behind_a_trusted_proxy_reaches_the_api_from_a_real_page()
    {
        await using var factory = WithProduct(Product(), configure: ProxyHopStartupFilter.Add("192.0.2.10"));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/p/paperplane");
        request.Headers.Add("X-Forwarded-For", "203.0.113.9");

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Api.AssertEveryCallBore("203.0.113.9");
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError, HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests)]
    public async Task When_the_api_fails_the_page_says_so_calmly_with_a_retry_link_and_no_brand_and_no_internals(HttpStatusCode apiStatus, HttpStatusCode pageStatus)
    {
        await using var factory = new PortalFactory("Production");
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/paperplane", apiStatus, "internal-error", "System.InvalidOperationException at Npgsql host=10.0.0.5 stack trace");

        var (response, html) = await GetAsync(factory, "/p/paperplane");

        response.StatusCode.ShouldBe(pageStatus);
        html.ShouldContain("This page could not be loaded.");
        html.ShouldContain("href=\"/p/paperplane\">Try again</a>");
        html.ShouldNotContain("Npgsql");
        html.ShouldNotContain("10.0.0.5");
        html.ShouldNotContain("InvalidOperationException");
        html.ShouldNotContain("--ts-accent");
        html.ShouldNotContain("ts-product-header");
        html.ShouldContain("ts-powered");
    }

    [Fact]
    public async Task A_transport_failure_is_the_same_calm_page()
    {
        await using var factory = new PortalFactory("Production");
        factory.Api.On(HttpMethod.Get, "/api/public/products/paperplane", _ => throw new HttpRequestException("No connection could be made (10.1.2.3:5432)"));

        var (response, html) = await GetAsync(factory, "/p/paperplane");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        html.ShouldContain("This page could not be loaded.");
        html.ShouldNotContain("10.1.2.3");
        factory.Api.Count(HttpMethod.Get, "/api/public/products/paperplane").ShouldBe(1 + 2, "a read is retried twice on a transport failure");
    }

    [Fact]
    public async Task A_page_for_a_product_is_not_cached_by_the_browser_forever_and_carries_the_shared_headers()
    {
        await using var factory = WithProduct(Product());

        var (response, _) = await GetAsync(factory, "/p/paperplane");

        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("strict-origin-when-cross-origin");
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("img-src 'self' https: data:");
    }
}
```

`tests/TechStrap.Portal.Tests/Products/NeutralPagesGuardTests.cs`

```csharp
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Products;

namespace TechStrap.Portal.Tests.Products;

/// <summary>
/// Review Focus 3 (product enumeration) and the PHASE-04 no-brand guard. An unknown, an inactive and a malformed product key are answered exactly like an unknown route, so nothing tells a
/// visitor which products exist; and the not-found and error pages are never branded, because they are rendered in a fresh scope that knows no product.
/// </summary>
public sealed class NeutralPagesGuardTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly PublicProductDto Paperplane = new("paperplane", "Paperplane", "https://cdn.example.com/paperplane.png", "#F59E0B", "#000000", "#9D6507");

    private static async Task<(HttpStatusCode Status, string Body, string[] Headers)> GetAsync(PortalFactory factory, string path)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path, Ct);
        var headers = response.Headers.Concat(response.Content.Headers).Where(h => h.Key is not ("Date" or "X-Correlation-Id" or "Content-Length")).OrderBy(h => h.Key).Select(h => $"{h.Key}: {string.Join(",", h.Value)}").ToArray();
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct), headers);
    }

    private static PortalFactory Factory(string environment = "Production", Action<IServiceCollection>? configure = null)
    {
        var factory = new PortalFactory(environment, configureServices: configure);
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", Paperplane);
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/gone", HttpStatusCode.NotFound, "product-not-found", "No such product.");
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/inactive", HttpStatusCode.NotFound, "product-not-found", "That product is not active.");
        return factory;
    }

    [Fact]
    public async Task An_unknown_inactive_and_malformed_key_are_identical_to_each_other_and_their_page_is_byte_identical_to_an_unknown_routes()
    {
        await using var factory = Factory();
        var unknownRoute = await GetAsync(factory, "/no/such/route");

        var answers = new List<(string Path, (HttpStatusCode Status, string Body, string[] Headers) Answer)>();
        foreach (var path in new[] { "/p/gone", "/p/inactive", "/p/BAD_KEY", "/p/Paperplane", "/p/a%20b", "/p/-x", "/p/x--y", "/p/" + new string('a', 41) })
        {
            answers.Add((path, await GetAsync(factory, path)));
        }

        unknownRoute.Status.ShouldBe(HttpStatusCode.NotFound);
        unknownRoute.Body.ShouldContain("Page not found");
        var first = answers[0].Answer;
        foreach (var (path, answer) in answers)
        {
            // Nothing about the answer tells an unknown product from an inactive one from a malformed key: the status, every header and the whole body match.
            answer.Status.ShouldBe(HttpStatusCode.NotFound, path);
            answer.Headers.ShouldBe(first.Headers, path);
            answer.Body.ShouldBe(first.Body, path);

            // And the page is the very page an unknown route gets.
            answer.Body.ShouldBe(unknownRoute.Body, path);
        }
    }

    [Fact]
    public async Task A_malformed_key_never_reaches_the_api_and_an_unknown_one_costs_exactly_one_read()
    {
        await using var factory = Factory();

        await GetAsync(factory, "/p/BAD_KEY");
        await GetAsync(factory, "/p/a%20b");
        await GetAsync(factory, "/p/" + new string('a', 41));
        factory.Api.Requests.ShouldBeEmpty("a key that is not a slug is answered without a call");

        await GetAsync(factory, "/p/gone");
        factory.Api.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_not_found_page_has_no_product_in_it_and_no_api_call_is_needed_to_render_it()
    {
        await using var factory = Factory();

        var (status, body, _) = await GetAsync(factory, "/p/gone");

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldNotContain("gone");
        body.ShouldNotContain("--ts-accent");
        body.ShouldNotContain("ts-product");
        body.ShouldNotContain("style=");
        body.ShouldContain("ts-powered");
        factory.Api.Count(HttpMethod.Get, "/api/public/products/gone").ShouldBe(1);
    }

    // Unknown route under a known product: the 404 is rendered in its own scope (the status-code re-execution), which has no product.
    [Fact]
    public async Task An_unknown_route_under_a_known_product_is_the_neutral_404_not_a_branded_one()
    {
        await using var factory = Factory();

        var (status, body, _) = await GetAsync(factory, "/p/paperplane/no-such-page");

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldNotContain("Paperplane");
        body.ShouldNotContain("--ts-accent");
        body.ShouldNotContain("ts-product");
    }

    // The product is per request: one visitor's product must never theme the next visitor's neutral page.
    [Fact]
    public async Task A_product_page_never_themes_a_later_request_to_a_neutral_page()
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();

        (await client.GetStringAsync("/p/paperplane", Ct)).ShouldContain("--ts-accent:#F59E0B");
        using var unknownRoute = await client.GetAsync("/no/such/route", Ct);
        using var unknownProduct = await client.GetAsync("/p/gone", Ct);
        using var error = await client.GetAsync("/error", Ct);
        var root = await client.GetStringAsync("/", Ct);

        foreach (var body in new[] { await unknownRoute.Content.ReadAsStringAsync(Ct), await unknownProduct.Content.ReadAsStringAsync(Ct), await error.Content.ReadAsStringAsync(Ct), root })
        {
            body.ShouldNotContain("Paperplane");
            body.ShouldNotContain("--ts-accent");
            body.ShouldNotContain("ts-product");
        }
    }

    private sealed class BrandThenThrowStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map("/__test/brand-then-throw", branch => branch.Run(context =>
            {
                // A page that had already resolved its product, then failed: the product is in this request's scope.
                context.RequestServices.GetRequiredService<ProductScope>().Set(new ProductThemeViewModel("paperplane", "Paperplane", "#F59E0B", "https://cdn.example.com/paperplane.png"));
                throw new InvalidOperationException("boom Paperplane");
            }));
        };
    }

    [Fact]
    public async Task An_unhandled_exception_after_the_product_was_resolved_is_the_plain_neutral_error_page()
    {
        await using var factory = Factory(configure: services => services.AddSingleton<IStartupFilter, BrandThenThrowStartupFilter>());

        var (status, body, _) = await GetAsync(factory, "/__test/brand-then-throw");

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldContain("Something went wrong.");
        body.ShouldNotContain("Paperplane");
        body.ShouldNotContain("boom");
        body.ShouldNotContain("--ts-accent");
        body.ShouldNotContain("ts-product");
        body.ShouldNotContain("style=");
    }

    [Fact]
    public async Task The_error_and_not_found_pages_themselves_are_neutral()
    {
        await using var factory = Factory();

        var error = await GetAsync(factory, "/error");
        var notFound = await GetAsync(factory, "/not-found");

        foreach (var (_, body, _) in new[] { error, notFound })
        {
            body.ShouldNotContain("ts-product");
            body.ShouldNotContain("--ts-accent");
            body.ShouldNotContain("style=");
        }

        factory.Api.Requests.ShouldBeEmpty();
    }
}
```

`tests/TechStrap.Portal.Tests/Products/RootPageHostTests.cs`

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Tests.Products;

/// <summary>P09-T05: <c>/</c> sends visitors to the default product when one is configured; otherwise it is a neutral page with no product list, so nothing can be enumerated.</summary>
public sealed class RootPageHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static Dictionary<string, string?> Default(string? key) => new() { [PortalOptions.DefaultProductKey] = key };

    [Fact]
    public async Task With_a_default_product_the_root_redirects_with_a_302_to_its_home_and_calls_nothing()
    {
        await using var factory = new PortalFactory(settings: Default("paperplane"));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        response.Headers.Location!.PathAndQuery.ShouldBe("/p/paperplane");
        factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_redirect_follows_through_to_the_themed_home()
    {
        await using var factory = new PortalFactory(settings: Default("paperplane"));
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", new TechStrap.Contracts.Products.PublicProductDto("paperplane", "Paperplane", null, "#F59E0B", "#000000", "#9D6507"));
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/", Ct);

        html.ShouldContain("How can we help?");
        html.ShouldContain("--ts-accent:#F59E0B");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Without_a_default_product_the_root_is_a_neutral_page_with_no_product_list_and_no_api_call(string? key)
    {
        await using var factory = new PortalFactory(settings: Default(key));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/", Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<h1>Support</h1>");
        html.ShouldContain("the link in your email");
        html.ShouldNotContain("<ul");
        html.ShouldNotContain("/p/");
        html.ShouldNotContain("--ts-accent");
        html.ShouldNotContain("ts-product");
        html.ShouldNotContain("TechStrap Portal");
        html.ShouldContain("ts-powered");
        factory.Api.Requests.ShouldBeEmpty("the root never asks the API which products exist");
    }

    [Fact]
    public async Task A_default_product_that_is_not_a_slug_stops_the_host_at_start()
    {
        await using var factory = new PortalFactory(settings: Default("Not A Slug"));

        Should.Throw<Microsoft.Extensions.Options.OptionsValidationException>(() => factory.CreateClient());
    }
}
```

Two existing tests named the placeholder page's text; the Portal's root is now the neutral "Support" page.

`tests/TechStrap.Portal.Tests/PortalHostTests.cs`

```diff
--- tests/TechStrap.Portal.Tests/PortalHostTests.cs
+++ tests/TechStrap.Portal.Tests/PortalHostTests.cs
@@ -69,6 +69,6 @@ public sealed class PortalHostTests
         var html = await client.GetStringAsync("/", TestContext.Current.CancellationToken);
 
         html.ShouldContain("ts-powered");
-        html.ShouldContain("TechStrap Portal");
+        html.ShouldContain("<h1>Support</h1>");
     }
 }
```

`tests/TechStrap.Api.Tests/ShellHostTests.cs`

```diff
--- tests/TechStrap.Api.Tests/ShellHostTests.cs
+++ tests/TechStrap.Api.Tests/ShellHostTests.cs
@@ -22,12 +22,12 @@ public sealed class ShellHostTests
     }
 
     [Fact]
-    public async Task Portal_serves_the_placeholder_page_icons_and_compiled_css()
+    public async Task Portal_serves_the_neutral_root_page_icons_and_compiled_css()
     {
         await using var factory = new PortalFactory();
         using var client = factory.CreateClient();
 
-        await AssertShellAsync(client, "TechStrap Portal", ".shell-placeholder");
+        await AssertShellAsync(client, "<h1>Support</h1>", ".shell-placeholder");
     }
 
     private static async Task AssertShellAsync(HttpClient client, string expectedHeading, string expectedCssClass)
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet build tests/TechStrap.Portal.Tests -c Release`
Expected: FAIL to compile, 6 distinct errors, the first of them `error CS0234: The type or namespace name 'Products' does not exist in the namespace 'TechStrap.Portal'` and `error CS0246: The type or namespace name 'ProductThemeViewModel' could not be found`.

- [ ] **Step 3: Implement the product theme, scope and page base**

`ProductThemeViewModel` is the only place a stored product is trusted, and it trusts as little as possible: `ProductAccent.TryDerive` is the one derivation rule (nothing else may recompute the three colours), and the logo rule is stricter than Contracts' `IsAcceptableLogoUrl` by exactly the Development switch, the same as `TechStrapCsp.ForBlazorApp(allowLoopbackImages)`. The scope is per request, which is what keeps the not-found and error pages neutral.

`src/TechStrap.Portal/Products/ProductThemeViewModel.cs`

```csharp
using TechStrap.Contracts.Branding;
using TechStrap.Contracts.Products;

namespace TechStrap.Portal.Products;

/// <summary>
/// What a page needs to look like one product's own, and nothing it does not (D-045; there is no <c>BrandingThemeFactory</c>). A stored product is data an agent typed, and a logo stored before the
/// Admin validated it was never checked, so it is re-checked here, once, for every page:
/// <list type="bullet">
/// <item>The accent is kept only when <see cref="ProductAccent.TryDerive"/> accepts it, in that rule's own spelling. <c>AccentScope</c> derives the on-accent and ink colours from it with the same
/// single implementation, so none of the DTO's three colour strings is ever written to a style attribute as it arrived.</item>
/// <item>The logo is kept only when <see cref="BrandingRules.IsAcceptableLogoUrl"/> accepts it and it is https, or http to <c>localhost</c> or <c>127.0.0.1</c> when
/// <paramref name="allowLoopbackImages"/> is set (Development), which is exactly what the Content-Security-Policy's <c>img-src</c> allows (<c>TechStrapCsp.ForBlazorApp</c>).</item>
/// </list>
/// The name is kept as text; every renderer encodes it, and no page may render it as markup.
/// </summary>
public sealed record ProductThemeViewModel(string Key, string DisplayName, string? Accent, string? LogoUrl)
{
    public static ProductThemeViewModel From(PublicProductDto product, bool allowLoopbackImages)
    {
        ArgumentNullException.ThrowIfNull(product);
        var name = product.DisplayName?.Trim();
        return new ProductThemeViewModel(
            product.Key,
            string.IsNullOrEmpty(name) ? product.Key : name,
            ProductAccent.TryDerive(product.AccentColour, out var colours) ? colours.Accent : null,
            AcceptableLogo(product.LogoPath, allowLoopbackImages));
    }

    private static string? AcceptableLogo(string? logoPath, bool allowLoopbackImages)
    {
        var text = logoPath?.Trim();
        if (string.IsNullOrEmpty(text) || !BrandingRules.IsAcceptableLogoUrl(text))
        {
            return null;
        }

        // Acceptable and non-blank means an absolute address with a host: https, or http to a loopback host.
        var uri = new Uri(text, UriKind.Absolute);
        return uri.Scheme == Uri.UriSchemeHttps || allowLoopbackImages ? uri.AbsoluteUri : null;
    }
}
```

`src/TechStrap.Portal/Products/ProductScope.cs`

```csharp
namespace TechStrap.Portal.Products;

/// <summary>
/// The product the current request is about, set by a product page once it has loaded the product and read by <c>PortalLayout</c> for the header, the footer and the accent. It is a scoped service, so
/// every request has its own: the not-found and error pages, which the host renders in a fresh scope (re-execution), know no product and stay neutral, whatever happened before them.
/// </summary>
public sealed class ProductScope
{
    public ProductThemeViewModel? Theme { get; private set; }

    /// <summary>Raised when a page sets the product after the layout has rendered, so the layout renders again.</summary>
    public event Action? Changed;

    public void Set(ProductThemeViewModel theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Theme = theme;
        Changed?.Invoke();
    }
}
```

`src/TechStrap.Portal/Products/ProductPageBase.cs`

```csharp
using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Products;

/// <summary>
/// The base of every page under <c>/p/{key}</c> (D-045). It loads the product once and puts it in the <see cref="ProductScope"/>, so the layout can theme the page. Review Focus 3: an unknown product,
/// an inactive product and a key that is not a slug all end in <c>NavigationManager.NotFound()</c>, which renders the same neutral page, with the same status, as an unknown route, so nothing tells
/// a visitor which products exist; a key that is not a slug is answered without a call. When the API fails the page shows a calm message (<see cref="UnavailableMessage"/>) and a 503, or a 429 when it
/// is rate limiting, never a stack trace.
/// </summary>
public abstract class ProductPageBase : ComponentBase
{
    [Parameter]
    public string Key { get; set; } = string.Empty;

    [Inject]
    private IPublicProductClient Products { get; set; } = default!;

    [Inject]
    private ProductScope Scope { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private IHostEnvironment Environment { get; set; } = default!;

    [Inject]
    private IHttpContextAccessor HttpContextAccessor { get; set; } = default!;

    /// <summary>The product's theme once it has loaded; null when the product is unknown or the API failed.</summary>
    protected ProductThemeViewModel? Theme => Scope.Theme;

    /// <summary>The fixed sentence to show when the API could not be asked (<see cref="ProblemCopy"/>); null when the product loaded or was not found.</summary>
    protected string? UnavailableMessage { get; private set; }

    /// <summary>This page's own address, root-relative, for a "Try again" link (the document's <c>base</c> is <c>/</c>).</summary>
    protected string RetryHref => "/" + Navigation.ToBaseRelativePath(Navigation.Uri);

    protected override async Task OnInitializedAsync()
    {
        if (!ProductKeyShape.IsWellFormed(Key))
        {
            Navigation.NotFound();
            return;
        }

        var result = await Products.GetAsync(Key, HttpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);
        if (result.IsSuccess)
        {
            Scope.Set(ProductThemeViewModel.From(result.Value, Environment.IsDevelopment()));
            return;
        }

        var error = result.Errors[0];
        if (error.Kind == ResultErrorKind.NotFound)
        {
            Navigation.NotFound();
            return;
        }

        UnavailableMessage = error.Message;
        SetStatus(error.Code == ApiErrorCodes.RateLimited ? StatusCodes.Status429TooManyRequests : StatusCodes.Status503ServiceUnavailable);
    }

    private void SetStatus(int status)
    {
        if (HttpContextAccessor.HttpContext is { Response.HasStarted: false } context)
        {
            context.Response.StatusCode = status;
        }
    }
}
```

- [ ] **Step 4: Implement the copy, the layout and the pages**

`NavigationManager.NotFound()` renders the router's not-found page with a 404 in the same response; the bodies match an unknown route's byte for byte (the tests pin it). `PortalLayout` reads the scope on every render and listens to `Changed`, because a page sets its product while it initialises, after the layout first rendered, and the static renderer writes the final tree. The header is the logo (decorative, `alt=""`) and the name as one link, on one line so no whitespace separates them.

`src/TechStrap.Portal/Components/ShellCopy.cs`

```csharp
namespace TechStrap.Portal.Components;

/// <summary>
/// The words of the shell pages. Plain copy, no humour (BRAND.md): TechStrap's name appears nowhere here, because the Portal is the product's, and the only TechStrap line is the
/// "Powered by TechStrap" footer. A page references these; it never writes a sentence of its own.
/// </summary>
public static class ShellCopy
{
    // The root page, shown only when no default product is configured. No product list: nothing may be enumerated.
    public const string RootTitle = "Support";
    public const string RootHeading = "Support";
    public const string RootIntro = "Open the help page of your product, or follow a ticket with the link in your email.";

    // A product page.
    public const string HomeHeading = "How can we help?";
    public const string SearchLabel = "Search help articles";
    public const string SearchButton = "Search";
    public const string ContactSupport = "Contact support";
    public const string LostLinkPrompt = "Lost your ticket link?";

    // The calm failure state of a product page.
    public const string UnavailableTitle = "This page could not be loaded.";
    public const string UnavailableRetry = "Try again";

    // The neutral system pages.
    public const string NotFoundTitle = "Not found";
    public const string NotFoundHeading = "Page not found";
    public const string ErrorTitle = "Error";
    public const string ErrorHeading = "Something went wrong.";
    public const string ErrorText = "We could not finish that request. Try again in a minute; if it keeps happening, contact support.";
    public const string BackToStart = "Back to the start";

    public static string ProductTitle(string productName) => $"{productName} Support";
}
```

`src/TechStrap.Portal/Components/Layout/PortalLayout.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using TechStrap.Portal.Products;

namespace TechStrap.Portal.Components.Layout;

/// <summary>
/// The Portal's page frame. A page under <c>/p/{key}</c> puts its product in the <see cref="ProductScope"/>, and the layout then wraps it in that product's accent, header and footer; with no product
/// in scope (the root page, not-found, error) the layout is neutral and carries only the "Powered by TechStrap" line.
/// </summary>
public partial class PortalLayout : LayoutComponentBase, IDisposable
{
    [Inject]
    private ProductScope Scope { get; set; } = default!;

    private ProductThemeViewModel? Theme => Scope.Theme;

    protected override void OnInitialized() => Scope.Changed += OnScopeChanged;

    public void Dispose() => Scope.Changed -= OnScopeChanged;

    // The page sets the product while it initialises, after this layout first rendered.
    private void OnScopeChanged() => _ = InvokeAsync(StateHasChanged);
}
```

`src/TechStrap.Portal/Components/Layout/PortalLayout.razor`

```diff
--- src/TechStrap.Portal/Components/Layout/PortalLayout.razor
+++ src/TechStrap.Portal/Components/Layout/PortalLayout.razor
@@ -1,6 +1,16 @@
 @inherits LayoutComponentBase
 
-<div class="ts-portal">
-    <main class="ts-portal-main">@Body</main>
-    <PoweredByFooter />
-</div>
+<AccentScope Accent="@Theme?.Accent">
+    <div class="ts-portal">
+        @if (Theme is { } header)
+        {
+            <ProductHeader Theme="header" />
+        }
+        <main class="ts-portal-main">@Body</main>
+        @if (Theme is { } footer)
+        {
+            <ProductFooter Theme="footer" />
+        }
+        <PoweredByFooter />
+    </div>
+</AccentScope>
```

`src/TechStrap.Portal/Components/Layout/ProductHeader.razor`

```razor
<header class="ts-product-header">
    <a class="ts-product-name" href="@PortalRoutes.ProductHome(Theme.Key)">@if (Theme.LogoUrl is { } logo){<img class="ts-product-logo" src="@logo" alt="" height="32" />}@Theme.DisplayName</a>
</header>

@code {
    [Parameter, EditorRequired]
    public ProductThemeViewModel Theme { get; set; } = default!;
}
```

`src/TechStrap.Portal/Components/Layout/ProductFooter.razor`

```razor
<footer class="ts-product-footer">
    <a href="@PortalRoutes.LostLink(Theme.Key)">@ShellCopy.LostLinkPrompt</a>
</footer>

@code {
    [Parameter, EditorRequired]
    public ProductThemeViewModel Theme { get; set; } = default!;
}
```

`src/TechStrap.Portal/Components/Ui/ProductUnavailable.razor`

```razor
<section class="ts-state" role="alert">
    <h1>@ShellCopy.UnavailableTitle</h1>
    <p>@Message</p>
    <p><a class="btn btn-outline-primary" href="@RetryHref">@ShellCopy.UnavailableRetry</a></p>
</section>

@code {
    [Parameter, EditorRequired]
    public string Message { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public string RetryHref { get; set; } = string.Empty;
}
```

`src/TechStrap.Portal/Components/Pages/ProductHome.razor`

```razor
@attribute [Route(PortalRoutes.ProductHomeTemplate)]
@inherits ProductPageBase

@if (Theme is { } theme)
{
    <PageTitle>@ShellCopy.ProductTitle(theme.DisplayName)</PageTitle>
    <h1>@ShellCopy.HomeHeading</h1>
    <form method="get" action="@PortalRoutes.KbSearch(theme.Key)" role="search" class="ts-help-search">
        <label for="kb-search" class="form-label">@ShellCopy.SearchLabel</label>
        <div class="ts-help-search-row">
            <input id="kb-search" name="q" type="search" class="form-control" maxlength="@KbLimits.MaxSearchTextChars" autocomplete="off" />
            <button type="submit" class="btn btn-primary">@ShellCopy.SearchButton</button>
        </div>
    </form>
    <p class="ts-help-contact"><a class="btn btn-primary" href="@PortalRoutes.Contact(theme.Key)">@ShellCopy.ContactSupport</a></p>
}
else if (UnavailableMessage is not null)
{
    <PageTitle>@ShellCopy.UnavailableTitle</PageTitle>
    <ProductUnavailable Message="@UnavailableMessage" RetryHref="@RetryHref" />
}
```

`src/TechStrap.Portal/Components/Pages/Home.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using TechStrap.Portal.Routing;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Components.Pages;

/// <summary>
/// The Portal's root. With <c>TECHSTRAP_PORTAL_DEFAULT_PRODUCT</c> set it sends the visitor to that product's home (a 302; the API is not asked, and the product page decides whether it exists). Without
/// it the root is a neutral page with no list of products, so nothing can be enumerated.
/// </summary>
public partial class Home
{
    [Inject]
    private IOptions<PortalOptions> Portal { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    protected override void OnInitialized()
    {
        if (Portal.Value.DefaultProductKeyOrNull is { } key)
        {
            Navigation.NavigateTo(PortalRoutes.ProductHome(key));
        }
    }
}
```

`src/TechStrap.Portal/Components/Pages/Home.razor`

```diff
--- src/TechStrap.Portal/Components/Pages/Home.razor
+++ src/TechStrap.Portal/Components/Pages/Home.razor
@@ -1,7 +1,7 @@
 @attribute [Route(PortalRoutes.HomeTemplate)]
 
-<PageTitle>TechStrap Portal</PageTitle>
+<PageTitle>@ShellCopy.RootTitle</PageTitle>
 <section class="shell-placeholder">
-    <h1>TechStrap Portal</h1>
-    <p>This host is a placeholder shell. The real interface arrives in a later phase.</p>
+    <h1>@ShellCopy.RootHeading</h1>
+    <p>@ShellCopy.RootIntro</p>
 </section>
```

`src/TechStrap.Portal/Components/Pages/NotFound.razor`

```diff
--- src/TechStrap.Portal/Components/Pages/NotFound.razor
+++ src/TechStrap.Portal/Components/Pages/NotFound.razor
@@ -1,8 +1,8 @@
 @attribute [Route(PortalRoutes.NotFoundTemplate)]
 @layout PortalLayout
 
-<PageTitle>Not found</PageTitle>
+<PageTitle>@ShellCopy.NotFoundTitle</PageTitle>
 <section class="shell-placeholder">
-    <h1>Page not found</h1>
-    <p><a href="/">Back to the start</a></p>
+    <h1>@ShellCopy.NotFoundHeading</h1>
+    <p><a href="@PortalRoutes.HomeTemplate">@ShellCopy.BackToStart</a></p>
 </section>
```

`src/TechStrap.Portal/Components/Pages/Error.razor`

```diff
--- src/TechStrap.Portal/Components/Pages/Error.razor
+++ src/TechStrap.Portal/Components/Pages/Error.razor
@@ -1,10 +1,10 @@
 @attribute [Route(PortalRoutes.ErrorTemplate)]
 @layout PortalLayout
 
-<PageTitle>Error</PageTitle>
+<PageTitle>@ShellCopy.ErrorTitle</PageTitle>
 
 <section class="shell-placeholder" role="alert">
-    <h1>Something went wrong.</h1>
-    <p>We couldn't finish that request. Try again in a minute; if it keeps happening, contact support.</p>
-    <p><a href="/">Back to the start</a></p>
+    <h1>@ShellCopy.ErrorHeading</h1>
+    <p>@ShellCopy.ErrorText</p>
+    <p><a href="@PortalRoutes.HomeTemplate">@ShellCopy.BackToStart</a></p>
 </section>
```

`src/TechStrap.Portal/Components/_Imports.razor`

```diff
--- src/TechStrap.Portal/Components/_Imports.razor
+++ src/TechStrap.Portal/Components/_Imports.razor
@@ -1,7 +1,9 @@
 @using Microsoft.AspNetCore.Components.Routing
 @using Microsoft.AspNetCore.Components.Web
+@using TechStrap.Contracts.Kb
 @using TechStrap.Portal
 @using TechStrap.Portal.Components
 @using TechStrap.Portal.Components.Layout
 @using TechStrap.Portal.Components.Ui
+@using TechStrap.Portal.Products
 @using TechStrap.Portal.Routing
```

`src/TechStrap.Portal/Program.cs`

```diff
--- src/TechStrap.Portal/Program.cs
+++ src/TechStrap.Portal/Program.cs
@@ -6,6 +6,7 @@ using TechStrap.Portal.Clients;
 using TechStrap.Portal.Components;
 using TechStrap.Portal.Components.Ui;
 using TechStrap.Portal.Headers;
+using TechStrap.Portal.Products;
 using TechStrap.Portal.Seo;
 using TechStrap.Portal.Settings;
 
@@ -33,6 +34,9 @@ builder.Services.AddPortalOptions();
 // The two named API clients (reads retried, writes never) and the typed clients: every call forwards the visitor's address (D-019, D-045).
 builder.Services.AddPortalApiClients();
 
+// The product the current request is about, read by the layout (one per request).
+builder.Services.AddScoped<ProductScope>();
+
 // Meta tags, robots.txt and the canonical-host redirect; Seo:BaseUrl comes from the public address (D-045).
 builder.Services.AddPortalSeo(builder.Configuration);
 // Installation-wide switch for the "Powered by TechStrap" footer (D-024); shown unless set to false.
```

`src/TechStrap.Portal/Styles/_components.scss`

```diff
--- src/TechStrap.Portal/Styles/_components.scss
+++ src/TechStrap.Portal/Styles/_components.scss
@@ -81,3 +81,50 @@
   border: 1px solid var(--p-line);
   border-top: 6px solid var(--ts-accent);
 }
+
+// A product's header and footer (PHASE-09a): the logo and name are one link to the product home; the colours are the product's through --ts-accent*.
+.ts-product-header {
+  width: 100%;
+  max-width: 640px;
+  margin: 0 auto;
+  padding: 16px 16px 0;
+}
+
+.ts-product-name {
+  display: inline-flex;
+  gap: 10px;
+  align-items: center;
+  min-height: 44px;
+  font-size: 1.0625rem;
+  font-weight: 600;
+  color: var(--p-ink);
+  text-decoration: none;
+}
+
+.ts-product-logo {
+  width: auto;
+  max-width: 160px;
+  max-height: 32px;
+}
+
+.ts-product-footer {
+  width: 100%;
+  max-width: 640px;
+  margin: 0 auto;
+  padding: 0 16px 16px;
+  font-size: .875rem;
+}
+
+// The product home: a help search and a contact button.
+.ts-help-search-row {
+  display: flex;
+  gap: 8px;
+
+  .form-control {
+    flex: 1;
+  }
+}
+
+.ts-help-contact {
+  margin-top: 24px;
+}
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet build tests/TechStrap.Portal.Tests -c Release`
Expected: 0 warnings, 0 errors.

Run: `dotnet test --project tests/TechStrap.Portal.Tests -c Release`
Expected: PASS: `total: 392, failed: 0` (310 before this task: `ProductThemeViewModelTests` 40, `PortalLayoutTests` 8, `ProductHomeHostTests` 21, `NeutralPagesGuardTests` 7, `RootPageHostTests` 6).

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release`
Expected: PASS: `total: 843, failed: 0` (`ShellHostTests`, `ContentSecurityPolicyHostTests`, `HostHealthSmokeTests` and the other Portal cases are green).

- [ ] **Step 6: Prove each pin with a mutation**

Run `git add -A` first, and run each row from the repository root with `python $T/mut.py <file> --replace "<old>" "<new>" -- dotnet test --project tests/TechStrap.Portal.Tests -c Release --filter-query "/*/*/<Class>/*"`. Files are under `src/TechStrap.Portal/`.

| # | File | Replace | With | Class | Expected |
| --- | --- | --- | --- | --- | --- |
| A | `Program.cs` | `builder.Services.AddScoped<ProductScope>();` | `builder.Services.AddSingleton<ProductScope>();` | `NeutralPagesGuardTests` | KILLED, 2 failed of 7 (one visitor's product themes the next visitor's neutral page) |
| B | `Products/ProductThemeViewModel.cs` | `return uri.Scheme == Uri.UriSchemeHttps \|\| allowLoopbackImages ? uri.AbsoluteUri : null;` | `return uri.AbsoluteUri;` | `ProductThemeViewModelTests` | KILLED, 2 failed of 40 (loopback http outside Development) |
| C | the same | `if (string.IsNullOrEmpty(text) \|\| !BrandingRules.IsAcceptableLogoUrl(text))` | `if (string.IsNullOrEmpty(text) \|\| !Uri.TryCreate(text, UriKind.Absolute, out _))` | `ProductThemeViewModelTests` | KILLED, 13 failed (`javascript:`, `data:`, user info, whitespace) |
| D | the same | `ProductAccent.TryDerive(product.AccentColour, out var colours) ? colours.Accent : null,` | `product.AccentColour,` | `ProductThemeViewModelTests` | KILLED, 8 failed (raw accent text reaches the view model) |
| E | `Products/ProductPageBase.cs` | `        if (error.Kind == ResultErrorKind.NotFound)\n        {\n            Navigation.NotFound();\n            return;\n        }\n` | `` | `NeutralPagesGuardTests` | KILLED, 2 failed (an unknown product must be the neutral 404) |
| F | the same | `context.Response.StatusCode = status;` | `context.Response.StatusCode = status * 0 + 200;` | `ProductHomeHostTests` | KILLED, 4 failed (the failure page must be a 503 or a 429) |
| G | `Components/Layout/PortalLayout.razor.cs` | `protected override void OnInitialized() => Scope.Changed += OnScopeChanged;` | `protected override void OnInitialized() { }` | `ProductHomeHostTests` | KILLED, 10 failed (the static render needs the layout to re-render when the page sets the product) |
| G2 | the same | `public void Dispose() => Scope.Changed -= OnScopeChanged;` | `public void Dispose() { }` | `PortalLayoutTests` | KILLED, 1 failed |
| H | `Components/Layout/ProductHeader.razor` | `}@Theme.DisplayName</a>` | `}@((MarkupString)Theme.DisplayName)</a>` | any class whose test name contains `Name`: filter `/*/*/*Tests/*Name*` | KILLED, 2 failed (the name must be encoded) |
| I | `Components/Pages/Home.razor.cs` | `Navigation.NavigateTo(PortalRoutes.ProductHome(key));` | `` | `RootPageHostTests` | KILLED, 2 failed of 6 |
| J | `Components/Pages/ProductHome.razor` | ` maxlength="@KbLimits.MaxSearchTextChars"` | `` | `ProductHomeHostTests` | KILLED, 1 failed of 21 |

In a table cell a backslash before a pipe stands for the plain pipe character; pass the plain character to the tool.

- [ ] **Step 7: Verify and commit**

Run: `dotnet build TechStrap.slnx -c Release --no-incremental`
Expected: `0 Warning(s)`.

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal`
Expected: PASS: `Tests Passed: 288, Failed: 0`.

```bash
git add -A src tests
git diff --cached --stat
git commit -F - <<'EOF'
feat(portal): the product theme, layout, product scope, product home and root page (PHASE-09a)

ProductThemeViewModel keeps only a derivable accent and an https (or Development loopback) logo.
A page under /p/{key} loads its product into a per-request ProductScope and PortalLayout themes
the page. An unknown, inactive or malformed key renders the same neutral 404 as an unknown route,
and the not-found and error pages are never branded. Adds the product home with a GET search form,
and the root page that redirects to TECHSTRAP_PORTAL_DEFAULT_PRODUCT or stays neutral.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
EOF
```
### Task 5: Log and Sentry redaction, the architecture rules, the Powered-by tests and the closing docs

**Review Focus pin:** Review Focus 1 (token leakage in logs and Sentry; the contact prefill's `name` and `email`). Pinned here by `RequestLogRedactionHostTests` (a `/t/{token}` address and the prefill query never reach a log event at Verbose, each with a control that proves the request line was logged and masked), `PiiRedactionQueryValueTests` and `SensitiveQuerySentryProcessorTests` (the masking rule itself, and the same for Sentry). The architecture rules (`PortalRuleTests`) keep later PRs inside the Portal's conventions: HTTP only in `Clients/`, no inline script or style, no interactive render mode, and `MarkupString` only where the allow-list says.

**Files:**

- Create: `docs/development/PORTAL-APP.md`
- Modify: `README.md`
- Modify: `docs/architecture/00-DISCOVERY-INDEX.md`
- Modify: `docs/architecture/04-DECISION-LOG.md`
- Modify: `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`
- Modify: `docs/architecture/PHASE-09-public-portal.md`
- Modify: `src/TechStrap.Hosting/Logging/PiiRedactionEnricher.cs`
- Modify: `src/TechStrap.Hosting/Sentry/SensitiveQuerySentryProcessor.cs`
- Modify: `src/TechStrap.Portal/Components/Pages/StyleGuide.razor`
- Test (create): `tests/TechStrap.Api.Tests/Redaction/PiiRedactionQueryValueTests.cs`
- Test (create): `tests/TechStrap.Architecture.Tests/PortalRuleTests.cs`
- Test (create): `tests/TechStrap.Architecture.Tests/PortalRules.cs`
- Test (create): `tests/TechStrap.Portal.Tests/Logging/RequestLogRedactionHostTests.cs`
- Test (create): `tests/TechStrap.Portal.Tests/PoweredByHostTests.cs`
- Test (modify): `scripts/tests/RepositoryDocs.Tests.ps1`
- Test (modify): `tests/TechStrap.Api.Tests/SensitiveQuerySentryProcessorTests.cs`


**Interfaces:**
- Consumes: `PiiRedactionEnricher.RedactText`, `TokenMarker` and the Sentry processors (Hosting), `PortalFactory.LogSink` and `VerboseLogging` (Task 2), `PoweredByOptions`, `PoweredByFooter` (PHASE-02), `ProjectGraph`, `ProjectNode`, `ReferenceRules.Portal`, `AdminRules.InlineMarkupViolations` (Architecture.Tests), the Portal's pages (Task 4).
- Produces:
  - `PiiRedactionEnricher.QueryValueMarker = "[redacted]"`: `RedactText` first masks the value of a `name` or `email` query parameter (matched after decoding the parameter's name, case-insensitively, only after `?`, `&` or the start of the text) in every property value of every host's logs. `SensitiveQuerySentryProcessor.Scrub` masks the same parameters and a 43-character token directly under `/t/` or `/T/`, wherever an address, a header, a breadcrumb or a span carries one.
  - `PortalRules` (Architecture.Tests, public static partial): `AllowedPackages`; `MarkupStringSites` (an empty list; 09b adds `Features/.../CustomerMessageBody.razor`, 09c `Features/.../KbArticleBody.razor`); `PackageViolations(ProjectNode)`; `HttpClientViolations(files)` (HttpClient, IHttpClientFactory or AddHttpClient outside `Clients/`, comments ignored); `InlineMarkupViolations(files)` (the Admin's rule); `MarkupStringViolations(files, allowedSites = null)` (every file that names `MarkupString` or `AddMarkupContent` must be listed and every listed file must still use it); `InteractivityViolations(files)` (no `AddInteractive*`, `@rendermode`, `RenderMode.Interactive*`; comments ignored); `Sources(repositoryRoot)`.
  - `docs/development/PORTAL-APP.md` (linked from the README), the PHASE-09 ticks for `P09-T01`, `T03`, `T05`, `T17`, `T19` and `T22` and two deliverables, the roadmap and discovery rows "09a complete (pending merge)", and the D-045 consequences "as built".

- [ ] **Step 1: Write the failing redaction tests**

The redactor tests feed `RedactText` a table of query strings (`?name=`, `NAME=`, `%6Eame=`, a request line, a fragment) and a table of things that must be left alone (`username=`, `filename=`, a sentence that says `name=`). The Sentry tests add `name` and `email` to the masked parameters and a `/t/{token}` table. The host tests run the Portal at Verbose and scan every log event; each has a control (the masked marker is in the log), so the scan cannot pass because nothing was logged.

`tests/TechStrap.Api.Tests/Redaction/PiiRedactionQueryValueTests.cs`

```csharp
using Serilog;
using Serilog.Events;
using TechStrap.Hosting.Logging;

namespace TechStrap.Api.Tests.Redaction;

/// <summary>
/// P09-T17 / T21: the contact page may be opened with <c>?name=...&amp;email=...</c> (a prefill from the product's own app). A name cannot be recognised by pattern, so the value of these two query
/// parameters is masked wherever a logged text carries a query string, in addition to the email and token patterns the redactor already has. The match is on the parameter's decoded name, so
/// <c>%6Eame=</c> and <c>NAME=</c> are caught, and on nothing else: a text that merely says "name=" is left alone.
/// </summary>
public sealed class PiiRedactionQueryValueTests
{
    private sealed class Sink : Serilog.Core.ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    [Theory]
    [InlineData("?name=Jane%20Doe", "?name=[redacted]")]
    [InlineData("?name=Jane+Doe&email=jane%40example.com", "?name=[redacted]&email=[redacted]")]
    [InlineData("?subject=Hi&name=Jane&page=2", "?subject=Hi&name=[redacted]&page=2")]
    [InlineData("?NAME=Jane&Email=a@b.example", "?NAME=[redacted]&Email=[redacted]")]
    [InlineData("?%6Eame=Jane&%65mail=x", "?%6Eame=[redacted]&%65mail=[redacted]")]
    [InlineData("?name=", "?name=[redacted]")]
    [InlineData("?name=Jane&name=Joe", "?name=[redacted]&name=[redacted]")]
    [InlineData("name=Jane", "name=[redacted]")]
    [InlineData("Request starting HTTP/1.1 GET http://localhost/p/paperplane/contact?name=Jane%20Doe&email=jane.doe%40example.com - -", "Request starting HTTP/1.1 GET http://localhost/p/paperplane/contact?name=[redacted]&email=[redacted] - -")]
    [InlineData("https://portal.test/p/x/contact?name=O%27Brien#top", "https://portal.test/p/x/contact?name=[redacted]#top")]
    [InlineData("?subject=Hi%20there", "?subject=Hi%20there")]
    public void The_value_of_name_and_email_in_a_query_string_is_masked_and_the_rest_is_kept(string text, string expected) =>
        PiiRedactionEnricher.RedactText(text).ShouldBe(expected);

    [Theory]
    [InlineData("a message that says name=nothing but is not a query")]
    [InlineData("?username=jane&nickname=j&surname=d&filename=f.txt&email2=x")]
    [InlineData("?%6Eam%65s=1")]
    [InlineData("hostname=db&email-from=x")]
    [InlineData("Name is Jane")]
    [InlineData("")]
    public void Anything_that_is_not_a_name_or_email_parameter_is_left_alone(string text) =>
        PiiRedactionEnricher.RedactText(text).ShouldBe(text);

    [Fact]
    public void A_logged_query_string_property_is_masked_before_any_sink_sees_it()
    {
        var sink = new Sink();
        using var log = new LoggerConfiguration().Enrich.With<PiiRedactionEnricher>().WriteTo.Sink(sink).CreateLogger();

        log.Information("Request starting {Path}{QueryString}", "/p/paperplane/contact", "?name=Jane%20Doe&email=jane.doe%40example.com");

        var rendered = sink.Events.Single().RenderMessage();
        rendered.ShouldBe("Request starting \"/p/paperplane/contact\"\"?name=[redacted]&email=[redacted]\"");
        rendered.ShouldNotContain("Jane");
        rendered.ShouldNotContain("jane.doe");
    }

    [Fact]
    public void A_very_long_value_is_masked_in_one_pass()
    {
        PiiRedactionEnricher.RedactText("?name=" + new string('x', 100_000)).ShouldBe("?name=[redacted]");
    }
}
```

`tests/TechStrap.Api.Tests/SensitiveQuerySentryProcessorTests.cs`

```diff
--- tests/TechStrap.Api.Tests/SensitiveQuerySentryProcessorTests.cs
+++ tests/TechStrap.Api.Tests/SensitiveQuerySentryProcessorTests.cs
@@ -35,6 +35,53 @@ public sealed class SensitiveQuerySentryProcessorTests
     public void The_value_of_search_and_q_is_masked_and_the_rest_of_the_address_is_kept(string text, string expected) =>
         SensitiveQuerySentryProcessor.Scrub(text).ShouldBe(expected);
 
+    // P09-T17 / T21: the Portal's contact page may be opened with a name and an email in the address, and its ticket address carries the access token in the path.
+    [Theory]
+    [InlineData("?name=Jane%20Doe", "?name=[redacted]")]
+    [InlineData("?subject=Hi&name=Jane+Doe&email=jane%40example.com&page=2", "?subject=Hi&name=[redacted]&email=[redacted]&page=2")]
+    [InlineData("?NAME=Jane&Email=a@b.example", "?NAME=[redacted]&Email=[redacted]")]
+    [InlineData("?%6Eame=Jane&%65mail=x", "?%6Eame=[redacted]&%65mail=[redacted]")]
+    [InlineData("https://portal.test/p/orbitly/contact?name=Jane&email=jane%40example.com#top", "https://portal.test/p/orbitly/contact?name=[redacted]&email=[redacted]#top")]
+    public void The_value_of_name_and_email_is_masked_so_the_contact_page_prefill_never_reaches_Sentry(string text, string expected) =>
+        SensitiveQuerySentryProcessor.Scrub(text).ShouldBe(expected);
+
+    [Theory]
+    [InlineData("https://portal.test/t/AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE", "https://portal.test/t/[token]")]
+    [InlineData("https://portal.test/t/AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE/attachments/11111111-2222-3333-4444-555555555555", "https://portal.test/t/[token]/attachments/11111111-2222-3333-4444-555555555555")]
+    [InlineData("/t/AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE?x=1", "/t/[token]?x=1")]
+    [InlineData("GET /T/AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE failed", "GET /T/[token] failed")]
+    [InlineData("https://portal.test/t/AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE?name=Jane", "https://portal.test/t/[token]?name=[redacted]")]
+    public void An_access_token_in_a_ticket_path_is_masked_so_the_ticket_address_never_reaches_Sentry(string text, string expected) =>
+        SensitiveQuerySentryProcessor.Scrub(text).ShouldBe(expected);
+
+    [Theory]
+    [InlineData("/t/short")]
+    [InlineData("/t/AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdEx")]
+    [InlineData("/t/AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCd")]
+    [InlineData("/ticket/AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE")]
+    [InlineData("/api/customer/ticket")]
+    public void Only_a_43_character_token_directly_under_t_is_masked(string text) =>
+        SensitiveQuerySentryProcessor.Scrub(text).ShouldBe(text);
+
+    [Fact]
+    public void A_ticket_address_in_a_request_a_breadcrumb_and_a_span_is_masked_everywhere_the_search_is()
+    {
+        const string url = "https://portal.test/t/AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE?name=Jane";
+        var @event = new SentryEvent();
+        @event.Request.Url = url;
+        @event.Request.QueryString = "?name=Jane";
+        @event.Request.Headers["Referer"] = url;
+
+        var scrubbed = new SensitiveQuerySentryProcessor().Process(@event)!;
+        var breadcrumb = SensitiveQuerySentryProcessor.ScrubBreadcrumb(new Breadcrumb("GET " + url, "http", new Dictionary<string, string> { ["url"] = url }, "http", BreadcrumbLevel.Info), new SentryHint())!;
+
+        scrubbed.Request.Url.ShouldBe("https://portal.test/t/[token]?name=[redacted]");
+        scrubbed.Request.QueryString.ShouldBe("?name=[redacted]");
+        scrubbed.Request.Headers["Referer"].ShouldBe("https://portal.test/t/[token]?name=[redacted]");
+        breadcrumb.Message.ShouldBe("GET https://portal.test/t/[token]?name=[redacted]");
+        breadcrumb.Data!["url"].ShouldBe("https://portal.test/t/[token]?name=[redacted]");
+    }
+
     [Theory]
     [InlineData("?status=Open&page=2")]
     [InlineData("?research=1&faq=2&query=3&squash=4")]
```

`tests/TechStrap.Portal.Tests/Logging/RequestLogRedactionHostTests.cs`

```csharp
using Serilog.Events;
using TechStrap.Contracts.Products;

namespace TechStrap.Portal.Tests.Logging;

/// <summary>
/// P09-T17 at the host, at Verbose: the framework logs every request's path and query, so a <c>/t/{token}</c> address and the contact page's <c>?name=&amp;email=</c> prefill (09b) must never reach a log
/// event in the clear, in its message or in any property. The first checks of each test are the controls: the request line really was logged, with the secret masked, so the scan can see it.
/// </summary>
public sealed class RequestLogRedactionHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string Token = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE";   // exactly 43 base64url characters

    private static string Everything(LogEvent e) => string.Join('\n', [e.RenderMessage(), e.Exception?.ToString() ?? string.Empty, .. e.Properties.Values.Select(v => v.ToString())]);

    private static PortalFactory Verbose()
    {
        var factory = new PortalFactory("Production", PortalFactory.VerboseLogging);
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", new PublicProductDto("paperplane", "Paperplane", null, "#F59E0B", "#000000", "#9D6507"));
        return factory;
    }

    private static void AssertVerboseWasCaptured(PortalFactory factory) =>
        factory.LogSink.Events.ShouldContain(e => e.Level <= LogEventLevel.Debug, "the Verbose setting must have taken effect, or this only scanned Information and above");

    [Theory]
    [InlineData("/t/" + Token)]
    [InlineData("/T/" + Token + "/")]
    [InlineData("/t/" + Token + "/attachments/11111111-2222-3333-4444-555555555555")]
    [InlineData("/t/" + Token + "?x=1")]
    public async Task A_ticket_address_never_puts_the_token_in_a_log_event(string path)
    {
        await using var factory = Verbose();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
        AssertVerboseWasCaptured(factory);
        factory.LogSink.Events.ShouldContain(e => Everything(e).Contains("[token]", StringComparison.Ordinal), "control: the request line was logged, with the token masked");
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text => !text.Contains(Token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_prefill_query_values_of_the_contact_page_never_reach_a_log_event()
    {
        await using var factory = Verbose();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/p/paperplane/contact?subject=Printer&name=Jane%20Doe&email=jane.doe%40example.com", Ct);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound, "the contact page arrives in PHASE-09b; the request is logged all the same");
        AssertVerboseWasCaptured(factory);
        factory.LogSink.Events.ShouldContain(e => Everything(e).Contains("name=[redacted]", StringComparison.Ordinal), "control: the query string was logged, with the name masked");
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text =>
            !text.Contains("Jane", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("Doe", StringComparison.Ordinal)
            && !text.Contains("jane.doe", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("example.com", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("/p/paperplane?name=Jane+Doe")]
    [InlineData("/p/paperplane?NAME=Jane%20Doe&EMAIL=jane%40example.com")]
    [InlineData("/p/paperplane?%6Eame=Jane%20Doe")]
    [InlineData("/p/paperplane/kb/search?q=a&name=Jane%20Doe")]
    public async Task A_name_in_the_query_of_any_page_is_masked_too(string path)
    {
        await using var factory = Verbose();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        AssertVerboseWasCaptured(factory);
        factory.LogSink.Events.ShouldContain(e => Everything(e).Contains("[redacted]", StringComparison.Ordinal), "control: the query string was logged, masked");
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text => !text.Contains("Jane", StringComparison.OrdinalIgnoreCase) && !text.Contains("Doe", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_name_the_page_echoes_never_reaches_the_log_through_the_api_call_either()
    {
        await using var factory = Verbose();
        using var client = factory.CreateClient();

        await client.GetAsync("/p/paperplane?name=Jane%20Doe", Ct);

        factory.Api.Requests.ShouldHaveSingleItem().Query.ShouldBeEmpty("the Portal sends the API the product key and nothing from the visitor's query");
    }
}
```

- [ ] **Step 2: Write the architecture rules and their tests**

The rules are pure functions over text or a parsed project, so each is proven with a deliberate violation. The real-project tests assert the scan saw the right files, so a broken reader cannot pass vacuously.

`tests/TechStrap.Architecture.Tests/PortalRules.cs`

```csharp
using System.Text.RegularExpressions;

namespace TechStrap.Architecture.Tests;

/// <summary>
/// Rules for the Portal (PHASE-09 T19, D-045), modelled on <see cref="AdminRules"/>. The Portal is a static-server-rendered, anonymous front of the API: no data access, HTTP only in
/// <c>Clients/</c>, no inline script or style (the CSP allows none), no interactive render mode, and a short, argued list of places that turn text into markup. Every rule is a pure function over
/// text or a parsed project so the tests can feed it a deliberately bad sample and prove it fails. Paths are relative to the repository root, with forward slashes.
/// </summary>
public static partial class PortalRules
{
    private const string PortalRoot = "src/TechStrap.Portal/";

    /// <summary>The only packages the Portal project may reference. A new package is a design decision: add it here in the same commit.</summary>
    public static IReadOnlySet<string> AllowedPackages { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "AspNetCore.SassCompiler",
        "GitVersion.MsBuild",
        "Microsoft.Web.LibraryManager.Build",
        "SyntaxCircus.AspNetCore.Common",
        "SyntaxCircus.AspNetCore.Serilog",
        "SyntaxCircus.Blazor.Components",
        "SyntaxCircus.Blazor.Seo",
        "SyntaxCircus.Common",
        "SyntaxCircus.DotEnv",
        "SyntaxCircus.Http.Resilience",
        "SyntaxCircus.Observability",
    };

    /// <summary>
    /// The files (relative to src/TechStrap.Portal) that may turn API text into markup, which is where a stored-XSS bug would live. The API sanitises the HTML before it sends it, and the Portal does not
    /// sanitise again, so each site is argued for in the commit that adds it: 09b adds <c>CustomerMessageBody</c> (a ticket message body) and 09c adds <c>KbArticleBody</c> (a published article). In 09a the
    /// list is empty: every other string the Portal shows is plain text, and Razor encodes it.
    /// </summary>
    public static IReadOnlyList<string> MarkupStringSites { get; } = [];

    [GeneratedRegex(@"\b(?:I|Add)?HttpClient(?:Factory)?\b", RegexOptions.CultureInvariant)]
    private static partial Regex HttpClientUse();

    [GeneratedRegex(@"\bMarkupString\b|\bAddMarkupContent\b", RegexOptions.CultureInvariant)]
    private static partial Regex MarkupSite();

    // Anything that opts a component or the host in to an interactive render mode (a circuit or WebAssembly). The Portal has none: every page is static server rendering.
    [GeneratedRegex(@"\bAddInteractive\w+|@rendermode\b|\brendermode\s*=|\bRenderMode\s*\.\s*Interactive\w*|\bInteractive(?:Server|WebAssembly|Auto)\b|\bIComponentRenderMode\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Interactivity();

    // Razor comments, HTML comments, block comments and whole-line or trailing slash comments (a "//" that follows a colon, a quote or a word character is part of a URL or a string, not a comment).
    [GeneratedRegex(@"@\*.*?\*@|<!--.*?-->|/\*.*?\*/|(?<![:""'\w])//[^\r\n]*", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex Comments();

    public static IReadOnlyList<string> PackageViolations(ProjectNode portal) =>
        [.. portal.PackageReferences
            .Where(package => !AllowedPackages.Contains(package))
            .Order(StringComparer.Ordinal)
            .Select(package => $"{portal.Name} must not reference package {package}. The Portal talks to the API only; add the package to PortalRules.AllowedPackages if it is a reviewed choice.")];

    /// <summary>HttpClient and IHttpClientFactory belong in <c>Clients/</c> and nowhere else in the Portal: a page, a component, a layout or a base class reaches the API through a typed client.</summary>
    public static IReadOnlyList<string> HttpClientViolations(IEnumerable<(string Path, string Text)> files) =>
        [.. files
            .Select(f => (Path: Normalize(f.Path), Text: Comments().Replace(f.Text, string.Empty)))
            .Where(f => !f.Path.StartsWith(PortalRoot + "Clients/", StringComparison.Ordinal) && HttpClientUse().IsMatch(f.Text))
            .Select(f => $"{f.Path} uses HttpClient or IHttpClientFactory. Only Clients/ may; everything else calls the API through a typed client.")];

    /// <summary>An inline script or style, the import map, or an on* handler attribute: the Content-Security-Policy allows none of them. The same check as the Admin's.</summary>
    public static IReadOnlyList<string> InlineMarkupViolations(IEnumerable<(string Path, string Text)> files) => AdminRules.InlineMarkupViolations(files);

    /// <summary>
    /// Every file that names <c>MarkupString</c> or <c>AddMarkupContent</c> must be in <paramref name="allowedSites"/> (default <see cref="MarkupStringSites"/>), and every listed file must still use it, so the
    /// list is the exact set. A file is judged by its text, comments included, as the Admin's site test is.
    /// </summary>
    public static IReadOnlyList<string> MarkupStringViolations(IEnumerable<(string Path, string Text)> files, IReadOnlyList<string>? allowedSites = null)
    {
        var allowed = allowedSites ?? MarkupStringSites;
        var users = files
            .Select(f => (Path: ProjectRelative(f.Path), f.Text))
            .Where(f => MarkupSite().IsMatch(f.Text))
            .Select(f => f.Path)
            .ToHashSet(StringComparer.Ordinal);
        var violations = users
            .Where(path => !allowed.Contains(path, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(path => $"{path} turns text into markup (MarkupString or AddMarkupContent) but is not in PortalRules.MarkupStringSites. Argue for the site in the commit that adds it.")
            .ToList();
        violations.AddRange(allowed
            .Where(path => !users.Contains(path))
            .Order(StringComparer.Ordinal)
            .Select(path => $"{path} is in PortalRules.MarkupStringSites but no longer uses MarkupString or AddMarkupContent. Remove it from the list."));
        return violations;
    }

    /// <summary>The Portal is static server rendering only: no file opts in to an interactive render mode, so no page has a circuit (D-045). Comments are ignored.</summary>
    public static IReadOnlyList<string> InteractivityViolations(IEnumerable<(string Path, string Text)> files) =>
        [.. files
            .Select(f => (Path: Normalize(f.Path), Text: Comments().Replace(f.Text, string.Empty)))
            .Where(f => Interactivity().IsMatch(f.Text))
            .Select(f => $"{f.Path} opts in to an interactive render mode. The Portal is static server rendering only; there is no circuit.")];

    /// <summary>Every .razor, .razor.cs and .cs file under src/TechStrap.Portal, except bin, obj and node_modules folders inside the project. Paths are relative to the repository root.</summary>
    public static IEnumerable<(string Path, string Text)> Sources(string repositoryRoot)
    {
        var skipped = new HashSet<string>(["bin", "obj", "node_modules"], StringComparer.OrdinalIgnoreCase);
        var portal = Path.Combine(repositoryRoot, "src", ReferenceRules.Portal);
        return Directory.EnumerateFiles(portal, "*", SearchOption.AllDirectories)
            .Select(f => (Full: f, Relative: Normalize(Path.GetRelativePath(repositoryRoot, f))))
            .Where(f => (f.Relative.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) || f.Relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                        && !f.Relative.Split('/')[..^1].Any(skipped.Contains))
            .Select(f => (f.Relative, File.ReadAllText(f.Full)));
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string ProjectRelative(string path)
    {
        var normalized = Normalize(path);
        return normalized.StartsWith(PortalRoot, StringComparison.Ordinal) ? normalized[PortalRoot.Length..] : normalized;
    }
}
```

`tests/TechStrap.Architecture.Tests/PortalRuleTests.cs`

```csharp
namespace TechStrap.Architecture.Tests;

public sealed class PortalRuleTests
{
    private const string Page = "src/TechStrap.Portal/Components/Pages/Page.razor";
    private const string Code = "src/TechStrap.Portal/Products/Thing.cs";

    private static ProjectNode PortalWith(params string[] packages) =>
        new(ReferenceRules.Portal, new HashSet<string>(), packages.ToHashSet(), new HashSet<string>());

    // ---- Packages -----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_Portal_project_references_only_allowed_packages()
    {
        var portal = ProjectGraph.LoadSourceProjects(ProjectGraph.FindRepositoryRoot())[ReferenceRules.Portal];

        portal.PackageReferences.ShouldContain("SyntaxCircus.Blazor.Seo", "the scan must read the real Portal project");
        PortalRules.PackageViolations(portal).ShouldBeEmpty();
    }

    [Fact]
    public void The_Portal_allow_list_is_exactly_what_the_project_references_today()
    {
        PortalRules.AllowedPackages.Order(StringComparer.Ordinal).ShouldBe(
        [
            "AspNetCore.SassCompiler",
            "GitVersion.MsBuild",
            "Microsoft.Web.LibraryManager.Build",
            "SyntaxCircus.AspNetCore.Common",
            "SyntaxCircus.AspNetCore.Serilog",
            "SyntaxCircus.Blazor.Components",
            "SyntaxCircus.Blazor.Seo",
            "SyntaxCircus.Common",
            "SyntaxCircus.DotEnv",
            "SyntaxCircus.Http.Resilience",
            "SyntaxCircus.Observability",
        ]);
    }

    [Fact]
    public void The_rule_flags_EF_Npgsql_Auth_and_any_unlisted_package()
    {
        var violations = PortalRules.PackageViolations(PortalWith("SyntaxCircus.Common", "Microsoft.EntityFrameworkCore", "Npgsql", "SyntaxCircus.Blazor.Auth", "Newtonsoft.Json"));

        violations.Count.ShouldBe(4);
        violations.ShouldContain(v => v.Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        violations.ShouldContain(v => v.Contains("Npgsql", StringComparison.Ordinal));
        violations.ShouldContain(v => v.Contains("SyntaxCircus.Blazor.Auth", StringComparison.Ordinal));
        violations.ShouldContain(v => v.Contains("Newtonsoft.Json", StringComparison.Ordinal));
        violations.ShouldAllBe(v => v.Contains("TechStrap.Portal", StringComparison.Ordinal));
    }

    // ---- HttpClient only in Clients/ ---------------------------------------------------------------------------------------------------

    [Fact]
    public void No_Portal_file_outside_Clients_uses_HttpClient()
    {
        var files = PortalRules.Sources(ProjectGraph.FindRepositoryRoot()).ToList();

        files.ShouldContain(f => f.Path.EndsWith("App.razor", StringComparison.Ordinal), "the scan must see the Portal components");
        files.ShouldContain(f => f.Path.EndsWith(".razor.cs", StringComparison.Ordinal), "the scan must see the code-behind files");
        files.ShouldContain(f => f.Path.EndsWith("Clients/ApiConnection.cs", StringComparison.Ordinal) && f.Text.Contains("IHttpClientFactory", StringComparison.Ordinal), "the one place that may use it must be in the scan");
        PortalRules.HttpClientViolations(files).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("src/TechStrap.Portal/Components/Pages/Page.razor", "@inject HttpClient Http")]
    [InlineData("src/TechStrap.Portal/Components/Pages/Page.razor", "@inject IHttpClientFactory Factory")]
    [InlineData("src/TechStrap.Portal/Components/Pages/Page.razor.cs", "[Inject] public HttpClient Http { get; set; } = default!;")]
    [InlineData("src/TechStrap.Portal/Components/Pages/Page.razor.cs", "public Page(HttpClient http) { }")]
    [InlineData("src/TechStrap.Portal/Products/Base.cs", "protected HttpClient Http => null!;")]
    [InlineData("src/TechStrap.Portal/Program.cs", "builder.Services.AddHttpClient(\"x\");")]
    public void A_file_outside_Clients_that_uses_HttpClient_is_flagged(string path, string text)
    {
        PortalRules.HttpClientViolations([(path, text)]).Count.ShouldBe(1);
    }

    [Fact]
    public void A_comment_that_names_HttpClient_is_not_a_hit()
    {
        PortalRules.HttpClientViolations(
        [
            ("src/TechStrap.Portal/Program.cs", "// the host-wide HttpClient logging default\nvar x = 1; // IHttpClientFactory is only in Clients/"),
            ("src/TechStrap.Portal/Components/Pages/Page.razor", "@* never inject HttpClient here *@<p>x</p>"),
        ]).ShouldBeEmpty();
    }

    [Fact]
    public void HttpClient_inside_Clients_is_not_flagged_and_a_look_alike_name_is_not_a_hit()
    {
        PortalRules.HttpClientViolations(
        [
            ("src/TechStrap.Portal/Clients/ApiConnection.cs", "HttpClient http; IHttpClientFactory factory;"),
            ("src/TechStrap.Portal/Components/Pages/Page.razor", "@inject IPublicProductClient Products"),
            ("src/TechStrap.Portal/Components/Pages/Page.razor.cs", "private HttpContext Context; private IHttpContextAccessor Accessor;"),
        ]).ShouldBeEmpty("Clients/ may use HttpClient; a different type whose name resembles it is not the rule's target");
    }

    // ---- No inline script or style ---------------------------------------------------------------------------------------------------

    [Fact]
    public void No_Portal_file_has_an_inline_script_a_style_element_the_import_map_or_an_event_handler_attribute()
    {
        var files = PortalRules.Sources(ProjectGraph.FindRepositoryRoot()).ToList();

        files.ShouldContain(f => f.Path.EndsWith("App.razor", StringComparison.Ordinal) && f.Text.Contains("blazor.web.js", StringComparison.Ordinal), "App.razor loads the framework script by src");
        PortalRules.InlineMarkupViolations(files).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<style>.a { color: red }</style>")]
    [InlineData("<ImportMap />")]
    [InlineData("<button onclick=\"x()\">Go</button>")]
    public void A_deliberate_inline_script_style_import_map_or_handler_is_flagged(string markup)
    {
        PortalRules.InlineMarkupViolations([(Page, markup)]).Count.ShouldBe(1);
    }

    [Fact]
    public void An_external_script_is_not_flagged()
    {
        PortalRules.InlineMarkupViolations([(Page, "<script src=\"app.js\"></script>")]).ShouldBeEmpty();
    }

    // ---- MarkupString sites -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void In_09a_no_Portal_file_turns_text_into_markup()
    {
        var files = PortalRules.Sources(ProjectGraph.FindRepositoryRoot()).ToList();

        files.Count.ShouldBeGreaterThan(20, "the scan must see the Portal sources");
        PortalRules.MarkupStringSites.ShouldBeEmpty("09b adds CustomerMessageBody and 09c adds KbArticleBody, each in the commit that argues for it");
        PortalRules.MarkupStringViolations(files).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(Page, "@((MarkupString)Html)")]
    [InlineData(Page, "@(new MarkupString(Html))")]
    [InlineData(Page, "<div>@((Microsoft.AspNetCore.Components.MarkupString)Html)</div>")]
    [InlineData(Code, "var m = new MarkupString(html);")]
    [InlineData(Code, "builder.AddMarkupContent(0, html);")]
    [InlineData("src/TechStrap.Portal/Components/Pages/Page.razor.cs", "public MarkupString Body => (MarkupString)Html;")]
    public void A_deliberate_markup_site_is_flagged_while_the_allow_list_is_empty(string path, string text)
    {
        var violations = PortalRules.MarkupStringViolations([(path, text)]);

        violations.Count.ShouldBe(1);
        violations[0].ShouldContain(path.Replace("src/TechStrap.Portal/", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void A_listed_site_passes_and_an_unlisted_one_and_a_listed_file_that_no_longer_uses_it_are_flagged()
    {
        string[] allowed = ["Features/Kb/KbArticleBody.razor"];

        PortalRules.MarkupStringViolations([("src/TechStrap.Portal/Features/Kb/KbArticleBody.razor", "@((MarkupString)Html)")], allowed).ShouldBeEmpty();
        PortalRules.MarkupStringViolations(
        [
            ("src/TechStrap.Portal/Features/Kb/KbArticleBody.razor", "@((MarkupString)Html)"),
            ("src/TechStrap.Portal/Features/Kb/KbArticleCard.razor", "@((MarkupString)Summary)"),
        ], allowed).Count.ShouldBe(1);
        PortalRules.MarkupStringViolations([("src/TechStrap.Portal/Features/Kb/KbArticleBody.razor", "<div>@Html</div>")], allowed)
            .ShouldHaveSingleItem().ShouldContain("no longer");
    }

    // ---- Static server rendering only ---------------------------------------------------------------------------------------------------

    [Fact]
    public void No_Portal_file_opts_in_to_an_interactive_render_mode()
    {
        PortalRules.InteractivityViolations(PortalRules.Sources(ProjectGraph.FindRepositoryRoot())).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Program.cs", "builder.Services.AddRazorComponents().AddInteractiveServerComponents();")]
    [InlineData("Program.cs", "app.MapRazorComponents<App>().AddInteractiveServerRenderMode();")]
    [InlineData("Program.cs", "builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();")]
    [InlineData("Components/Routes.razor", "<Router @rendermode=\"InteractiveServer\" />")]
    [InlineData("Components/Pages/Page.razor", "@rendermode InteractiveServer")]
    [InlineData("Components/App.razor", "<Routes @rendermode=\"RenderMode.InteractiveServer\" />")]
    [InlineData("Components/Pages/Page.razor.cs", "static IComponentRenderMode Mode = RenderMode.InteractiveAuto;")]
    public void A_deliberate_interactive_render_mode_is_flagged(string relativePath, string text)
    {
        PortalRules.InteractivityViolations([("src/TechStrap.Portal/" + relativePath, text)]).Count.ShouldBe(1);
    }

    [Fact]
    public void A_comment_that_names_the_forbidden_things_is_not_a_hit()
    {
        PortalRules.InteractivityViolations([(Page, "@* No @rendermode here: this site is static server rendering. *@")]).ShouldBeEmpty();
        PortalRules.InteractivityViolations([(Code, "// No AddInteractiveServerComponents: static rendering only.")]).ShouldBeEmpty();
    }

    // ---- The scan ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_scan_skips_build_output_and_includes_every_source_kind()
    {
        var files = PortalRules.Sources(ProjectGraph.FindRepositoryRoot()).Select(f => f.Path.Replace('\\', '/')).ToList();

        files.ShouldNotContain(path => path.Contains("/obj/", StringComparison.Ordinal) || path.Contains("/bin/", StringComparison.Ordinal));
        files.ShouldContain("src/TechStrap.Portal/Program.cs");
        files.ShouldContain(path => path.EndsWith("Components/Pages/ProductHome.razor", StringComparison.Ordinal));
        files.ShouldContain(path => path.EndsWith("Components/Pages/Home.razor.cs", StringComparison.Ordinal));
    }
}
```

- [ ] **Step 3: Write the Powered-by tests**

The footer, the option and its start-up validation exist since PHASE-02 and D-024. These tests pin them on every page type, so their first run passes; the mutations below are their evidence.

`tests/TechStrap.Portal.Tests/PoweredByHostTests.cs`

```csharp
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Components.Ui;

namespace TechStrap.Portal.Tests;

/// <summary>
/// P09-T22 (D-024): the "Powered by TechStrap" line is the only place the TechStrap name appears on the Portal. By default every page type carries exactly one link to the repository and no other
/// TechStrap text; the installation setting <c>TECHSTRAP_PORTAL_SHOW_POWERED_BY=false</c> removes the whole line everywhere; it defaults to true; and a value that is not true or false stops the start.
/// </summary>
public sealed partial class PoweredByHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string Href = "https://github.com/Syntax-Circus/techstrap";

    [GeneratedRegex("""<footer class="ts-powered">.*?</footer>""", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex FooterElement();

    // The root, a themed product page, the calm failure page of a product, an unknown product, an unknown route, the not-found page and the error page.
    public static TheoryData<string> PageTypes() => ["/", "/p/paperplane", "/p/down", "/p/gone", "/no/such/route", "/not-found", "/error"];

    private static PortalFactory Factory(string? setting = null)
    {
        var settings = setting is null ? null : new Dictionary<string, string?> { ["TECHSTRAP_PORTAL_SHOW_POWERED_BY"] = setting };
        var factory = new PortalFactory("Production", settings);
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", new PublicProductDto("paperplane", "Paperplane", "https://cdn.example.com/logo.png", "#F59E0B", "#000000", "#9D6507"));
        factory.Api.OnStatus(HttpMethod.Get, "/api/public/products/down", System.Net.HttpStatusCode.ServiceUnavailable);
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/gone", System.Net.HttpStatusCode.NotFound, "product-not-found", "No such product.");
        return factory;
    }

    // Whatever the status: a 404 and a 503 page are pages too.
    private static async Task<string> BodyAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, Ct);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    [Theory]
    [MemberData(nameof(PageTypes))]
    public async Task By_default_every_page_type_has_exactly_one_link_to_the_repository_and_no_other_TechStrap_text(string path)
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();

        var html = await BodyAsync(client, path);

        Regex.Matches(html, "href=\"" + Regex.Escape(Href) + "\"").Count.ShouldBe(1, path);
        FooterElement().Matches(html).Count.ShouldBe(1, path);
        var footer = FooterElement().Match(html).Value;
        footer.ShouldContain("Powered by <a href=\"" + Href + "\" rel=\"noopener\">TechStrap</a>");
        FooterElement().Replace(html, string.Empty).ShouldNotContain("TechStrap", Case.Insensitive, $"{path}: the footer line is the only TechStrap text");
    }

    [Theory]
    [MemberData(nameof(PageTypes))]
    public async Task When_the_setting_is_false_no_page_type_has_the_line_the_link_or_the_name(string path)
    {
        await using var factory = Factory("false");
        using var client = factory.CreateClient();

        var html = await BodyAsync(client, path);

        html.ShouldNotContain("ts-powered", Case.Sensitive, path);
        html.ShouldNotContain(Href, Case.Sensitive, path);
        html.ShouldNotContain("TechStrap", Case.Insensitive, path);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("True")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_true_or_a_blank_setting_shows_the_line(string setting)
    {
        await using var factory = Factory(setting);
        using var client = factory.CreateClient();

        (await client.GetStringAsync("/", Ct)).ShouldContain("ts-powered");
    }

    [Fact]
    public async Task The_setting_defaults_to_true_when_the_variable_is_not_set_at_all()
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();

        factory.Services.GetRequiredService<IOptions<PoweredByOptions>>().Value.Show.ShouldBeTrue();
        new PoweredByOptions().Show.ShouldBeTrue();
        (await client.GetStringAsync("/", Ct)).ShouldContain("ts-powered");
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("tru")]
    [InlineData("truee")]
    [InlineData("false false")]
    public async Task A_value_that_is_not_true_or_false_stops_the_start_and_names_the_key(string setting)
    {
        await using var factory = Factory(setting);

        var failure = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        failure.Message.ShouldContain("TECHSTRAP_PORTAL_SHOW_POWERED_BY");
        failure.Message.ShouldContain("true or false");
    }
}
```

- [ ] **Step 4: Write the failing docs tests**

The D-045 block of `RepositoryDocs.Tests.ps1` now ticks only the tasks 09a finishes, pins the roadmap and discovery rows, and requires the developer guide, its headings and settings, and its README link.

`scripts/tests/RepositoryDocs.Tests.ps1`

```diff
--- scripts/tests/RepositoryDocs.Tests.ps1
+++ scripts/tests/RepositoryDocs.Tests.ps1
@@ -155,14 +155,44 @@ Describe 'D-045 (the public portal)' {
         }
     }
 
-    It 'corrects the PHASE-09 spec, the package map and marks the roadmap and discovery rows as in progress (09a)' {
+    It 'corrects the PHASE-09 spec and the package map' {
         $spec = Get-RepoText 'docs/architecture/PHASE-09-public-portal.md'
         $spec | Should -Match '(?m)^### Corrections \(D-045, 2026-10-05\)'
         $spec | Should -Match 'MapSeoRobotsTxt'
         $spec | Should -Match 'ProductThemeViewModel'
         (Get-RepoText 'docs/architecture/03-PACKAGE-MAP.md') | Should -Match 'MapSeoRobotsTxt'
-        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 09 \|.*D-045.*\| In progress \(09a\)'
-        (Get-RepoText 'docs/architecture/00-DISCOVERY-INDEX.md') | Should -Match '(?m)^\| 09 \|.*\| In progress \(09a\)'
+    }
+
+    It 'ticks only the tasks and deliverables 09a fully delivers, and the roadmap and discovery rows say 09a is complete, pending merge' {
+        $spec = Get-RepoText 'docs/architecture/PHASE-09-public-portal.md'
+        foreach ($number in 1, 3, 5, 17, 19, 22) {
+            $id = 'P09-T{0:00}' -f $number
+            $spec | Should -Match ('(?m)^- \[x\] \*\*' + $id + '\*\*') -Because "$id is delivered by 09a"
+        }
+        foreach ($number in 2, 4, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 18, 20, 21, 23) {
+            $id = 'P09-T{0:00}' -f $number
+            $spec | Should -Match ('(?m)^- \[ \] \*\*' + $id + '\*\*') -Because "$id is not finished by 09a"
+        }
+        $spec | Should -Match '(?m)^- \[x\] `TechStrap\.Portal` host with `\.env\.example`, forwarded-headers and client-IP forwarding to the API\.'
+        $spec | Should -Match '(?m)^- \[x\] Branded layout with per-product theming and NotFound handling\.'
+        $spec | Should -Match '(?m)^- \[ \] Typed clients for public product, public ticket, customer ticket, public KB\.'
+        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 09 \|.*D-045.*\| 09a complete \(pending merge\)'
+        (Get-RepoText 'docs/architecture/00-DISCOVERY-INDEX.md') | Should -Match '(?m)^\| 09 \|.*\| 09a complete \(pending merge\)'
+    }
+
+    It 'has a Portal developer guide, linked from the README, that lists every setting and the known gaps' {
+        $guide = Get-RepoText 'docs/development/PORTAL-APP.md'
+        foreach ($heading in '## Run it locally', '### Configuration', '## How a page is served', '## Where things live', '## Tests', '## Known gaps in 09a') {
+            $guide | Should -Match ('(?m)^' + [regex]::Escape($heading))
+        }
+        foreach ($key in 'API__BASEURL', 'TECHSTRAP_PORTAL_PUBLIC_URL', 'TECHSTRAP_PORTAL_DEFAULT_PRODUCT', 'TECHSTRAP_PORTAL_SHOW_POWERED_BY', 'CANONICALHOST__CANONICALHOST') {
+            $guide | Should -Match ([regex]::Escape($key))
+        }
+        (Get-RepoText 'README.md') | Should -Match '\[Portal app\]\(docs/development/PORTAL-APP\.md\)'
+        $links = [regex]::Matches($guide, '\]\((?!http)(?<link>[^)#]+)') | ForEach-Object { $_.Groups['link'].Value }
+        foreach ($link in $links) {
+            Test-Path -LiteralPath (Join-Path $script:RepoRoot 'docs' 'development' $link) | Should -BeTrue -Because "PORTAL-APP.md links to $link"
+        }
     }
 }
 
```

- [ ] **Step 5: Run the tests to see them fail**

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/*/PiiRedactionQueryValueTests/*"`
Expected: FAIL: `total: 19, failed: 12`.

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/*/SensitiveQuerySentryProcessorTests/*"`
Expected: FAIL: `total: 53, failed: 11`.

Run: `dotnet test --project tests/TechStrap.Portal.Tests -c Release --filter-query "/*/*/RequestLogRedactionHostTests/*"`
Expected: FAIL: `total: 10, failed: 5` (the four `/t/{token}` cases already pass: the shared redactor masks a 43-character token; the `name` and `email` cases fail).

Run: `dotnet test --project tests/TechStrap.Architecture.Tests -c Release --filter-query "/*/*/PortalRuleTests/*"`
Expected: FAIL: `total: 36, failed: 1`: `No_Portal_file_has_an_inline_script_a_style_element_the_import_map_or_an_event_handler_attribute`, because `StyleGuide.razor` has `<form class="ts-sg-form" onsubmit="return false">` (a real finding: the CSP blocks an inline handler).

Run: `dotnet test --project tests/TechStrap.Portal.Tests -c Release --filter-query "/*/*/PoweredByHostTests/*"`
Expected: PASS: `total: 28, failed: 0` (test-only task: the behaviour exists).

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/RepositoryDocs.Tests.ps1 -Output Minimal`
Expected: FAIL: `Tests Passed: 23, Failed: 2` (the ticks test and the developer-guide test).

- [ ] **Step 6: Implement the redaction**

The redactor masks the value of a `name` or `email` query parameter before the email, token and hash patterns run, and fails closed like the others (a regex timeout becomes `[redaction-failed]`). The match is on the decoded parameter name, so `%6Eame=` and `NAME=` are caught while `username=` and a sentence that says `name=` are not. The Api and the Admin get the same masking; masking a `name=` value in their logs is accepted (over-redaction is acceptable, under-redaction is not). The Sentry processor adds the two names and a `/t/{43 characters}` rule.

`src/TechStrap.Hosting/Logging/PiiRedactionEnricher.cs`

```diff
--- src/TechStrap.Hosting/Logging/PiiRedactionEnricher.cs
+++ src/TechStrap.Hosting/Logging/PiiRedactionEnricher.cs
@@ -6,7 +6,8 @@ namespace TechStrap.Hosting.Logging;
 
 /// <summary>
 /// Rewrites PII-shaped text in every property value before any sink sees the event (D-039): email addresses (also URL-encoded), 43-character access tokens, JWT-shaped bearer tokens and
-/// "sha256:" hashes. It cannot touch LogEvent.Exception or the template, and it cannot recognise a name; application code never logs either
+/// "sha256:" hashes, and (D-045) the value of a <c>name</c> or <c>email</c> query parameter, which is how the Portal's contact page is prefilled and which a request log would otherwise carry.
+/// It cannot touch LogEvent.Exception or the template, and it cannot recognise a name by shape; application code never logs either
 /// (exceptions are logged by type name, requesters by id).
 /// Residual risk, accepted: names cannot be pattern-redacted, and an attached Exception is not rewritten. The worker loops that attach an
 /// exception (EmailOutboxWorker, AutoCloseWorker, OutboxRetentionWorker) get Npgsql's default, which hides PostgresException.Detail unless the error-detail connection option is enabled;
@@ -18,6 +19,7 @@ public sealed partial class PiiRedactionEnricher : ILogEventEnricher
     public const string EmailMarker = "[email]";
     public const string TokenMarker = "[token]";
     public const string HashMarker = "[hash]";
+    public const string QueryValueMarker = "[redacted]";
     public const string FailedMarker = "[redaction-failed]";
 
     private const RegexOptions Options = RegexOptions.CultureInvariant;
@@ -41,6 +43,10 @@ public sealed partial class PiiRedactionEnricher : ILogEventEnricher
         @"(?:(?<![A-Za-z0-9_\-])|(?<=%2[Ff]))(?:ts[kp]_)?[A-Za-z0-9_\-]{43}(?![A-Za-z0-9_\-])",
         Options, MatchTimeout);
 
+    // A "name=value" pair at the start of a text or after "?" or "&": the value runs up to the next "&", "#", a space or a quote. Whether the parameter is one of the personal ones is decided after decoding
+    // its name (see RedactQueryValues), so "%6Eame=" and "NAME=" are masked while "username", "filename" and a sentence that says "name=" are not.
+    private static readonly Regex QueryPairPattern = new(@"(?<=^|[?&])(?<name>[^=&#?\s""']+)=(?<value>[^&#\s""']*)", Options, MatchTimeout);
+
     private readonly Func<string, string> _redactText;
 
     public PiiRedactionEnricher()
@@ -72,7 +78,25 @@ public sealed partial class PiiRedactionEnricher : ILogEventEnricher
     }
 
     internal static string RedactText(string text) =>
-        TokenPattern.Replace(JwtPattern.Replace(EmailPattern.Replace(HashPattern.Replace(text, HashMarker), EmailMarker), TokenMarker), TokenMarker);
+        TokenPattern.Replace(JwtPattern.Replace(EmailPattern.Replace(HashPattern.Replace(RedactQueryValues(text), HashMarker), EmailMarker), TokenMarker), TokenMarker);
+
+    private static string RedactQueryValues(string text) =>
+        QueryPairPattern.Replace(text, match => IsPersonalQueryName(match.Groups["name"].Value) ? $"{match.Groups["name"].Value}={QueryValueMarker}" : match.Value);
+
+    private static bool IsPersonalQueryName(string name)
+    {
+        string decoded;
+        try
+        {
+            decoded = Uri.UnescapeDataString(name.Replace('+', ' '));
+        }
+        catch (UriFormatException)
+        {
+            decoded = name;
+        }
+
+        return decoded.Equals("name", StringComparison.OrdinalIgnoreCase) || decoded.Equals("email", StringComparison.OrdinalIgnoreCase);
+    }
 
     private LogEventPropertyValue Redact(LogEventPropertyValue value)
     {
```

`src/TechStrap.Hosting/Sentry/SensitiveQuerySentryProcessor.cs`

```diff
--- src/TechStrap.Hosting/Sentry/SensitiveQuerySentryProcessor.cs
+++ src/TechStrap.Hosting/Sentry/SensitiveQuerySentryProcessor.cs
@@ -1,6 +1,7 @@
 using System.Text.RegularExpressions;
 using Sentry;
 using Sentry.Extensibility;
+using TechStrap.Hosting.Logging;
 
 namespace TechStrap.Hosting.Sentry;
 
@@ -8,7 +9,9 @@ namespace TechStrap.Hosting.Sentry;
 /// Masks the text an agent searched for in what Sentry records. The queue keeps its search in the address (<c>/queue/mine?search=...</c>) so a view can be bookmarked, and the same text goes to the
 /// API as <c>GET /api/tickets?search=...</c>. A search is often a requester's email address or a subject line, so on an unhandled exception it must not reach Sentry in the request's query string
 /// or URL, in a breadcrumb, or in the description of a span. The value becomes <c>[redacted]</c> and the rest of the address is left alone, so the event still shows which page failed.
-/// The parameters are <c>search</c> (the queue and the ticket list) and <c>q</c> (a free-text query). Registered with the header scrubber by <see cref="SentryOptionsExtensions.AddSensitiveHeaderScrubbing"/>.
+/// The parameters are <c>search</c> (the queue and the ticket list) and <c>q</c> (a free-text query), and, for the Portal's contact page (D-045), <c>name</c> and <c>email</c> (the prefill, which a customer's own
+/// app puts in the address). The Portal's ticket address carries the access token in its path (<c>/t/{token}</c>), so a 43-character token directly under <c>/t/</c> is masked wherever an address appears.
+/// Registered with the header scrubber by <see cref="SentryOptionsExtensions.AddSensitiveHeaderScrubbing"/>.
 /// </summary>
 public sealed partial class SensitiveQuerySentryProcessor : ISentryEventProcessor, ISentryTransactionProcessor
 {
@@ -20,6 +23,10 @@ public sealed partial class SensitiveQuerySentryProcessor : ISentryEventProcesso
     [GeneratedRegex(@"(?<=^|[?&])(?<name>[^=&#?\s""']+)=(?<value>[^&#\s""']*)", RegexOptions.CultureInvariant)]
     private static partial Regex Parameter();
 
+    // The 43-character access token (base64url) right after "/t/" or "/T/": the Portal's ticket address and its attachment address.
+    [GeneratedRegex(@"(?<=/[tT]/)[A-Za-z0-9_\-]{43}(?![A-Za-z0-9_\-])", RegexOptions.CultureInvariant)]
+    private static partial Regex TicketPathToken();
+
     private static bool IsSensitiveName(string name)
     {
         string decoded;
@@ -32,15 +39,16 @@ public sealed partial class SensitiveQuerySentryProcessor : ISentryEventProcesso
             decoded = name;
         }
 
-        return decoded.Equals("search", StringComparison.OrdinalIgnoreCase) || decoded.Equals("q", StringComparison.OrdinalIgnoreCase);
+        return decoded.Equals("search", StringComparison.OrdinalIgnoreCase) || decoded.Equals("q", StringComparison.OrdinalIgnoreCase)
+            || decoded.Equals("name", StringComparison.OrdinalIgnoreCase) || decoded.Equals("email", StringComparison.OrdinalIgnoreCase);
     }
 
     // A non-sensitive value is looked into once more per level, but only this deep: "last=/queue/mine?search=x" needs one. The bound keeps hostile text such as "x=a=a=a=..." from
     // recursing once per pair (a stack overflow cannot be caught and would end the process).
     private const int MaxNesting = 2;
 
-    /// <summary>The text with the value of every sensitive query parameter masked. Null stays null.</summary>
-    public static string? Scrub(string? text) => Scrub(text, 0);
+    /// <summary>The text with the value of every sensitive query parameter masked and every ticket-path token replaced. Null stays null.</summary>
+    public static string? Scrub(string? text) => Scrub(string.IsNullOrEmpty(text) ? text : TicketPathToken().Replace(text, PiiRedactionEnricher.TokenMarker), 0);
 
     private static string? Scrub(string? text, int depth) =>
         string.IsNullOrEmpty(text)
```

- [ ] **Step 7: Fix the style guide's inline handler**

The Development-only style guide's demo form submits to itself instead of using an inline `onsubmit`.

`src/TechStrap.Portal/Components/Pages/StyleGuide.razor`

```diff
--- src/TechStrap.Portal/Components/Pages/StyleGuide.razor
+++ src/TechStrap.Portal/Components/Pages/StyleGuide.razor
@@ -36,7 +36,7 @@
 
         <section class="ts-sg-section" aria-labelledby="sg-forms">
             <h2 id="sg-forms">Forms</h2>
-            <form class="ts-sg-form" onsubmit="return false">
+            <form class="ts-sg-form" method="get" action="@PortalRoutes.StyleGuideTemplate">
                 <div class="mb-3"><label class="form-label" for="sg-name">Your name</label><input id="sg-name" class="form-control" autocomplete="name" /></div>
                 <div class="mb-3"><label class="form-label" for="sg-email">Email</label><input id="sg-email" class="form-control is-invalid" type="email" value="not-an-email" aria-describedby="sg-email-msg" /><div id="sg-email-msg" class="invalid-feedback">Enter an email address like name@example.com.</div></div>
                 <div class="mb-3"><label class="form-label" for="sg-msg">Message</label><textarea id="sg-msg" class="form-control" rows="4"></textarea></div>
```

- [ ] **Step 8: Write the developer guide and the closing docs**

`PORTAL-APP.md` is the Portal's counterpart to `ADMIN-APP.md`: kept short, with the settings table, how a page is served, where things live, the tests and the known gaps. The script (save it as `docs_task5.py` in `$T`, run from the repository root) links the guide from the README, sets the roadmap and discovery rows, ticks the tasks and deliverables 09a delivers with a one-line evidence note under each (and a note under T02 and T04, which stay open), and appends the "as built" consequences to D-045.

`docs/development/PORTAL-APP.md`

````markdown
# Portal app (customers)

`TechStrap.Portal` is the public site customers use: a product's own help and support pages, with the product's name, logo and accent. It never touches the database: every page asks the TechStrap API,
anonymously. This page is for people who run it and people who extend it. How agents work the tickets customers send is in [ADMIN-APP.md](ADMIN-APP.md); the decisions behind the Portal are D-045 in the
[decision log](../architecture/04-DECISION-LOG.md) and the spec is [PHASE-09](../architecture/PHASE-09-public-portal.md).

PHASE-09 is delivered in three pull requests. **09a** (this page describes it) is the foundation: the settings, the API client, the per-product theme and shell, the product home, the root page, the
ticket-page headers, robots.txt, log redaction and the architecture rules. **09b** adds the customer flows (contact form, ticket view and reply, lost link, attachments). **09c** adds the knowledge base
pages, the sitemap and the polish pass. The routes of all three are already in `PortalRoutes`.

## Run it locally

You need the API running (the root README starts it with Docker Compose, or run `src/TechStrap.Api`). With the Development seed (`TECHSTRAP_SEED_DEV_DATA=true`, see
[DEV-DATA.md](DEV-DATA.md)) the products `orbitly` and `paperplane` exist.

```bash
cp src/TechStrap.Portal/.env.example src/TechStrap.Portal/.env.local     # then edit the values below
dotnet run --project src/TechStrap.Portal --urls http://localhost:8082
```

`.env.local` is read in Development only and is git-ignored. Open `http://localhost:8082/p/paperplane` for the themed product home, `http://localhost:8082/p/nope` for the not-found page. With Docker Compose the
Portal listens on `http://127.0.0.1:8082` and compose sets `Api__BaseUrl` and `TECHSTRAP_PORTAL_PUBLIC_URL` for it.

### Configuration

The Portal refuses to start with a message that names the missing or malformed key. Every key is in `src/TechStrap.Portal/appsettings.json` with its default (D-043).

| Key | Required | Default | Meaning |
| --- | --- | --- | --- |
| `API__BASEURL` (`Api:BaseUrl`) | Yes | blank (the compose files set `http://api/`) | The address of the TechStrap API, absolute http or https |
| `TECHSTRAP_PORTAL_PUBLIC_URL` | Outside Development | blank | The Portal's public address as customers see it, absolute http or https, no query or fragment. The base of canonical URLs and robots.txt's sitemap line (`Seo:BaseUrl` is derived from it); the same value as the Api's key |
| `TECHSTRAP_PORTAL_DEFAULT_PRODUCT` | No | blank | A product key. When set, `/` redirects (302) to `/p/<key>`; blank shows a neutral page with no product list |
| `TECHSTRAP_PORTAL_SHOW_POWERED_BY` | No | `true` | `false` hides the "Powered by TechStrap" line on every page. Any other value than `true` or `false` stops the start (D-024) |
| `CANONICALHOST__CANONICALHOST` | No | blank | The host to redirect legacy hosts to; blank turns the redirect off |
| `CANONICALHOST__LEGACYHOSTS__0` ... | No | none | The hosts that are redirected. Only these are; any other host is left alone |
| `CANONICALHOST__FORCEHTTPS`, `CANONICALHOST__PERMANENT` | No | `false`, `true` | https target; 301 (true) or 302 |
| `DATAPROTECTION__KEYRINGPATH`, `TRUSTEDPROXY__*`, `SECURITYHEADERS__*`, `SENTRY__*`, `OPENTELEMETRY__*`, `SERILOG__*` | No | see `appsettings.json` | Shared with the other hosts |

## How a page is served

Every page is static server-side rendering: there is no render mode, no circuit and no SignalR (`PortalRules.InteractivityViolations` fails the build of the architecture tests if one appears).

- **The product scope.** A page under `/p/{key}` derives from `ProductPageBase`. It loads the product once through `IPublicProductClient` and puts the result in the request's `ProductScope`; `PortalLayout`
  then wraps the page in the product's accent (`AccentScope`), a header (logo and name) and a footer. The scope is per request, so the not-found and error pages, which the host renders in a fresh scope, are
  never branded.
- **One answer for "no such product".** An unknown key, an inactive product and a key that is not a slug (`^[a-z0-9]+(-[a-z0-9]+)*$`, at most 40 characters) all call `NavigationManager.NotFound()`, so the visitor
  gets the same neutral 404 page as for an unknown route; a malformed key never reaches the API. `NeutralPagesGuardTests` pins it.
- **When the API fails** the page shows "This page could not be loaded." with a Try again link and a 503 (429 when the API is rate limiting), never a stack trace.
- **Branding is untrusted.** `ProductThemeViewModel` keeps the accent only if `ProductAccent.TryDerive` accepts it and the logo only if it is https (or http to `localhost` or `127.0.0.1` in Development, the same
  rule as the CSP's `img-src`). The product name is always encoded.
- **Copy** lives in `ShellCopy` and `ProblemCopy`; routes in `PortalRoutes` (a page declares `@attribute [Route(PortalRoutes.XTemplate)]` and builds every link with a builder).

### Talking to the API

`ApiConnection` (internal, `Clients/`) is the only place the Portal uses HTTP. It sends reads through a client that retries transport errors, 408 and 502 to 504 (twice, honouring `Retry-After` up to
2 seconds, no circuit breaker) and writes through a client that never retries. Both forward the visitor's address in `X-Forwarded-For` (`AddForwardedClientIp`; the API trusts it only from the compose subnet,
D-019) and have no logging handlers. `ProblemMapping` turns every answer into a `Result`: 400 keeps the API's field codes, 404 is one not-found whatever the API called it, 413 and 415 are the attachment
errors, 429 is rate limited, any 5xx or transport error is `api-unavailable`, with fixed sentences from `ProblemCopy`. A call made as a ticket's customer takes a `TicketToken` (43 base64url characters; it
prints as `[token]`), which becomes the `X-Ticket-Token` header of that request only. The ticket and KB clients arrive with their pages in 09b and 09c.

### Headers, robots.txt and the canonical host

`UseTechStrapWebHost(PortalHeaderRules.Rules)` applies per-path rules after the shared security headers: everything under `/t` gets `Referrer-Policy: no-referrer`, `Cache-Control: no-store` and
`X-Robots-Tag: noindex`, and only `/t/{token}/attachments/{id}` is sandboxed (so the ticket page keeps the normal CSP). `/robots.txt` disallows `/t/` and names `{public url}/sitemap.xml`, which answers 404
until 09c. The canonical-host redirect is an allow-list of legacy hosts and does nothing until configured.

### Logs and Sentry

Request paths and queries are logged by the framework at Information. The PII enricher masks the access token in a `/t/{token}` address and the value of a `name` or `email` query parameter (the contact
page prefill, 09b), and the Sentry processors mask the same in URLs, headers, breadcrumbs and spans. `RequestLogRedactionHostTests` and `TicketTokenLeakTests` scan every level at Verbose.

## Where things live

```text
src/TechStrap.Portal/
  Clients/        ApiConnection, ProblemMapping and ProblemCopy, TicketToken, ApiClientRegistration (the two named clients), IPublicProductClient
  Components/
    Layout/       PortalLayout, ProductHeader, ProductFooter
    Pages/        Home (the root), ProductHome, NotFound, Error, StyleGuide (Development only)
    Ui/           AccentScope, PoweredByFooter, ProductUnavailable, DevelopmentOnly
  Headers/        PortalHeaderRules (the /t rules)
  Products/       ProductThemeViewModel, ProductScope, ProductPageBase
  Routing/        PortalRoutes, ProductKeyShape
  Seo/            PortalSeoRegistration (Blazor.Seo: base URL, robots.txt, canonical host)
  Settings/       PortalOptions and its validator
  Styles/         SCSS partials over the brand tokens (docs/BRAND.md)
```

Rules the code follows (the architecture tests check them): the Portal references **Contracts and Hosting only**; **components never inject `HttpClient`** (only `Clients/` mentions it); no inline script
or style; no interactive render mode; `MarkupString` is used nowhere yet (09b adds `CustomerMessageBody` and 09c `KbArticleBody`, each argued for in its commit); every plain-text DTO field is encoded.

## Tests

`tests/TechStrap.Portal.Tests` runs the host in memory with a stub API behind its two named clients (`PortalFactory.Api`, `StubApiHandler`), so a test sees what the API would see, including the headers the
Portal added. `ProxyHopStartupFilter` puts the host behind a trusted reverse proxy; `OkProbeStartupFilter` answers 200 on a path no Portal route matches. bUnit covers the layout. The shared rules are in
`TechStrap.Architecture.Tests` (`PortalRules`), the Hosting header-rule mechanism in `TechStrap.Api.Tests` (`PathHeaderRuleHostTests`), and the log and Sentry redaction in `TechStrap.Api.Tests`.

## Known gaps in 09a

- The product home links to pages that arrive later: Contact support (`/p/{key}/contact`, 09b), the footer's "Lost your ticket link?" (`/p/{key}/lost-link`, 09b) and the search box (`/p/{key}/kb/search`, 09c) answer 404 until then.
- `/robots.txt` names `/sitemap.xml`, which answers 404 until 09c maps it.
- A legacy-host redirect decodes percent-escapes in the query string (the package builds the target with `Uri.ToString()`), so a value that holds an encoded `&` or `#` changes meaning. It affects only hosts
  in `CANONICALHOST__LEGACYHOSTS`; report it upstream before using the redirect with the contact prefill.
- `NavigationManager.NotFound()` adds the framework's `blazor-enhanced-nav: allow` response header to an unknown product's 404, which the router's own unknown-route 404 does not carry. The bodies are identical,
  and both are 404, so it reveals nothing about which products exist.
- The Portal does not use `GlobalErrorBoundary`: its Try again button needs interactivity, and catching a render error in the page would answer 200 with a branded page instead of the plain 500 error page.
- A category named `suggest` would be unreachable once 09b serves `/p/{key}/kb/suggest`; 09b decides whether to reserve the slug.
````

`docs_task5.py`

```python
"""Task 5 closing docs for PHASE-09a. Run from the repository root: python docs_task5.py"""


def read(path):
    return open(path, encoding="utf-8", newline="").read()


def write(path, text):
    open(path, "w", encoding="utf-8", newline="").write(text)


def newline_of(text):
    return "\r\n" if "\r\n" in text else "\n"


def sub(path, old, new):
    text = read(path)
    nl = newline_of(text)
    old, new = old.replace("\n", nl), new.replace("\n", nl)
    assert text.count(old) == 1, (path, text.count(old), old[:70])
    write(path, text.replace(old, new))


# 1. README: one row in the documents table.
sub(
    "README.md",
    "| [Admin app](docs/development/ADMIN-APP.md) | The agent app: configuration, sign-in, Authentik setup, shortcuts, known limits |\n",
    "| [Admin app](docs/development/ADMIN-APP.md) | The agent app: configuration, sign-in, Authentik setup, shortcuts, known limits |\n"
    "| [Portal app](docs/development/PORTAL-APP.md) | The customer portal: configuration, how a page is served, the API client, headers, known gaps |\n",
)

# 2. Roadmap and discovery rows.
sub("docs/architecture/99-IMPLEMENTATION-ROADMAP.md", "| D-002, D-017, D-019, D-045 | In progress (09a) |", "| D-002, D-017, D-019, D-045 | 09a complete (pending merge); 09b and 09c not started |")
sub("docs/architecture/00-DISCOVERY-INDEX.md", "| In progress (09a) |", "| 09a complete (pending merge); 09b and 09c not started |")

# 3. The spec: tick what 09a fully delivers, with evidence, and note what it delivers of T02 and T04.
spec = "docs/architecture/PHASE-09-public-portal.md"
text = read(spec)
nl = newline_of(text)
lines = text.split(nl)
evidence = {
    "P09-T01": "  - **09a evidence:** `PortalOptionsValidatorTests` and `PortalOptionsHostTests` (the start fails naming the key), `PortalRoutesTests` and `RouteLiteralTests` (every route once, no inline route string), `ConfigContract.Tests.ps1` and `ProductionBlankTemplateTests` (the keys in every template). The keys are `API__BASEURL`, `TECHSTRAP_PORTAL_PUBLIC_URL` and `TECHSTRAP_PORTAL_DEFAULT_PRODUCT`; `Seo:BaseUrl` is derived from the public URL (D-045).",
    "P09-T02": "  - **09a:** done: `ApiConnection`, `ProblemMapping`, the token capability and `IPublicProductClient` (`ApiConnectionTests`, `ProblemMappingTests`, `PublicProductClientTests`, `ForwardedClientIpHostTests`, `TicketTokenLeakTests`), with the fake-API harness. The ticket and KB clients arrive with 09b and 09c, so this task stays open.",
    "P09-T03": "  - **09a evidence:** `ProductThemeViewModelTests`, `PortalLayoutTests`, `NeutralPagesGuardTests`, `ProductHomeHostTests`. There is no `BrandingThemeFactory` (D-045); the contrast theory is `ProductAccentContrastTests`.",
    "P09-T04": "  - **09a:** done: Seo wiring, `/robots.txt`, the canonical host and the per-path headers (`SeoHostTests`, `TicketHeaderHostTests`, `PathHeaderRuleHostTests`). The sitemap arrives with 09c, so this task stays open.",
    "P09-T05": "  - **09a evidence:** `ProductHomeHostTests`, `RootPageHostTests`, `NotFoundHostTests`. `GlobalErrorBoundary` is not used: its retry button needs interactivity and it would turn the plain 500 page into a branded 200 (D-045).",
    "P09-T17": "  - **09a evidence:** `RequestLogRedactionHostTests` and `TicketTokenLeakTests` (every level at Verbose), `PiiRedactionQueryValueTests` and `SensitiveQuerySentryProcessorTests` (the `name` and `email` query values and the `/t/{token}` path).",
    "P09-T19": "  - **09a evidence:** `PortalRuleTests` (the package allow-list, HttpClient only in `Clients/`, no inline script or style, no interactive render mode, `MarkupString` sites: none yet, each with a deliberate-violation sample).",
    "P09-T22": "  - **09a evidence:** `PoweredByHostTests` (exactly one link on every page type, none when false, true by default, an invalid value fails the start) and `PoweredByFooterTests`.",
}
ticked = {"P09-T01", "P09-T03", "P09-T05", "P09-T17", "P09-T19", "P09-T22"}
for task, line in evidence.items():
    start = next(i for i, l in enumerate(lines) if l.startswith(f"- [ ] **{task}**"))
    if task in ticked:
        lines[start] = lines[start].replace("- [ ]", "- [x]", 1)
    end = next(i for i in range(start + 1, len(lines)) if lines[i].startswith("  - **Validation:**"))
    lines.insert(end + 1, line)
text = nl.join(lines)
text = text.replace("- [ ] `TechStrap.Portal` host with `.env.example`, forwarded-headers and client-IP forwarding to the API.", "- [x] `TechStrap.Portal` host with `.env.example`, forwarded-headers and client-IP forwarding to the API.")
text = text.replace("- [ ] Branded layout with per-product theming and NotFound handling.", "- [x] Branded layout with per-product theming and NotFound handling.")
write(spec, text)

# 4. D-045: what 09a built, and what it knowingly leaves.
log = "docs/architecture/04-DECISION-LOG.md"
text = read(log)
nl = newline_of(text)
marker = nl + "### Approval" + nl
at = text.rindex(marker)
assert text.rindex("## D-045:") < at
addition = """- **As built in 09a: log and Sentry redaction.** The shared PII redactor masks the value of a `name` or `email` query parameter (the contact page prefill) in every host's logs, and the Sentry processors mask the same, plus a 43-character token directly under `/t/` in any address they scrub. The Api and the Admin get this too; masking a `name=` value in their logs is accepted.
- **As built in 09a: `GlobalErrorBoundary` is not used.** Its retry button needs interactivity, and catching a render error in the page would answer 200 with a branded page instead of the plain 500 error page.
- **Known: a legacy-host redirect decodes percent-escapes.** `UseSyntaxCircusSeo` builds the target with `Uri.ToString()`, so an encoded `&` or `#` in a query value changes meaning. It affects only hosts listed in `CANONICALHOST__LEGACYHOSTS`; it is to be reported upstream.
- **Known: the product pages link ahead.** Contact support and the footer's lost-link link (09b) and the search box (09c) answer 404 until their pages exist.
""".replace("\n", nl)
write(log, text[:at] + addition + text[at:])
```

Run: `python $T/docs_task5.py`
Expected: no output.

- [ ] **Step 9: Run the tests to see them pass**

Run: `dotnet build TechStrap.slnx -c Release --no-incremental`
Expected: `0 Warning(s)`, `0 Error(s)`.

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release`
Expected: PASS: `total: 878, failed: 0` (843 before this task: `PiiRedactionQueryValueTests` 19 and 16 more `SensitiveQuerySentryProcessorTests`).

Run: `dotnet test --project tests/TechStrap.Portal.Tests -c Release`
Expected: PASS: `total: 430, failed: 0` (392 before this task: `RequestLogRedactionHostTests` 10 and `PoweredByHostTests` 28).

Run: `dotnet test --project tests/TechStrap.Architecture.Tests -c Release`
Expected: PASS: `total: 289, failed: 0` (253 before this task, plus `PortalRuleTests` 36).

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal`
Expected: PASS: `Tests Passed: 290, Failed: 0`.

- [ ] **Step 10: Prove each pin with a mutation**

Run `git add -A` first, and run each row from the repository root with `python $T/mut.py <file> --replace "<old>" "<new>" -- <command>`. Commands: **PT** = `dotnet test --project tests/TechStrap.Portal.Tests -c Release --filter-query "/*/*/<Class>/*"`, **AT** = `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-query "/*/*/<Class>/*"`, **XT** = `dotnet test --project tests/TechStrap.Architecture.Tests -c Release --filter-query "/*/*/PortalRuleTests/*"`, **PS** = `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/RepositoryDocs.Tests.ps1 -Output Minimal`.

| # | File | Replace | With | Command and class | Expected |
| --- | --- | --- | --- | --- | --- |
| R1 | `src/TechStrap.Hosting/Logging/PiiRedactionEnricher.cs` | `HashPattern.Replace(RedactQueryValues(text), HashMarker)` | `HashPattern.Replace(text, HashMarker)` | PT `RequestLogRedactionHostTests` | KILLED, 5 failed of 10 |
| R1b | the same | `TokenPattern.Replace(JwtPattern.Replace(EmailPattern.Replace(HashPattern.Replace(RedactQueryValues(text), HashMarker), EmailMarker), TokenMarker), TokenMarker);` | `JwtPattern.Replace(EmailPattern.Replace(HashPattern.Replace(RedactQueryValues(text), HashMarker), EmailMarker), TokenMarker);` | PT `RequestLogRedactionHostTests` | KILLED, 4 failed (the token must be masked in the request line) |
| R2 | the same | `decoded.Equals("name", StringComparison.OrdinalIgnoreCase) \|\| decoded.Equals("email", StringComparison.OrdinalIgnoreCase);` | `decoded.Equals("name", StringComparison.OrdinalIgnoreCase);` | AT `PiiRedactionQueryValueTests` | KILLED, 5 failed of 19 |
| R3 | `src/TechStrap.Hosting/Sentry/SensitiveQuerySentryProcessor.cs` | `Scrub(string.IsNullOrEmpty(text) ? text : TicketPathToken().Replace(text, PiiRedactionEnricher.TokenMarker), 0)` | `Scrub(text, 0)` | AT `SensitiveQuerySentryProcessorTests` | KILLED, 6 failed of 53 |
| R4 | the same | `\n            \|\| decoded.Equals("name", StringComparison.OrdinalIgnoreCase) \|\| decoded.Equals("email", StringComparison.OrdinalIgnoreCase);` | `;` | AT `SensitiveQuerySentryProcessorTests` | KILLED, 7 failed |
| A1 | `tests/TechStrap.Architecture.Tests/PortalRules.cs` | `        "SyntaxCircus.Blazor.Seo",\n` | `` | XT | KILLED, 2 failed of 36 |
| A2 | `src/TechStrap.Portal/Components/ShellCopy.cs` | `public const string RootTitle = "Support";` | `public const string RootTitle = "Support"; // MarkupString` | XT | KILLED, 1 failed |
| A3 | `src/TechStrap.Portal/Components/Pages/NotFound.razor` | `@layout PortalLayout` | `@layout PortalLayout\n@rendermode InteractiveServer` | XT | KILLED, 1 failed |
| A4 | `src/TechStrap.Portal/Components/Pages/Home.razor` | `<PageTitle>` | `@inject HttpClient Http\n<PageTitle>` | XT | KILLED, 1 failed |
| A5 | `src/TechStrap.Portal/Components/Pages/ProductHome.razor` | `<h1>@ShellCopy.HomeHeading</h1>` | `<style>.x{}</style><h1>@ShellCopy.HomeHeading</h1>` | XT | KILLED, 1 failed |
| A6 | `src/TechStrap.Portal/TechStrap.Portal.csproj` | `<PackageReference Include="SyntaxCircus.Common" />` | `<PackageReference Include="SyntaxCircus.Common" />\n    <PackageReference Include="Newtonsoft.Json" />` | XT | KILLED, 1 failed |
| W1 | `src/TechStrap.Portal/Components/Layout/PortalLayout.razor` | `        <PoweredByFooter />\n` | `` | PT `PoweredByHostTests` | KILLED, 13 failed of 28 |
| W2 | `src/TechStrap.Portal/Components/Layout/ProductFooter.razor` | `<footer class="ts-product-footer">` | `<footer class="ts-product-footer"><small>TechStrap</small>` | PT `PoweredByHostTests` | KILLED, 2 failed (no other TechStrap text) |
| W3 | `src/TechStrap.Portal/Program.cs` | `options.Show = !bool.TryParse(configuration[PoweredByOptions.ConfigurationKey], out var show) \|\| show)` | `options.Show = bool.TryParse(configuration[PoweredByOptions.ConfigurationKey], out var show) && show)` | PT `PoweredByHostTests` | KILLED, 2 failed (it defaults to true) |
| W4 | the same | `    .ValidateOnStart();\n` | `    ;\n` | PT `PoweredByHostTests` | KILLED, 8 failed (an invalid value fails the start) |
| D1 | `docs/architecture/PHASE-09-public-portal.md` | `- [x] **P09-T05**` | `- [ ] **P09-T05**` | PS | KILLED, 1 failed |
| D2 | the same | `- [ ] **P09-T02**` | `- [x] **P09-T02**` | PS | KILLED, 1 failed (T02 stays open until 09b and 09c) |
| D3 | `README.md` | `\| [Portal app](docs/development/PORTAL-APP.md)` | `\| [Portal](docs/development/PORTAL-APP.md)` | PS | KILLED, 1 failed |
| D4 | `docs/development/PORTAL-APP.md` | `## Known gaps in 09a` | `## Gaps` | PS | KILLED, 1 failed |

In a table cell a backslash before a pipe stands for the plain pipe character; pass the plain character to the tool. Row R4 starts its old text with a newline (`\n` in the argument).

- [ ] **Step 11: Final verification**

```bash
dotnet build TechStrap.slnx -c Release --no-incremental
dotnet test --solution TechStrap.CI.slnf -c Release
pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal
```

Expected: `0 Warning(s)`; every test project passes (`total: 5389, failed: 0` across the solution filter: Domain, Application, Architecture, Portal, Admin, Infrastructure integration and Api); Pester `Tests Passed: 290, Failed: 0`. The Infrastructure integration and Api tests need Docker (Testcontainers) running.

Check the encoding rule: `git diff main --name-only | xargs grep -nP '[^\x00-\x7F]'` must show nothing from files this phase added (the older docs already hold a few typographic characters, all pre-existing).

- [ ] **Step 12: Commit**

```bash
git add -A src tests scripts docs README.md
git diff --cached --stat
git commit -F - <<'EOF'
feat(portal): log and Sentry redaction, architecture rules, Powered-by tests and docs (PHASE-09a)

The shared PII redactor and the Sentry processors mask the name and email query values and the
/t/{token} path. Adds PortalRules (packages, HttpClient only in Clients, no inline script or style,
no interactive render mode, MarkupString sites: none yet), host tests for the Powered-by line on
every page type, the Portal developer guide, the PHASE-09 ticks and the D-045 as-built notes.
Removes an inline onsubmit handler from the Development style guide.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
EOF
```

The branch now holds five commits. Do not open the pull request: the owner reviews first (the controller's final review, then the owner's manual check: the Portal under compose shows a themed product home).
