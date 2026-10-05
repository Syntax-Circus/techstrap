# PHASE-07c Admin Polish, Security Headers and Host Wiring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish PHASE-07. That covers:
- **Rules and wiring:** the Admin architecture rules; security headers and a strict CSP for Admin and Portal; shared host wiring through TechStrap.Hosting.
- **Resilience:** session resilience, so a 401 mid-session keeps drafts.
- **Agent experience:** the theme applied before first paint, local-time display, a Ctrl+K command palette and menu accessibility, and the responsive and brand pass.
- **Scrubbing and docs:** Sentry search scrubbing, OpenAPI security schemes, the 07b follow-ups, a compose smoke check and the closing docs.

**Architecture:**
- **Shared wiring.** TechStrap.Hosting gains these extensions, adopted by Admin and Portal:
  - the security-header and CSP middleware;
  - the `HttpClient` logging default, which all four hosts use;
  - the Sentry search scrub.
- **Session.** The Admin reports any 401 to `AgentSession` from one choke point in `ApiConnection`.
- **Browser-side features.** These are small ES modules (`tz.js`) or classic scripts (`theme-init.js`) under `wwwroot/js`, all served from `'self'`.
- **Command palette.** It reuses the dialog and shortcut layers from 07a and 07b.

**Tech Stack:** .NET 10, Blazor Server, ASP.NET Core, SyntaxCircus.Http.Resilience, Sentry, OpenTelemetry, xUnit v3, Shouldly, NSubstitute, bUnit 2.11.3, node:test and Pester.

**Spec:**
- `docs/architecture/PHASE-07-admin-app.md`: T19, T20 and the 07c carry-forwards.
- `docs/architecture/UX-BRIEF-admin.md`.
- D-040 and D-041.
- The owner decisions of 2026-10-04, recorded as D-042 in Task 10.

### Owner decisions (2026-10-04), recorded as D-042 in Task 10
- **CSP.** `script-src 'self'` stays strict. Styles use `style-src 'self'` plus `style-src-attr 'unsafe-inline'`, so the tag and accent colours work without JS and on the first server render.
- **Theme flash.** It is fixed by an external blocking `wwwroot/js/theme-init.js` in `<head>`.
- **Time zone.** It comes from the browser (`Intl`), per circuit. The first server render shows UTC. There is no API change.
- **Host wiring.**
  - A shared Hosting helper is adopted by Admin and Portal, and the Portal gains the Hosting reference.
  - The no-`HttpClient`-logging default applies in all four hosts.
- **One PR.**

### Technical decisions this plan makes (D-042; the owner confirms at plan review)
- **Queue search** stays in the URL. It is scrubbed from the Sentry QueryString, Url and breadcrumbs.
- **`Retry-After`** is honoured, capped at 2 s.
- **OpenAPI** documents Bearer (agents), ApiKey (products) and TicketToken (customers).
- **A 401 mid-session** sets `AgentSession` to SessionExpired. A banner offers "Sign in again", and the page stays mounted so drafts survive. A 401 on the first load still shows the full session-expired page.
  - Every 401 goes through one choke point: `ApiConnection` calls `SessionExpiry.Report()`.
  - The session keeps `Agent`. `IsAdmitted` and `IsAdmin` stay true while it is expired, so the pages stay mounted.
  - A 401 during a reload also keeps the agent.
- **CSP, as built.**
  - Admin and Portal:
    - `default-src 'self'; script-src 'self'; style-src 'self'; style-src-attr 'unsafe-inline'`;
    - `img-src 'self' https: data:`. Bootstrap's compiled icons use `data:` SVG. Loopback `http` is added in Development only;
    - `connect-src 'self'; font-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'`;
    - `form-action 'self'` plus the OIDC authority origin.
  - Api: `default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'`.
  - Attachment downloads also get `sandbox`.
  - `upgrade-insecure-requests` is dropped, because it would rewrite Development loopback logos.
  - `<ImportMap />` is removed. A source rule keeps it out.
  - The owner's browser check confirms that `connect-src 'self'` covers the circuit websocket.
- **Host wiring, as built.**
  - `TechStrap.Hosting.Wiring` provides:
    - `AddTechStrapHttpClientDefaults`, used in all four hosts;
    - `AddTechStrapWebHost`, `UseTechStrapWebHost` and `UseTechStrapErrorPages`, used by Admin and Portal;
    - `AddTechStrapObservability`, used by Portal.
  - Hosting now depends on `SyntaxCircus.AspNetCore.Common`.
  - `FactoryClientLeakTests` sends a secret header through the real factory in all four hosts.
  - The 07b assumption that the OTLP exporter uses `IHttpClientFactory` was not reproduced. The OTLP leak tests stay as an end-to-end guard, with a positive control: a local listener must see the exporter connect.
- **Time zone data.** `Dockerfile.admin` installs `tzdata`, so IANA zone ids resolve in the container.
- **Command palette.** It adds `palette.js` and `menu.js`, because Blazor cannot cancel arrow keys per keypress. It also adds `ShortcutAction.AssignToMe`, `ShortcutService.RaiseAsync` and a `RailLink` component that emits `aria-current`.
- **Compose smoke.** It uses its own project name and ports, builds images one at a time and never runs `down -v`. CI runs it only on manual dispatch.
- **Product update.** It looks the product up before validating, so an unchanged stored relative logo can still be saved. The side effect: an unknown product id with an invalid body now returns 404 instead of 400.
- **Tasks 6-10 were written against main.** Where Tasks 1-5 changed the same file, apply the intent to the current file. Task 6 lists the shared files.

## Global Constraints

- **Build.** .NET SDK 10.0.401 targeting `net10.0`, with `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild`.
  - Private fields are `_camelCase`.
  - Constants are PascalCase.
  - Namespaces are file-scoped.
- **Packages.** Versions are managed centrally. This plan adds no package unless a task says so explicitly.
- **Project references.**
  - Admin and Portal reference only Contracts and Hosting.
  - Hosting references no TechStrap project.
  - Domain and Contracts reference nothing.
- **Admin code.**
  - Components never inject `HttpClient`.
  - Copy lives in `*Copy` constants.
  - Use paired `.razor` and `.razor.cs` files beyond the inline ceiling.
  - JS lives only in files under `wwwroot/js`. No inline `<script>` or `<style>`.
  - CSS lives in `Styles/_*.scss` partials with `ts-` classes.
- **Writes.**
  - Use the `ApiConnection` write methods, and never retry.
  - Pass `CancellationToken.None`, and add `_disposed` guards.
  - Classify outcomes with `WriteOutcomes.Classify` / `ApiErrorCodes.IsUncertainWrite`.
  - An uncertain outcome is held until a reload.
- **Loads.** Every list load uses a `_loadId` stale guard.
- **Encoding.**
  - Write non-ASCII in C# as `\u` escapes, written with Python or .NET, never GNU sed.
  - The Write tool decodes `\uXXXX`, so grep for non-ASCII afterwards.
  - `SourceEncodingTests` and `SourceEscapeTests` must pass.
- **Secrets and PII.** Never log a token, cookie, API-key plaintext, email, requester name or search text.
- **Tests.**
  - Write the failing test first, and record RED and GREEN.
  - Prove each pin non-vacuous with a recorded mutation.
  - Every signed-in host test calls `AssertEveryCallBore`.
  - Leak tests run at Verbose and assert that Debug events were captured.
  - A test that sets process environment variables runs in `ProcessEnvironmentCollection`.
- **Persistence.** No migration is expected. If one is needed, generate it with `dotnet ef` only.
- **Commits.** Use Conventional Commits, each ending with exactly:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
  ```
- **Forbidden.**
  - `git add -f`.
  - Committing `.superpowers/`.
  - `docker compose down -v`.
  - Killing processes you did not start.
  - Committing without running `git diff --cached --stat` first.
- **Verification.**
  - `dotnet build TechStrap.slnx -c Release` gives 0 warnings.
  - `dotnet test --solution TechStrap.CI.slnf -c Release` passes.
  - `pwsh -File scripts/Invoke-ScriptTests.ps1` passes.
  - The EF pending-model check is clean.

## Review Focus

1. **A CSP that breaks the app or sign-in in a real browser, or that is weaker than decided.** Watch the Blazor websocket, `blazor.web.js`, the import map, the OIDC `form-action` redirect and the colour styles. Pinned in Task 4 by the host header tests, plus the owner's Chrome console check.
2. **A 401 mid-session that loses an agent's draft, or leaves the app showing data as if signed in.** Pinned in Task 2.
3. **The command palette exposing admin commands to a plain agent, or firing a command twice.** Pinned in Task 7.
4. **A secret leaking through `HttpClient` logging in any host, or search text through Sentry.** Pinned in Tasks 3 and 9.
5. **Local time showing the wrong instant**, through DST, an unknown zone, or drift between the server render and the live page. Pinned in Task 6.

---

### Task 1: Admin architecture rules (P07-T20, first half)

**Review Focus pin:**
- **(1)** and the Admin gate. A page that carries `[ExcludeFromInteractiveRouting]` renders with no circuit and no `GET /api/agents/me`, so `AgentGate` skips the NoAccess check for it. The allowlist (Error, NotFound, StyleGuide, each `[AllowAnonymous]`) is pinned by `AdminRuleTests.Only_the_allowed_pages_are_excluded_from_interactive_routing_and_each_is_anonymous`.
- The inline `<script>` and `<style>` rule is the static half of the CSP in Task 4: the policy forbids them, so a `.razor` file must never add one. Pinned by `AdminRuleTests.No_razor_file_has_an_inline_script_or_a_style_element`.

**Files:**
- Create: `tests/TechStrap.Architecture.Tests/AdminRules.cs`
- Create: `tests/TechStrap.Architecture.Tests/AdminRuleTests.cs`

**Interfaces:**
- Consumes: `ProjectGraph.FindRepositoryRoot`, `ProjectGraph.LoadSourceProjects`, `ProjectNode` and `ReferenceRules.Admin` (all in `TechStrap.Architecture.Tests`). The project reference direction (Admin references only Contracts and Hosting) is already pinned by `ProjectReferenceDirectionTests`; this task adds what it does not cover: packages, `HttpClient`, the static-page allowlist and inline markup. The Architecture test project does not reference the Admin project, so every rule scans text.
- Produces (names are fixed; Task 3 and Task 4 add rows to the same lists when they need to):

```csharp
namespace TechStrap.Architecture.Tests;

public static partial class AdminRules
{
    public static IReadOnlySet<string> AllowedPackages { get; }   // the 12 packages the Admin csproj references today
    public static IReadOnlySet<string> StaticPages { get; }       // "Error", "NotFound", "StyleGuide"
    public static IReadOnlyList<string> PackageViolations(ProjectNode admin);
    public static IReadOnlyList<string> HttpClientViolations(IEnumerable<(string Path, string Text)> files);
    public static IReadOnlyList<string> InlineMarkupViolations(IEnumerable<(string Path, string Text)> files);
    public static IReadOnlyList<string> StaticPageViolations(IEnumerable<(string Path, string Text)> files);
    public static IEnumerable<(string Path, string Text)> ComponentSources(string repositoryRoot);   // every .razor and .razor.cs under src/TechStrap.Admin
}
```

**Rules:**
1. **Evaluate, do not assert, inside the rule.** Each rule returns a list of violation texts, like `ReferenceRules.Evaluate`, so a test can feed it a deliberately bad sample and prove it fails. Every rule has a "bad sample" test next to its "real tree" test.
2. **Every real-tree test also proves the scan sees the tree** (it asserts a file or package it must find), so an empty scan cannot pass.
3. **Comments are not hits.** The attribute rules need `@attribute [` or a line that starts with `[`, so a `// Pages marked [ExcludeFromInteractiveRouting]` comment (App.razor has one) is not a page.
4. **A script with a `src` is allowed.** `App.razor` loads `blazor.web.js` that way. `<ImportMap />` is a component, not a `<script>` tag, so this rule does not see it yet (the Admin's `App.razor` still has it). Task 4 removes it from both apps and extends this rule to flag it.
5. **`HttpClient` is allowed in `Clients/` and `Program.cs`** because the rule reads only `.razor` and `.razor.cs`.

- [ ] **Step 1: Write the failing tests**

Create `tests/TechStrap.Architecture.Tests/AdminRuleTests.cs`:

```csharp
namespace TechStrap.Architecture.Tests;

public sealed class AdminRuleTests
{
    private static ProjectNode AdminWith(params string[] packages) =>
        new(ReferenceRules.Admin, new HashSet<string>(), packages.ToHashSet(), new HashSet<string>());

    [Fact]
    public void The_Admin_project_references_only_allowed_packages()
    {
        var admin = ProjectGraph.LoadSourceProjects(ProjectGraph.FindRepositoryRoot())[ReferenceRules.Admin];

        admin.PackageReferences.ShouldContain("SyntaxCircus.Blazor.Auth", "the scan must read the real Admin project");
        AdminRules.PackageViolations(admin).ShouldBeEmpty();
    }

    [Fact]
    public void The_rule_flags_EF_Npgsql_and_any_unlisted_package()
    {
        var violations = AdminRules.PackageViolations(AdminWith("SyntaxCircus.Common", "Microsoft.EntityFrameworkCore", "Npgsql", "Newtonsoft.Json"));

        violations.Count.ShouldBe(3);
        violations.ShouldContain(v => v.Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        violations.ShouldContain(v => v.Contains("Npgsql", StringComparison.Ordinal));
        violations.ShouldContain(v => v.Contains("Newtonsoft.Json", StringComparison.Ordinal));
    }

    [Fact]
    public void No_component_uses_HttpClient()
    {
        var files = AdminRules.ComponentSources(ProjectGraph.FindRepositoryRoot()).ToList();

        files.ShouldContain(f => f.Path.EndsWith("App.razor", StringComparison.Ordinal), "the scan must see the Admin components");
        files.ShouldContain(f => f.Path.EndsWith(".razor.cs", StringComparison.Ordinal), "the scan must see the code-behind files");
        AdminRules.HttpClientViolations(files).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Page.razor", "@inject HttpClient Http")]
    [InlineData("Page.razor", "@inject IHttpClientFactory Factory")]
    [InlineData("Page.razor.cs", "[Inject] public HttpClient Http { get; set; } = default!;")]
    [InlineData("Page.razor.cs", "public Page(HttpClient http) { }")]
    public void A_component_that_uses_HttpClient_is_flagged(string path, string text)
    {
        AdminRules.HttpClientViolations([(path, text)]).Count.ShouldBe(1);
    }

    [Fact]
    public void HttpClient_outside_a_component_is_not_flagged_and_a_look_alike_name_is_not_a_hit()
    {
        AdminRules.HttpClientViolations([("Clients/ApiConnection.cs", "HttpClient http"), ("Page.razor", "@inject IBlazorCircuitHttpClientFactory Factory")])
            .ShouldBeEmpty("Clients/ may use HttpClient; a different type whose name ends the same is not the rule's target");
    }

    [Fact]
    public void Only_the_allowed_pages_are_excluded_from_interactive_routing_and_each_is_anonymous()
    {
        var files = AdminRules.ComponentSources(ProjectGraph.FindRepositoryRoot()).ToList();

        AdminRules.StaticPageViolations(files).ShouldBeEmpty();
        AdminRules.StaticPages.Order().ShouldBe(["Error", "NotFound", "StyleGuide"]);
    }

    [Fact]
    public void A_data_page_excluded_from_interactive_routing_is_flagged_twice_when_it_is_not_anonymous()
    {
        var violations = AdminRules.StaticPageViolations(
        [
            ("src/Pages/Error.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
            ("src/Pages/NotFound.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
            ("src/Pages/StyleGuide.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
            ("src/Pages/Queue.razor", "@attribute [ExcludeFromInteractiveRouting]"),
        ]);

        violations.Count.ShouldBe(2);
        violations.ShouldContain(v => v.Contains("Queue", StringComparison.Ordinal) && v.Contains("StaticPages", StringComparison.Ordinal));
        violations.ShouldContain(v => v.Contains("Queue", StringComparison.Ordinal) && v.Contains("AllowAnonymous", StringComparison.Ordinal));
    }

    [Fact]
    public void An_allowed_page_that_is_not_anonymous_is_flagged_and_the_attribute_is_found_in_a_code_behind_file()
    {
        var violations = AdminRules.StaticPageViolations(
        [
            ("src/Pages/Error.razor", "<h1>Error</h1>"),
            ("src/Pages/Error.razor.cs", "[ExcludeFromInteractiveRouting]\npublic partial class Error;"),
            ("src/Pages/NotFound.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
            ("src/Pages/StyleGuide.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
        ]);

        violations.ShouldBe(["src/Pages/Error is [ExcludeFromInteractiveRouting] but is not [AllowAnonymous]. A static page must hold no agent data."]);
    }

    [Fact]
    public void An_allowlist_entry_with_no_matching_page_is_flagged()
    {
        var violations = AdminRules.StaticPageViolations(
        [
            ("src/Pages/Error.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
        ]);

        violations.ShouldContain(v => v.StartsWith("NotFound is in AdminRules.StaticPages", StringComparison.Ordinal));
        violations.ShouldContain(v => v.StartsWith("StyleGuide is in AdminRules.StaticPages", StringComparison.Ordinal));
    }

    [Fact]
    public void A_comment_that_names_the_attribute_is_not_a_hit()
    {
        AdminRules.StaticPageViolations(
        [
            ("src/Pages/Queue.razor", "// Pages marked [ExcludeFromInteractiveRouting] render statically."),
            ("src/Pages/Error.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
            ("src/Pages/NotFound.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
            ("src/Pages/StyleGuide.razor", "@attribute [AllowAnonymous]\n@attribute [ExcludeFromInteractiveRouting]"),
        ]).ShouldBeEmpty();
    }

    [Fact]
    public void No_razor_file_has_an_inline_script_or_a_style_element()
    {
        var files = AdminRules.ComponentSources(ProjectGraph.FindRepositoryRoot()).ToList();

        files.ShouldContain(f => f.Path.EndsWith("App.razor", StringComparison.Ordinal) && f.Text.Contains("blazor.web.js", StringComparison.Ordinal), "App.razor loads the framework script by src");
        AdminRules.InlineMarkupViolations(files).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<SCRIPT type=\"text/javascript\">x()</SCRIPT>")]
    [InlineData("<style>.a { color: red }</style>")]
    [InlineData("<style media=\"print\"></style>")]
    public void An_inline_script_or_style_element_is_flagged(string markup)
    {
        AdminRules.InlineMarkupViolations([("Components/Bad.razor", markup)]).Count.ShouldBe(1);
    }

    [Fact]
    public void An_external_script_and_an_import_map_component_are_not_flagged()
    {
        AdminRules.InlineMarkupViolations([("Components/App.razor", "<ImportMap />\n<script src=\"@Assets[\"_framework/blazor.web.js\"]\"></script>")]).ShouldBeEmpty();
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet build tests/TechStrap.Architecture.Tests -c Release`
Expected: FAIL with `CS0103: The name 'AdminRules' does not exist in the current context`.

- [ ] **Step 3: Add the rules**

Create `tests/TechStrap.Architecture.Tests/AdminRules.cs`:

```csharp
using System.Text.RegularExpressions;

namespace TechStrap.Architecture.Tests;

/// <summary>
/// Rules for the Admin app (PHASE-07 T20). The Admin is a thin client of the API: no data access, no direct HTTP in a component, and a short,
/// reviewed list of pages that render without a circuit. Every rule is a pure function over text or a parsed project so the tests can feed it a
/// deliberately bad sample and prove it fails.
/// </summary>
public static partial class AdminRules
{
    /// <summary>The only packages the Admin project may reference. A new package is a design decision: add it here in the same commit.</summary>
    public static IReadOnlySet<string> AllowedPackages { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "AspNetCore.SassCompiler",
        "GitVersion.MsBuild",
        "Microsoft.AspNetCore.Authentication.OpenIdConnect",
        "Microsoft.Web.LibraryManager.Build",
        "SyntaxCircus.AspNetCore.Common",
        "SyntaxCircus.AspNetCore.Serilog",
        "SyntaxCircus.Blazor.Auth",
        "SyntaxCircus.Blazor.Components",
        "SyntaxCircus.Common",
        "SyntaxCircus.DotEnv",
        "SyntaxCircus.Http.Resilience",
        "SyntaxCircus.Observability",
    };

    /// <summary>
    /// The pages that may carry [ExcludeFromInteractiveRouting]. AgentGate treats such a page as having no circuit and no session, so it renders its
    /// content without asking the API who the agent is (no NoAccess check). A data-bearing page given the attribute would skip that gate.
    /// </summary>
    public static IReadOnlySet<string> StaticPages { get; } = new HashSet<string>(StringComparer.Ordinal) { "Error", "NotFound", "StyleGuide" };

    [GeneratedRegex(@"\bI?HttpClient(Factory)?\b", RegexOptions.CultureInvariant)]
    private static partial Regex HttpClientUse();

    [GeneratedRegex(@"(@attribute\s*\[|^\s*\[)\s*(?:[\w.]+\s*,\s*)*(?:Microsoft\.AspNetCore\.Components\.)?ExcludeFromInteractiveRouting\b", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex ExcludeAttribute();

    [GeneratedRegex(@"(@attribute\s*\[|^\s*\[)\s*(?:[\w.]+\s*,\s*)*(?:Microsoft\.AspNetCore\.Authorization\.)?AllowAnonymous\b", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex AllowAnonymousAttribute();

    /// <summary>An element that carries code or style in the page: a script without a src, or any style element.</summary>
    [GeneratedRegex(@"<style\b|<script\b(?![^>]*\bsrc\s*=)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex InlineScriptOrStyle();

    public static IReadOnlyList<string> PackageViolations(ProjectNode admin) =>
        [.. admin.PackageReferences
            .Where(package => !AllowedPackages.Contains(package))
            .Order(StringComparer.Ordinal)
            .Select(package => $"{admin.Name} must not reference package {package}. The Admin talks to the API only; add the package to AdminRules.AllowedPackages if it is a reviewed choice.")];

    public static IReadOnlyList<string> HttpClientViolations(IEnumerable<(string Path, string Text)> files) =>
        [.. files
            .Where(f => IsComponentFile(f.Path) && HttpClientUse().IsMatch(f.Text))
            .Select(f => $"{f.Path} uses HttpClient or IHttpClientFactory. Components call the API only through a typed client from Clients/.")];

    public static IReadOnlyList<string> InlineMarkupViolations(IEnumerable<(string Path, string Text)> files) =>
        [.. files
            .Where(f => f.Path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) && InlineScriptOrStyle().IsMatch(f.Text))
            .Select(f => $"{f.Path} has an inline <script> or a <style> element. The CSP allows neither; use wwwroot/js modules and Styles/_*.scss.")];

    public static IReadOnlyList<string> StaticPageViolations(IEnumerable<(string Path, string Text)> files)
    {
        var violations = new List<string>();
        var excluded = files
            .Where(f => IsComponentFile(f.Path))
            .GroupBy(f => ComponentKey(f.Path), StringComparer.Ordinal)
            .Select(group => (Key: group.Key, Text: string.Join('\n', group.Select(f => f.Text))))
            .Where(component => ExcludeAttribute().IsMatch(component.Text))
            .OrderBy(component => component.Key, StringComparer.Ordinal)
            .ToList();

        foreach (var (key, text) in excluded)
        {
            if (!StaticPages.Contains(Path.GetFileName(key)))
            {
                violations.Add($"{key} is [ExcludeFromInteractiveRouting] but is not in AdminRules.StaticPages. A static page skips the agent gate.");
            }

            if (!AllowAnonymousAttribute().IsMatch(text))
            {
                violations.Add($"{key} is [ExcludeFromInteractiveRouting] but is not [AllowAnonymous]. A static page must hold no agent data.");
            }
        }

        var found = excluded.Select(component => Path.GetFileName(component.Key)).ToHashSet(StringComparer.Ordinal);
        foreach (var missing in StaticPages.Except(found).Order(StringComparer.Ordinal))
        {
            violations.Add($"{missing} is in AdminRules.StaticPages but no page of that name is [ExcludeFromInteractiveRouting]. Remove it from the list.");
        }

        return violations;
    }

    /// <summary>Every .razor and .razor.cs file under src/TechStrap.Admin, except bin and obj.</summary>
    public static IEnumerable<(string Path, string Text)> ComponentSources(string repositoryRoot)
    {
        var skipped = new[] { "bin", "obj", "node_modules" }.Select(d => $"{Path.DirectorySeparatorChar}{d}{Path.DirectorySeparatorChar}").ToArray();
        var admin = Path.Combine(repositoryRoot, "src", ReferenceRules.Admin);
        return Directory.EnumerateFiles(admin, "*", SearchOption.AllDirectories)
            .Where(f => !skipped.Any(f.Contains) && IsComponentFile(f))
            .Select(f => (Path.GetRelativePath(repositoryRoot, f), File.ReadAllText(f)));
    }

    private static bool IsComponentFile(string path) =>
        path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase);

    private static string ComponentKey(string path) =>
        path.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase) ? path[..^".razor.cs".Length] : path[..^".razor".Length];
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test --project tests/TechStrap.Architecture.Tests -c Release --filter "AdminRuleTests"`
Expected: PASS, 19 tests. To see each rule bite on the real tree (do not commit these edits), make each change below, rerun the same command with `--no-build`, expect the failure named, and restore the file with `git checkout -- <file>`. These were run in the scratch copy:
- Add `@inject HttpClient Http` as the first line of `src/TechStrap.Admin/Components/Pages/Error.razor`: `No_component_uses_HttpClient` fails.
- Append `<script>x()</script>` to `src/TechStrap.Admin/Components/Pages/Error.razor`: `No_razor_file_has_an_inline_script_or_a_style_element` fails. The same happens for a `<style>` element.
- Delete the `@attribute [AllowAnonymous]` line of `src/TechStrap.Admin/Components/Pages/NotFound.razor`: `Only_the_allowed_pages_are_excluded_from_interactive_routing_and_each_is_anonymous` fails.
- Add `@attribute [ExcludeFromInteractiveRouting]` to `src/TechStrap.Admin/Components/Pages/NoAccessPage.razor`: the same test fails (a page outside the allowlist).
- Add `<PackageReference Include="Npgsql" />` to `src/TechStrap.Admin/TechStrap.Admin.csproj`: `The_Admin_project_references_only_allowed_packages` fails.

- [ ] **Step 5: Whole-project check**

Run: `dotnet build TechStrap.slnx -c Release` (0 warnings), then `dotnet test --project tests/TechStrap.Architecture.Tests -c Release`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add tests/TechStrap.Architecture.Tests/AdminRules.cs tests/TechStrap.Architecture.Tests/AdminRuleTests.cs
git diff --cached --stat
git commit -m "test(architecture): Admin package, HttpClient, static-page and inline-markup rules (P07-T20)" -m "Each rule is a pure function with a deliberately bad sample, so it is proven to fail. The Admin may reference only an allowlist of packages (no EF, Npgsql, Application, Infrastructure or Domain), no component may use HttpClient, only Error, NotFound and StyleGuide may skip interactive routing and each must be anonymous, and no razor file may hold an inline script or style element." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```

### Task 2: Client and session resilience: mid-session 401, the Retry-After cap, the dead catch, exception hardening

**Review Focus pin:**
- **(2)** A 401 that arrives while an agent is working must not lose their draft and must not leave the app showing data as if signed in. Pinned by `SessionExpiryTests` (every API call that answers 401 moves `AgentSession` to SessionExpired and keeps the agent) and `AgentGateSessionExpiryTests.A_session_that_expires_while_working_shows_the_banner_and_keeps_the_same_page_mounted` (the page component is created once and never disposed).
- Also the 07a behaviour made explicit: a 401 on the first `GET /api/agents/me` is still the full session-expired page with no content. Pinned by `AgentGateSessionExpiryTests.A_401_on_the_first_load_is_the_full_page_with_no_content_and_no_banner`.

**Files:**
- Create: `src/TechStrap.Admin/Auth/SessionExpiry.cs`
- Modify: `src/TechStrap.Admin/Auth/AgentSession.cs`
- Modify: `src/TechStrap.Admin/Clients/ApiConnection.cs`, `src/TechStrap.Admin/Clients/ApiClientRegistration.cs`, `src/TechStrap.Admin/Clients/AttachmentPassThrough.cs`
- Create: `src/TechStrap.Admin/Components/Layout/SessionExpiredBanner.razor`, `src/TechStrap.Admin/Components/Layout/SessionExpiredBanner.razor.cs`
- Modify: `src/TechStrap.Admin/Components/Layout/AgentGate.razor`, `AgentGate.razor.cs`, `GateCopy.cs`, `NavMenu.razor`, `NavMenu.razor.cs`, `MainLayout.razor.cs`
- Modify: `src/TechStrap.Admin/Components/Ui/AdminOnly.razor`
- Modify: `src/TechStrap.Admin/Styles/_shell.scss`
- Create: `tests/TechStrap.Admin.Tests/Auth/SessionExpiryTests.cs`, `tests/TechStrap.Admin.Tests/Auth/AgentGateSessionExpiryTests.cs`
- Create: `tests/TechStrap.Admin.Tests/Clients/ReadRetryAfterTests.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/LayoutResilienceTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/Clients/ApiHarness.cs` (an optional `TimeProvider`), `tests/TechStrap.Admin.Tests/Components/ShellTestServices.cs` (register `SessionExpiry`), `tests/TechStrap.Admin.Tests/Auth/AgentSessionTests.cs` (one theory row moves), `tests/TechStrap.Admin.Tests/AttachmentPassThroughTests.cs`
- Modify: `tests/TechStrap.Api.Tests/AdminLeakTests.cs` (a stale comment)

**Interfaces:**
- Consumes: `ApiConnection` (the only place the Admin talks HTTP), `AgentSession`, `AgentGate`, `ProblemMapping` (401 is already `ResultErrorKind.Unauthenticated`), `LocalReturnUrl.Sanitize`, `AdminAuthentication.SignInStartPath` and `ReturnUrlParameter`, `ApiHarness`, `AdminComponentTest`, `RecordingLoggerProvider`, and `AgentSessions` (test support).
- Produces (names are fixed; Tasks 6 to 10 and the docs use them):

```csharp
namespace TechStrap.Admin.Auth;

public sealed class SessionExpiry               // scoped; the single choke point
{
    public event Action? Lapsed;
    public void Report();                        // ApiConnection calls it for every 401
}

public sealed class AgentSession
{
    public AgentSession(IAgentsClient agents, SessionExpiry? expiry = null);   // the optional parameter keeps every existing `new AgentSession(client)` working
    public bool ExpiredWhileWorking { get; }     // State == SessionExpired && Agent is not null
    public bool IsAdmitted { get; }              // State == Ready || ExpiredWhileWorking
    public void MarkUnavailable();               // a load that threw: Unavailable + Retry (Ready and NoAccess are never downgraded)
    // IsAdmin is now "IsAdmitted && role is Admin"
}

namespace TechStrap.Admin.Components.Layout;

public sealed partial class SessionExpiredBanner : ComponentBase, IDisposable   // role="alert", class ts-session-banner; link /signin/start?returnUrl=<current local url>
public static class GateCopy { SessionLapsedTitle, SessionLapsedBody }          // new constants

namespace TechStrap.Admin.Clients;

public static class ApiClientRegistration
{
    public static readonly TimeSpan ReadRetryAfterCap;                           // 2 seconds
    internal static TimeSpan? RetryAfterDelay(HttpResponseMessage? response, DateTimeOffset now);
}
```

**Rules:**
1. **One choke point.** `ApiConnection` reports every 401 to `SessionExpiry`; `AgentSession` listens. No page, client or component has 401 code. The report carries no data (no URL, no header, no body).
2. **Mid-session means "the agent had been admitted".** A lapse while `Agent` is null (the first `/me` is still in flight, or it was refused) is ignored: the first answer decides, and a 401 on it is the full page of 07a. A lapse after that sets SessionExpired and keeps `Agent`, so `ExpiredWhileWorking` is true.
3. **The page stays mounted.** `AgentGate` renders the banner and, in one fixed place, the content for `IsAdmitted`. Putting the content in the same render position for Ready and for expired-while-working is what keeps Blazor from rebuilding it (a rebuild would drop an unsent reply). Do not move `@ChildContent` into a `switch` case.
4. **The rail and admin pages follow `IsAdmitted`,** not `State == Ready`, so the rail does not vanish and an admin page does not blank when the session lapses. The API still refuses every call with 401; nothing new is allowed.
5. **The banner link is a plain `<a>`,** not a form: it needs no circuit, and the CSP `form-action` (Task 4) does not apply to a link navigation. It carries the current local path and query; `LocalReturnUrl.Sanitize` checks it again on the server.
6. **A reload that answers 401 also keeps the agent.** The one existing test that expected `Agent` to be null after a 401 reload (`AgentSessionTests.A_reload_that_the_api_refuses_wins_over_the_ready_session`) loses its 401 row; `SessionExpiryTests.A_reload_that_answers_401_keeps_the_agent_and_expires_the_session` replaces it. A 403 still ends in NoAccess with no agent.
7. **Retry-After is honoured and capped.** The Http.Resilience default (`ShouldRetryAfterHeader = true`) honours the header without a limit, so a 503 with `Retry-After: 120` would hold a read for up to the 30 s client timeout. The retry options now set `ShouldRetryAfterHeader = false` and a `DelayGenerator` that returns the header's wait, capped at `ReadRetryAfterCap` (2 s), or null so the 250 ms exponential backoff applies. The write client has no retry and is unchanged.
8. **The dead catch goes.** The read client has no circuit breaker (D-040, `ReadClientIsolationTests`), so `BrokenCircuitException` can never reach `AttachmentPassThrough`. The clause and the `Polly` reference in that file are deleted.
9. **No exception text in logs.** The three hardened places log the exception type name only (`ex.GetType().Name`), never the exception or its message, which can carry an address or a name. Each is a `LogWarning`, and each degrades: the badge keeps its last value, the keyboard layer stays off, the gate shows Unavailable with Retry.
10. **Encoding.** All new strings are ASCII. After writing the files run `grep -nP "[^\x00-\x7F]"` over them; it must find nothing.

- [ ] **Step 1: Write the failing tests**

1. `tests/TechStrap.Admin.Tests/Auth/SessionExpiryTests.cs` (the choke point, through the real clients over the stub API):

```csharp
using System.Net;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Tests.Clients;
using TechStrap.Contracts.Agents;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Auth;

/// <summary>
/// Review Focus 2, session half: a 401 from any API call after the agent was let in moves <see cref="AgentSession"/> to SessionExpired, through one choke point
/// (<c>ApiConnection</c>), and keeps the agent so the page can stay mounted.
/// </summary>
public sealed class SessionExpiryTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static AgentDto Agent() => new(Guid.NewGuid(), "Sam", "sam@orbitly.test", AgentRoles.Agent, true, null, null);

    /// <summary>The real clients over the stub API, with the agent already let in by the real <c>/me</c> call.</summary>
    private static async Task<(ApiHarness Api, AgentSession Session)> ReadyAsync()
    {
        var api = await ApiHarness.CreateAsync();
        api.Stub.WithTestAgents();
        var session = api.Get<AgentSession>();
        await session.EnsureLoadedAsync(Ct);
        session.State.ShouldBe(AgentSessionState.Ready);
        return (api, session);
    }

    [Fact]
    public async Task A_401_on_a_read_moves_a_ready_session_to_expired_and_keeps_the_agent()
    {
        var (api, session) = await ReadyAsync();
        await using var scope = api;
        api.Stub.OnStatus(HttpMethod.Get, "/api/tickets/counts", HttpStatusCode.Unauthorized);

        var result = await api.Get<ApiConnection>().GetAsync<int>("api/tickets/counts", Ct);

        result.Errors[0].Code.ShouldBe(ApiErrorCodes.Unauthenticated);
        session.State.ShouldBe(AgentSessionState.SessionExpired);
        session.ExpiredWhileWorking.ShouldBeTrue();
        session.Agent.ShouldNotBeNull("the page stays mounted, so the agent stays");
        session.IsAdmitted.ShouldBeTrue();
        session.ErrorCode.ShouldBe(ApiErrorCodes.Unauthenticated);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task A_401_on_a_write_expires_the_session_too(string verb)
    {
        var (api, session) = await ReadyAsync();
        await using var scope = api;
        var method = new HttpMethod(verb);
        api.Stub.OnStatus(method, "/api/thing", HttpStatusCode.Unauthorized);

        var result = await api.Get<ApiConnection>().SendAsync(method, "api/thing", new { note = "x" }, Ct);

        result.IsFailure.ShouldBeTrue();
        session.ExpiredWhileWorking.ShouldBeTrue();
    }

    [Fact]
    public async Task A_401_on_a_write_with_a_body_expires_the_session()
    {
        var (api, session) = await ReadyAsync();
        await using var scope = api;
        api.Stub.OnStatus(HttpMethod.Post, "/api/thing", HttpStatusCode.Unauthorized);

        var result = await api.Get<ApiConnection>().SendAsync<int>(HttpMethod.Post, "api/thing", new { note = "x" }, Ct);

        result.Errors[0].Code.ShouldBe(ApiErrorCodes.Unauthenticated);
        session.ExpiredWhileWorking.ShouldBeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Any_other_failure_leaves_the_session_ready(HttpStatusCode status)
    {
        var (api, session) = await ReadyAsync();
        await using var scope = api;
        api.Stub.OnStatus(HttpMethod.Get, "/api/thing", status);

        await api.Get<ApiConnection>().GetAsync<int>("api/thing", Ct);

        session.State.ShouldBe(AgentSessionState.Ready);
        session.ExpiredWhileWorking.ShouldBeFalse();
    }

    [Fact]
    public async Task Several_401s_raise_one_change_so_the_banner_does_not_flicker()
    {
        var (api, session) = await ReadyAsync();
        await using var scope = api;
        var changes = 0;
        session.Changed += () => changes++;
        api.Stub.OnStatus(HttpMethod.Get, "/api/thing", HttpStatusCode.Unauthorized);

        await api.Get<ApiConnection>().GetAsync<int>("api/thing", Ct);
        await api.Get<ApiConnection>().GetAsync<int>("api/thing", Ct);
        await api.Get<ApiConnection>().GetAsync<int>("api/thing", Ct);

        changes.ShouldBe(1);
        session.State.ShouldBe(AgentSessionState.SessionExpired);
    }

    [Fact]
    public void A_401_before_the_agent_is_admitted_is_left_to_the_first_load()
    {
        var agents = Substitute.For<IAgentsClient>();
        var expiry = new SessionExpiry();
        var session = new AgentSession(agents, expiry);
        var raised = false;
        session.Changed += () => raised = true;

        expiry.Report();

        session.State.ShouldBe(AgentSessionState.NotLoaded);
        session.ExpiredWhileWorking.ShouldBeFalse();
        raised.ShouldBeFalse();
    }

    [Fact]
    public async Task A_401_on_the_very_first_load_is_the_full_expired_state_with_no_agent()
    {
        var agents = Substitute.For<IAgentsClient>();
        agents.GetMeAsync(Ct).Returns(Result<AgentDto>.Failure(new ResultError(ApiErrorCodes.Unauthenticated, "No.", ResultErrorKind.Unauthenticated)));
        var session = new AgentSession(agents, new SessionExpiry());

        await session.EnsureLoadedAsync(Ct);

        session.State.ShouldBe(AgentSessionState.SessionExpired);
        session.Agent.ShouldBeNull();
        session.ExpiredWhileWorking.ShouldBeFalse("nothing was mounted yet, so the gate shows the full page");
        session.IsAdmitted.ShouldBeFalse();
    }

    [Fact]
    public async Task A_reload_that_answers_401_keeps_the_agent_and_expires_the_session()
    {
        var agents = Substitute.For<IAgentsClient>();
        agents.GetMeAsync(Ct).Returns(
            Result<AgentDto>.Success(Agent()),
            Result<AgentDto>.Failure(new ResultError(ApiErrorCodes.Unauthenticated, "No.", ResultErrorKind.Unauthenticated)));
        var session = new AgentSession(agents, new SessionExpiry());
        await session.EnsureLoadedAsync(Ct);

        await session.ReloadAsync(Ct);

        session.ExpiredWhileWorking.ShouldBeTrue();
        session.Agent.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_reload_after_the_session_expired_does_not_drop_to_checking_while_it_runs()
    {
        var agents = Substitute.For<IAgentsClient>();
        var expiry = new SessionExpiry();
        var gate = new TaskCompletionSource<Result<AgentDto>>();
        agents.GetMeAsync(Ct).Returns(Task.FromResult(Result<AgentDto>.Success(Agent())), gate.Task);
        var session = new AgentSession(agents, expiry);
        await session.EnsureLoadedAsync(Ct);
        expiry.Report();

        var reload = session.ReloadAsync(Ct);

        session.IsAdmitted.ShouldBeTrue("the page must stay mounted while the reload runs");
        gate.SetResult(Result<AgentDto>.Success(Agent()));
        await reload;
        session.State.ShouldBe(AgentSessionState.Ready);
    }

    [Fact]
    public async Task A_load_that_throws_can_be_marked_unavailable_and_a_ready_session_is_never_downgraded()
    {
        var agents = Substitute.For<IAgentsClient>();
        agents.GetMeAsync(Ct).Returns(Result<AgentDto>.Success(Agent()));
        var ready = new AgentSession(agents, new SessionExpiry());
        await ready.EnsureLoadedAsync(Ct);
        ready.MarkUnavailable();
        ready.State.ShouldBe(AgentSessionState.Ready);

        var fresh = new AgentSession(Substitute.For<IAgentsClient>(), new SessionExpiry());
        fresh.MarkUnavailable();
        fresh.State.ShouldBe(AgentSessionState.Unavailable);
        fresh.ErrorCode.ShouldBe(ApiErrorCodes.ApiUnavailable);
    }
}
```

2. `tests/TechStrap.Admin.Tests/Auth/AgentGateSessionExpiryTests.cs` (the page half):

```csharp
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Layout;
using TechStrap.Admin.Tests.Components;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Auth;

/// <summary>
/// Review Focus 2, page half: a session that ends while the agent is working shows a banner over the same, still-mounted page, so an unsent reply or note
/// survives. A 401 on the first load stays the full session-expired page (07a), and an unexpected fault while loading degrades instead of ending the circuit.
/// </summary>
public sealed class AgentGateSessionExpiryTests : BunitContext
{
    private static readonly CancellationToken Ct = Xunit.TestContext.Current.CancellationToken;

    private readonly IAgentsClient _agents;
    private readonly RecordingLoggerProvider _logs = new();

    public AgentGateSessionExpiryTests()
    {
        _agents = this.AddAgentShell();
        Services.AddLogging(logging => logging.AddProvider(_logs));
    }

    private AgentSession Session => Services.GetRequiredService<AgentSession>();

    private IRenderedComponent<AgentGate> RenderGate() => Render<AgentGate>(p => p
        .SignedIn()
        .Add(g => g.ChildContent, (RenderFragment)(builder =>
        {
            builder.OpenComponent<Draft>(0);
            builder.CloseComponent();
        })));

    /// <summary>Stands in for a composer: it holds what the agent typed in a field of its own, and counts how often it was created or removed.</summary>
    private sealed class Draft : ComponentBase, IDisposable
    {
        public static int Created;
        public static int Disposed;

        protected override void OnInitialized() => Interlocked.Increment(ref Created);

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "p");
            builder.AddAttribute(1, "id", "draft");
            builder.AddContent(2, "unsent reply");
            builder.CloseElement();
        }

        public void Dispose() => Interlocked.Increment(ref Disposed);
    }

    private static void ResetDraftCounters()
    {
        Draft.Created = 0;
        Draft.Disposed = 0;
    }

    [Fact]
    public async Task A_session_that_expires_while_working_shows_the_banner_and_keeps_the_same_page_mounted()
    {
        ResetDraftCounters();
        var cut = RenderGate();
        await Session.EnsureLoadedAsync(Ct);
        cut.WaitForAssertion(() => cut.Find("#draft"));
        cut.FindAll(".ts-session-banner").ShouldBeEmpty();

        Services.GetRequiredService<SessionExpiry>().Report();

        cut.WaitForElement(".ts-session-banner");
        cut.Find("#draft").TextContent.ShouldBe("unsent reply");
        Draft.Created.ShouldBe(1, "the page component must be created once, not rebuilt when the banner appears");
        Draft.Disposed.ShouldBe(0, "tearing the page down would lose an unsent reply");
        cut.Find(".ts-session-banner").GetAttribute("role").ShouldBe("alert");
        cut.Find(".ts-session-banner").TextContent.ShouldContain(GateCopy.SessionLapsedTitle);
        cut.FindAll("section.ts-gate").ShouldBeEmpty("the full-page expired state is only for the first load");
    }

    [Fact]
    public async Task The_banner_links_to_sign_in_with_the_current_local_address()
    {
        var cut = RenderGate();
        await Session.EnsureLoadedAsync(Ct);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/queue/mine?status=open&search=a%20b");

        Services.GetRequiredService<SessionExpiry>().Report();

        var link = cut.WaitForElement(".ts-session-banner a");
        link.TextContent.ShouldBe(GateCopy.SignInAgain);
        link.GetAttribute("href").ShouldBe("/signin/start?returnUrl=%2Fqueue%2Fmine%3Fstatus%3Dopen%26search%3Da%2520b");
    }

    [Fact]
    public async Task The_banner_link_follows_the_agent_when_they_navigate()
    {
        var cut = RenderGate();
        await Session.EnsureLoadedAsync(Ct);
        Services.GetRequiredService<SessionExpiry>().Report();
        cut.WaitForElement(".ts-session-banner a");

        Services.GetRequiredService<NavigationManager>().NavigateTo("/tickets/ORB-7");

        cut.WaitForAssertion(() => cut.Find(".ts-session-banner a").GetAttribute("href").ShouldBe("/signin/start?returnUrl=%2Ftickets%2FORB-7"));
    }

    [Fact]
    public void A_401_on_the_first_load_is_the_full_page_with_no_content_and_no_banner()
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Failure(new ResultError(ApiErrorCodes.Unauthenticated, "No.", ResultErrorKind.Unauthenticated)));
        ResetDraftCounters();

        var cut = RenderGate();

        cut.FindAll("#draft").ShouldBeEmpty();
        cut.FindAll(".ts-session-banner").ShouldBeEmpty();
        cut.Find("section.ts-gate h1").TextContent.ShouldBe(GateCopy.SessionExpiredTitle);
        cut.Find("form[action='/signin/start'] button").TextContent.ShouldBe(GateCopy.SignInAgain);
    }

    [Fact]
    public void An_unexpected_fault_while_loading_shows_unavailable_with_retry_and_logs_only_the_type()
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromException<Result<AgentDto>>(new InvalidOperationException("secret-detail sam@orbitly.test")),
            Task.FromResult(Result<AgentDto>.Success(new AgentDto(Guid.NewGuid(), "Sam", "sam@orbitly.test", AgentRoles.Agent, true, null, null))));
        ResetDraftCounters();

        var cut = RenderGate();

        cut.FindAll("#draft").ShouldBeEmpty();
        cut.Find("section.ts-gate h1").TextContent.ShouldBe(GateCopy.UnavailableTitle);
        _logs.Lines.ShouldContain(line => line.Contains("InvalidOperationException", StringComparison.Ordinal));
        _logs.Lines.ShouldNotContain(line => line.Contains("secret-detail", StringComparison.Ordinal) || line.Contains("sam@orbitly.test", StringComparison.Ordinal));

        cut.Find("section.ts-gate button").Click();

        cut.WaitForAssertion(() => cut.Find("#draft").TextContent.ShouldBe("unsent reply"));
    }
}
```

3. `tests/TechStrap.Admin.Tests/Components/LayoutResilienceTests.cs` (the rail and the layout never end the circuit):

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;
using NSubstitute;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Layout;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// The rail and the layout sit outside every error boundary, so an exception out of their <c>OnAfterRenderAsync</c> would end the circuit. Each must log
/// the exception type (never the message, which can carry an address) and degrade.
/// </summary>
public sealed class LayoutResilienceTests : BunitContext
{
    private static readonly CancellationToken Ct = Xunit.TestContext.Current.CancellationToken;

    private const string Secret = "secret-detail ada@example.com";

    private readonly RecordingLoggerProvider _logs = new();

    public LayoutResilienceTests()
    {
        Services.AddLogging(logging => logging.AddProvider(_logs));
        Services.AddShell();
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider());
    }

    private void NoLeak() =>
        _logs.Lines.ShouldNotContain(line => line.Contains("secret-detail", StringComparison.Ordinal) || line.Contains("ada@example.com", StringComparison.Ordinal));

    [Fact]
    public async Task A_failing_badge_read_leaves_the_rail_working_and_logs_only_the_type()
    {
        this.AddAgentShell(AgentRoles.Admin);
        var deadLetters = Services.GetRequiredService<IDeadLettersClient>();
        deadLetters.CountAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<SyntaxCircus.Common.Result<int>>(new InvalidOperationException(Secret)));
        await Services.GetRequiredService<AgentSession>().EnsureLoadedAsync(Ct);

        var cut = Render<NavMenu>(p => p.SignedIn());

        cut.WaitForAssertion(() => _logs.Lines.ShouldContain(line => line.Contains("InvalidOperationException", StringComparison.Ordinal)));
        cut.FindAll("a.ts-rail-link").Count.ShouldBe(7);
        cut.FindAll(".ts-rail-badge").ShouldBeEmpty();
        NoLeak();
        await deadLetters.Received(1).CountAsync(Arg.Any<CancellationToken>());
    }

    private void SetupScripts(Action<BunitJSModuleInterop> shortcuts)
    {
        var shortcutModule = JSInterop.SetupModule("./js/shortcuts.js");
        shortcuts(shortcutModule);
        shortcutModule.SetupVoid("unregister", _ => true).SetVoidResult();
        JSInterop.SetupModule("./js/dialog.js");
        var preferences = JSInterop.SetupModule("./js/preferences.js");
        preferences.Setup<StoredPreferences>("load", _ => true).SetResult(new StoredPreferences(SingleKeyShortcuts: true, Theme: "auto"));
    }

    [Fact]
    public void A_shortcut_listener_that_cannot_start_leaves_the_pages_working_and_logs_only_the_type()
    {
        this.AddAgentShell();
        SetupScripts(module => module.SetupVoid("register", _ => true).SetException(new JSException(Secret)));

        var cut = Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, b => b.AddMarkupContent(0, "<p id=\"page\">page</p>")));

        cut.WaitForAssertion(() => _logs.Lines.ShouldContain(line => line.Contains("JSException", StringComparison.Ordinal)));
        cut.WaitForAssertion(() => cut.Find("main.ts-main #page").TextContent.ShouldBe("page"));
        cut.Find(".ts-brand").ShouldNotBeNull();
        NoLeak();
    }

    [Fact]
    public async Task The_rail_and_an_admin_page_stay_complete_when_the_session_expires_while_working()
    {
        this.AddAgentShell(AgentRoles.Admin);
        var session = Services.GetRequiredService<AgentSession>();
        await session.EnsureLoadedAsync(Ct);
        var rail = Render<NavMenu>(p => p.SignedIn());
        var guarded = Render<AdminOnly>(p => p.AddChildContent("<p id='admin'>admin content</p>"));
        rail.FindAll("a.ts-rail-link").Count.ShouldBe(7);

        Services.GetRequiredService<SessionExpiry>().Report();

        session.ExpiredWhileWorking.ShouldBeTrue();
        rail.WaitForAssertion(() => rail.FindAll("a.ts-rail-link").Count.ShouldBe(7));
        guarded.WaitForAssertion(() => guarded.Find("#admin").TextContent.ShouldBe("admin content"));
    }
}
```

4. `tests/TechStrap.Admin.Tests/Clients/ReadRetryAfterTests.cs` (the cap, with a fake clock so the test never waits):

```csharp
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Time.Testing;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Clients;

/// <summary>
/// The read client honours <c>Retry-After</c> but never waits more than <see cref="ApiClientRegistration.ReadRetryAfterCap"/> for it: an overloaded API that
/// asks for two minutes must not freeze a page on "Loading" until the client timeout.
/// </summary>
public sealed class ReadRetryAfterTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

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
    public void A_delay_in_seconds_is_honoured_up_to_the_cap(int seconds, double expectedSeconds)
    {
        var delay = ApiClientRegistration.RetryAfterDelay(WithRetryAfter(new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds))), Now);

        delay.ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void A_date_is_converted_with_the_clock_and_capped_the_same_way()
    {
        ApiClientRegistration.RetryAfterDelay(WithRetryAfter(new RetryConditionHeaderValue(Now.AddMilliseconds(1500))), Now).ShouldBe(TimeSpan.FromMilliseconds(1500));
        ApiClientRegistration.RetryAfterDelay(WithRetryAfter(new RetryConditionHeaderValue(Now.AddMinutes(10))), Now).ShouldBe(ApiClientRegistration.ReadRetryAfterCap);
    }

    [Fact]
    public void No_header_a_zero_delay_or_a_date_in_the_past_leaves_the_backoff_in_charge()
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
        await using var api = await ApiHarness.CreateAsync(time: time);
        api.Stub.On(HttpMethod.Get, "/api/thing", _ => WithRetryAfter(new RetryConditionHeaderValue(TimeSpan.FromSeconds(120))));

        var call = api.Get<ApiConnection>().GetAsync<TicketStateDto>("api/thing", TestContext.Current.CancellationToken);
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

5. Test support and the changed rows:

```diff
--- a/tests/TechStrap.Admin.Tests/Clients/ApiHarness.cs
+++ b/tests/TechStrap.Admin.Tests/Clients/ApiHarness.cs
@@ -29,7 +29,7 @@
 
     public IServiceProvider Services => _scope.ServiceProvider;
 
-    public static async Task<ApiHarness> CreateAsync(AdminTestPrincipal? principal = null, bool seedToken = true)
+    public static async Task<ApiHarness> CreateAsync(AdminTestPrincipal? principal = null, bool seedToken = true, TimeProvider? time = null)
     {
         principal ??= AdminTestPrincipal.Agent;
         var stub = new StubApiHandler();
@@ -37,6 +37,12 @@
 
         var services = new ServiceCollection();
         services.AddLogging();
+        if (time is not null)
+        {
+            // The resilience pipelines take their clock from the container, so a test can step through the retry delays instead of waiting for them.
+            services.AddSingleton(time);
+        }
+
         services.AddSingleton<IConfiguration>(configuration);
         services.AddBlazorTokenForwarding(configuration, "Auth");
         services.AddScoped<AuthenticationStateProvider>(_ => new FixedAuthenticationStateProvider(principal.ToClaimsPrincipal("Test")));
```

```diff
--- a/tests/TechStrap.Admin.Tests/Components/ShellTestServices.cs
+++ b/tests/TechStrap.Admin.Tests/Components/ShellTestServices.cs
@@ -27,6 +27,7 @@
         var deadLetters = Substitute.For<IDeadLettersClient>();
         deadLetters.CountAsync(Arg.Any<CancellationToken>()).Returns(Result<int>.Success(0));
         context.Services.AddSingleton(deadLetters);
+        context.Services.AddSingleton<SessionExpiry>();
         context.Services.AddSingleton<AgentSession>();
         context.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new AgentGroupOptions()));
         context.Services.AddSingleton<AntiforgeryStateProvider, NoTokenAntiforgeryStateProvider>();
```

```diff
--- a/tests/TechStrap.Admin.Tests/Auth/AgentSessionTests.cs
+++ b/tests/TechStrap.Admin.Tests/Auth/AgentSessionTests.cs
@@ -239,7 +239,7 @@
 
     [Theory]
     [InlineData(ApiErrorCodes.AgentInactive, ResultErrorKind.Forbidden, AgentSessionState.NoAccess)]
-    [InlineData(ApiErrorCodes.Unauthenticated, ResultErrorKind.Unauthenticated, AgentSessionState.SessionExpired)]
+    // A 401 on a reload is no longer here: the session expires but keeps the agent so the page stays mounted (SessionExpiryTests).
     public async Task A_reload_that_the_api_refuses_wins_over_the_ready_session(string code, ResultErrorKind kind, AgentSessionState expected)
     {
         _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(Agent(AgentRoles.Admin)), Refused(code, kind));
```

```diff
--- a/tests/TechStrap.Admin.Tests/AttachmentPassThroughTests.cs
+++ b/tests/TechStrap.Admin.Tests/AttachmentPassThroughTests.cs
@@ -153,4 +153,30 @@
         public override void SetLength(long value) => throw new NotSupportedException();
         public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
     }
+
+    // The read client has no circuit breaker (ReadClientIsolationTests), so a transport failure or a timeout is all the pass-through can see.
+    [Theory]
+    [InlineData("transport")]
+    [InlineData("timeout")]
+    public async Task A_transport_failure_or_a_timeout_is_a_502_and_never_an_unhandled_error(string failure)
+    {
+        await using var factory = new AdminFactory();
+        var id = Guid.NewGuid();
+        factory.Api.On(HttpMethod.Get, $"/api/attachments/{id}", _ => failure == "transport" ? throw new HttpRequestException("connection refused") : throw new TimeoutException());
+        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);
+
+        using var response = await client.GetAsync($"/attachments/{id}", Ct);
+
+        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
+        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("connection refused");
+    }
+
+    [Fact]
+    public void The_pass_through_does_not_name_a_circuit_breaker_the_read_client_cannot_throw()
+    {
+        var source = System.IO.File.ReadAllText(TechStrap.Tests.Shared.RepositoryRoot.Combine("src", "TechStrap.Admin", "Clients", "AttachmentPassThrough.cs"));
+
+        source.ShouldNotContain("BrokenCircuitException", Case.Sensitive, "D-040: the read client has no circuit breaker, so that catch clause is dead code");
+        source.ShouldNotContain("Polly", Case.Sensitive);
+    }
 }
```

6. The leak test's comment still says a 5xx would trip a circuit breaker that does not exist:

```diff
--- a/tests/TechStrap.Api.Tests/AdminLeakTests.cs
+++ b/tests/TechStrap.Api.Tests/AdminLeakTests.cs
@@ -40,8 +40,8 @@
     public async Task The_access_token_appears_in_no_log_event_page_or_download()
     {
         await using var factory = VerboseFactory();
-        // The queue and the ticket are not configured, so the stub answers 404 and the pages show their error states. A 5xx here would trip the read client's circuit
-        // breaker and the download below would never reach the API.
+        // The queue and the ticket are not configured, so the stub answers 404 and the pages show their error states. The read client has no circuit breaker (D-040), so
+        // nothing here can stop the download below reaching the API.
         factory.Api.On(HttpMethod.Get, $"/api/attachments/{AttachmentId}", _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
         var token = AdminTestPrincipal.Agent.AccessToken;
         using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet build tests/TechStrap.Admin.Tests -c Release`
Expected: FAIL to build (`SessionExpiry`, `ExpiredWhileWorking`, `RetryAfterDelay` and `SessionExpiredBanner` do not exist; `ApiHarness.CreateAsync` has no `time` parameter until its edit is applied).

- [ ] **Step 3: Add the session, the choke point, the gate and the banner**

1. `src/TechStrap.Admin/Auth/SessionExpiry.cs`:

```csharp
namespace TechStrap.Admin.Auth;

/// <summary>
/// The single choke point for "the API answered 401 to this agent" (PHASE-07c). <c>ApiConnection</c>, the only place the Admin talks HTTP to the API, calls
/// <see cref="Report"/> for every 401, and <see cref="AgentSession"/> listens, so no page needs its own code and none can forget it. It is a separate scoped
/// object (not a call from the connection to the session) because the session depends on the agents client, which depends on the connection.
/// </summary>
public sealed class SessionExpiry
{
    /// <summary>Raised on every reported 401. It may be raised from any thread.</summary>
    public event Action? Lapsed;

    public void Report() => Lapsed?.Invoke();
}
```

2. `AgentSession`: the constructor takes the optional `SessionExpiry`, `IsAdmin` follows `IsAdmitted`, a lapse expires a working session, a reload that answers 401 keeps the agent, and `MarkUnavailable` degrades a load that threw:

```diff
--- a/src/TechStrap.Admin/Auth/AgentSession.cs
+++ b/src/TechStrap.Admin/Auth/AgentSession.cs
@@ -28,9 +28,23 @@
 /// <c>AgentGate</c>, which renders them only when <see cref="State"/> is <see cref="AgentSessionState.Ready"/>, so NoAccess always wins and no ticket call
 /// precedes a successful <c>/me</c>. Scoped: one per circuit (and one per prerender request).
 /// </summary>
-public sealed class AgentSession(IAgentsClient agents)
+public sealed class AgentSession
 {
+    private readonly IAgentsClient _agents;
     private Task? _loading;
+
+    /// <param name="agents">The API client that answers <c>GET /api/agents/me</c>.</param>
+    /// <param name="expiry">
+    /// Reports a 401 from any later API call. Optional so a test can build a session over a substitute client alone; the app always registers it.
+    /// </param>
+    public AgentSession(IAgentsClient agents, SessionExpiry? expiry = null)
+    {
+        _agents = agents;
+        if (expiry is not null)
+        {
+            expiry.Lapsed += OnLapsed;
+        }
+    }
 
     public AgentSessionState State { get; private set; }
 
@@ -44,7 +58,17 @@
     public string? ErrorMessage { get; private set; }
 
     /// <summary>Admin-only actions (delete, erase) show only for an Admin. The API still enforces it.</summary>
-    public bool IsAdmin => State == AgentSessionState.Ready && Agent?.Role == AgentRoles.Admin;
+    public bool IsAdmin => IsAdmitted && Agent?.Role == AgentRoles.Admin;
+
+    /// <summary>
+    /// True when the session ended (a 401) after the API had let the agent in. <see cref="State"/> is SessionExpired but <see cref="Agent"/> is still set, and the
+    /// layout keeps the page mounted with a banner, so what the agent was typing survives until they sign in again. A 401 on the very first
+    /// <c>/me</c> is not this: there is no agent and no page content yet, so the gate shows the full session-expired page.
+    /// </summary>
+    public bool ExpiredWhileWorking => State == AgentSessionState.SessionExpired && Agent is not null;
+
+    /// <summary>True when the pages and the rail may show: the agent is Ready, or was Ready when the session expired (<see cref="ExpiredWhileWorking"/>).</summary>
+    public bool IsAdmitted => State == AgentSessionState.Ready || ExpiredWhileWorking;
 
     /// <summary>Raised after every state change so the gate and the navigation can re-render.</summary>
     public event Action? Changed;
@@ -82,7 +106,7 @@
     {
         try
         {
-            Apply(await agents.GetMeAsync(cancellationToken), keepReadyOnTransientFailure);
+            Apply(await _agents.GetMeAsync(cancellationToken), keepReadyOnTransientFailure);
         }
         finally
         {
@@ -102,7 +126,7 @@
     /// </summary>
     public async Task ReloadAsync(CancellationToken cancellationToken)
     {
-        var keepReady = State == AgentSessionState.Ready;
+        var keepReady = IsAdmitted;
         if (!keepReady)
         {
             State = AgentSessionState.NotLoaded;
@@ -122,8 +146,55 @@
         }
     }
 
+    /// <summary>
+    /// A load that could not even be attempted (an unexpected exception, not an API answer): show the Unavailable state with its Retry button instead of
+    /// ending the circuit. Ready and NoAccess are final and are not touched.
+    /// </summary>
+    public void MarkUnavailable()
+    {
+        if (State is AgentSessionState.Ready or AgentSessionState.NoAccess)
+        {
+            return;
+        }
+
+        Agent = null;
+        ErrorCode = ApiErrorCodes.ApiUnavailable;
+        ErrorMessage = "TechStrap could not check your access. Try again in a moment.";
+        State = AgentSessionState.Unavailable;
+        Changed?.Invoke();
+    }
+
+    private void OnLapsed()
+    {
+        // Before the agent is admitted the first /me answers for itself (Apply); only a lapse after that is a mid-session expiry.
+        if (Agent is not null)
+        {
+            Expire();
+        }
+    }
+
+    private void Expire()
+    {
+        if (State == AgentSessionState.SessionExpired)
+        {
+            return;
+        }
+
+        ErrorCode = ApiErrorCodes.Unauthenticated;
+        ErrorMessage = "Your session has expired. Sign in again.";
+        State = AgentSessionState.SessionExpired;
+        Changed?.Invoke();
+    }
+
     private void Apply(Result<AgentDto> result, bool keepReadyOnTransientFailure)
     {
+        if (result.IsFailure && result.Errors[0].Kind == ResultErrorKind.Unauthenticated && Agent is not null)
+        {
+            // A reload of a session that was working: keep the agent (and so the page) and show the banner.
+            Expire();
+            return;
+        }
+
         if (keepReadyOnTransientFailure && result.IsFailure && result.Errors[0].Code is ApiErrorCodes.ApiUnavailable or ApiErrorCodes.ApiTimeout or ApiErrorCodes.ApiError)
         {
             return;
```

3. `ApiConnection` reports every 401, and `AddTechStrapApiClients` registers the scoped `SessionExpiry` (shown with the Retry-After change below).

```diff
--- a/src/TechStrap.Admin/Clients/ApiConnection.cs
+++ b/src/TechStrap.Admin/Clients/ApiConnection.cs
@@ -1,8 +1,10 @@
 using System.Globalization;
+using System.Net;
 using System.Net.Http.Json;
 using System.Text.Json;
 using SyntaxCircus.Blazor.Auth;
 using SyntaxCircus.Common;
+using TechStrap.Admin.Auth;
 
 namespace TechStrap.Admin.Clients;
 
@@ -12,7 +14,7 @@
 /// <see cref="Result"/>. It does not use ApiClientBase because that drops the <c>errorCodes</c> the API sends and has no Result mapping. Cancellation by the
 /// caller propagates as <see cref="OperationCanceledException"/>; it is never turned into a Result. One instance per scope (circuit).
 /// </summary>
-internal sealed class ApiConnection(IBlazorCircuitHttpClientFactory httpClients)
+internal sealed class ApiConnection(IBlazorCircuitHttpClientFactory httpClients, SessionExpiry expiry)
 {
     private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
 
@@ -52,13 +54,14 @@
         return await SendAsync<T>(WriteClient, request, cancellationToken);
     }
 
-    private static async Task<Result<T>> SendAsync<T>(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
+    private async Task<Result<T>> SendAsync<T>(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
     {
         try
         {
             using var response = await client.SendAsync(request, cancellationToken);
             if (!response.IsSuccessStatusCode)
             {
+                ReportIfUnauthenticated(response);
                 var errors = ProblemMapping.Map(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
                 return Result<T>.Failure(errors[0], [.. errors.Skip(1)]);
             }
@@ -77,7 +80,7 @@
         }
     }
 
-    private static async Task<Result> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
+    private async Task<Result> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
     {
         try
         {
@@ -87,6 +90,7 @@
                 return Result.Success();
             }
 
+            ReportIfUnauthenticated(response);
             var errors = ProblemMapping.Map(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
             return Result.Failure(errors[0], [.. errors.Skip(1)]);
         }
@@ -97,6 +101,18 @@
         catch (Exception ex) when (Transport(ex, cancellationToken) is { } error)
         {
             return Result.Failure(error);
+        }
+    }
+
+    /// <summary>
+    /// A 401 means the agent's session is over (the access token was refused and could not be renewed). Every call passes here, so one report moves
+    /// <see cref="AgentSession"/> to SessionExpired and no page needs code of its own. Only the status is looked at; nothing about the request is reported.
+    /// </summary>
+    private void ReportIfUnauthenticated(HttpResponseMessage response)
+    {
+        if (response.StatusCode == HttpStatusCode.Unauthorized)
+        {
+            expiry.Report();
         }
     }
 
```

4. The gate, the banner and their copy:

`src/TechStrap.Admin/Components/Layout/AgentGate.razor`:

```razor
@if (_authenticated is null)
{
    @* Fail closed: until the authentication state is known (or when no router provides one) nothing of the page is shown. *@
    <p class="ts-gate" role="status">@GateCopy.Checking</p>
}
else if (_authenticated == false || IsStaticPage)
{
    @* A visitor known to be anonymous: only the anonymous pages (error, not found) get this far, the router blocks every other page.
       A static page (no circuit) renders its content directly for a signed-in user too: the Retry button below could not do anything there. *@
    @ChildContent
}
else
{
    @if (Session.ExpiredWhileWorking)
    {
        @* Mid-session 401: keep the page mounted (an unsent reply or note survives) and say so. A 401 on the first load stays the full page below. *@
        <SessionExpiredBanner />
    }

    @if (Session.IsAdmitted)
    {
        @* One place for the content, whether the session is Ready or has expired while working, so Blazor keeps the same component instances across the change. *@
        @ChildContent
    }
    else
    {
        switch (Session.State)
        {
            case AgentSessionState.NoAccess:
                <NoAccessPage Code="@Session.ErrorCode" Message="@Session.ErrorMessage" />
                break;
            case AgentSessionState.SessionExpired:
                <section class="ts-gate" role="alert">
                    <h1>@GateCopy.SessionExpiredTitle</h1>
                    <p>@GateCopy.SessionExpiredBody</p>
                    <form method="get" action="@AdminAuthentication.SignInStartPath">
                        <button type="submit">@GateCopy.SignInAgain</button>
                    </form>
                </section>
                break;
            case AgentSessionState.Unavailable:
                <section class="ts-gate" role="alert">
                    <h1>@GateCopy.UnavailableTitle</h1>
                    <p>@Session.ErrorMessage</p>
                    <button type="button" @onclick="RetryAsync">@UiCopy.RetryLabel</button>
                </section>
                break;
            default:
                <p class="ts-gate" role="status">@GateCopy.Checking</p>
                break;
        }
    }
}
```

```diff
--- a/src/TechStrap.Admin/Components/Layout/AgentGate.razor.cs
+++ b/src/TechStrap.Admin/Components/Layout/AgentGate.razor.cs
@@ -2,6 +2,7 @@
 using Microsoft.AspNetCore.Components.Authorization;
 using Microsoft.AspNetCore.Components.Routing;
 using Microsoft.AspNetCore.Http;
+using Microsoft.Extensions.Logging;
 using TechStrap.Admin.Auth;
 
 namespace TechStrap.Admin.Components.Layout;
@@ -18,6 +19,9 @@
 
     [Inject]
     private AgentSession Session { get; set; } = default!;
+
+    [Inject]
+    private ILogger<AgentGate> Logger { get; set; } = default!;
 
     [CascadingParameter]
     private HttpContext? HttpContext { get; set; }
@@ -47,7 +51,21 @@
         _authenticated = state.User.Identity?.IsAuthenticated == true;
         if (_authenticated == true && !IsStaticPage)
         {
-            await Session.EnsureLoadedAsync(_lifetime.Token);
+            try
+            {
+                await Session.EnsureLoadedAsync(_lifetime.Token);
+            }
+            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
+            {
+                // The circuit or the layout went away while the read was in flight.
+            }
+            catch (Exception ex)
+            {
+                // An unexpected fault (not an API answer, which the session already maps): degrade to the Unavailable state with its Retry button. Only the type
+                // is logged, never the message, which can carry an address or a name.
+                Logger.LogWarning("The agent session could not be loaded ({ExceptionType}).", ex.GetType().Name);
+                Session.MarkUnavailable();
+            }
         }
     }
 
```

`src/TechStrap.Admin/Components/Layout/SessionExpiredBanner.razor`:

```razor
@* Shown over a page that stays mounted (PHASE-07c): the agent's session ended while they were working. Nothing on the page is torn down, so an unsent
   reply or note survives; "Sign in again" is a plain link, so it does not depend on the circuit, and it brings the agent back to this page. *@
<div class="ts-session-banner" role="alert">
    <p><strong>@GateCopy.SessionLapsedTitle</strong> @GateCopy.SessionLapsedBody</p>
    <a class="ts-session-banner-action" href="@SignInHref">@GateCopy.SignInAgain</a>
</div>
```

`src/TechStrap.Admin/Components/Layout/SessionExpiredBanner.razor.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using TechStrap.Admin.Auth;

namespace TechStrap.Admin.Components.Layout;

/// <summary>
/// The non-destructive notice <see cref="AgentGate"/> shows when <see cref="AgentSession.ExpiredWhileWorking"/> is true. The link goes to
/// <c>/signin/start?returnUrl=</c> with the current local address (path and query), checked again by <see cref="LocalReturnUrl.Sanitize"/> on the server, so it can
/// never name another host. It follows the agent as they navigate, so signing in again returns them to wherever they are.
/// </summary>
public sealed partial class SessionExpiredBanner : ComponentBase, IDisposable
{
    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private string SignInHref
    {
        get
        {
            var local = LocalReturnUrl.Sanitize("/" + Navigation.ToBaseRelativePath(Navigation.Uri));
            return $"{AdminAuthentication.SignInStartPath}?{AdminAuthentication.ReturnUrlParameter}={Uri.EscapeDataString(local)}";
        }
    }

    protected override void OnInitialized() => Navigation.LocationChanged += OnLocationChanged;

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => Navigation.LocationChanged -= OnLocationChanged;
}
```

```diff
--- a/src/TechStrap.Admin/Components/Layout/GateCopy.cs
+++ b/src/TechStrap.Admin/Components/Layout/GateCopy.cs
@@ -6,6 +6,8 @@
     public const string Checking = "Checking your access...";
     public const string SessionExpiredTitle = "Your session has expired.";
     public const string SessionExpiredBody = "Sign in again to carry on. Nothing you were typing was sent.";
+    public const string SessionLapsedTitle = "Your session has expired.";
+    public const string SessionLapsedBody = "What you have typed on this page is still here. Sign in again to carry on; changes you make before then are not saved.";
     public const string SignInAgain = "Sign in again";
     public const string UnavailableTitle = "TechStrap could not check your access.";
 }
```

5. The rail and `AdminOnly` follow `IsAdmitted`:

```diff
--- a/src/TechStrap.Admin/Components/Layout/NavMenu.razor
+++ b/src/TechStrap.Admin/Components/Layout/NavMenu.razor
@@ -3,7 +3,7 @@
         <img src="brand/mark.svg" alt="" width="36" height="36" />
         <span>TechStrap<small>Call log / admin</small></span>
     </a>
-    @if (Session.State == AgentSessionState.Ready)
+    @if (Session.IsAdmitted)
     {
         <NavLink class="ts-rail-link" href="/queue" Match="NavLinkMatch.Prefix">@ShellCopy.QueueLink</NavLink>
         @if (Session.IsAdmin)
```

```diff
--- a/src/TechStrap.Admin/Components/Ui/AdminOnly.razor
+++ b/src/TechStrap.Admin/Components/Ui/AdminOnly.razor
@@ -1,4 +1,4 @@
-@if (Session.State == AgentSessionState.Ready)
+@if (Session.IsAdmitted)
 {
     if (Session.IsAdmin)
     {
```

6. The banner style (brand tokens only, so both themes keep their contrast):

```diff
--- a/src/TechStrap.Admin/Styles/_shell.scss
+++ b/src/TechStrap.Admin/Styles/_shell.scss
@@ -123,3 +123,38 @@
     border-bottom: 3px solid transparent;
   }
 }
+
+// The notice over a page that stays mounted after the session expired (AgentGate, PHASE-07c). Plain and calm, like every state that blocks work (BRAND.md section 3);
+// brand tokens only, so both themes keep their contrast. The action is a link, at least 24px tall (WCAG 2.2 target size).
+.ts-session-banner {
+  display: flex;
+  flex-wrap: wrap;
+  gap: 8px 16px;
+  align-items: center;
+  justify-content: space-between;
+  padding: 10px 16px;
+  margin: 0 0 12px;
+  color: var(--ink);
+  background: var(--sheet);
+  border: 2px solid var(--st-spam);
+  box-shadow: 3px 3px 0 var(--shadow);
+
+  p {
+    flex: 1 1 280px;
+    margin: 0;
+  }
+}
+
+.ts-session-banner-action {
+  display: inline-flex;
+  align-items: center;
+  min-height: 34px;
+  padding: 6px 14px;
+  font: 600 .75rem var(--ts-font-mono);
+  letter-spacing: .05em;
+  color: var(--paper);
+  text-decoration: none;
+  text-transform: uppercase;
+  background: var(--ink);
+  border: 2px solid var(--ink);
+}
```

- [ ] **Step 4: Cap Retry-After, delete the dead catch, harden the rail and the layout**

```diff
--- a/src/TechStrap.Admin/Clients/ApiClientRegistration.cs
+++ b/src/TechStrap.Admin/Clients/ApiClientRegistration.cs
@@ -16,6 +16,12 @@
 
     /// <summary>The write client allows a multipart reply with attachments at least this long, whatever the configured read timeout.</summary>
     public const int WriteTimeoutFloorSeconds = 300;
+
+    /// <summary>
+    /// The longest a read waits before a retry, whatever the API's <c>Retry-After</c> asks for. The resilience default honours the header with no limit, so an
+    /// overloaded API that says "120" would freeze a page on "Loading" until the client timeout (30 s). The header is still honoured below this cap.
+    /// </summary>
+    public static readonly TimeSpan ReadRetryAfterCap = TimeSpan.FromSeconds(2);
 
     private static readonly TimeSpan ReadRetryBaseDelay = TimeSpan.FromMilliseconds(250);
 
@@ -48,6 +54,7 @@
             .AddForwardedClientIp()
             .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = ConnectionLifetime });
 
+        services.AddScoped<SessionExpiry>();
         services.AddScoped<ApiConnection>();
         services.AddScoped<IAgentsClient, AgentsClient>();
         services.AddScoped<IProductsClient, ProductsClient>();
@@ -67,6 +74,10 @@
         Delay = ReadRetryBaseDelay,
         BackoffType = DelayBackoffType.Exponential,
         UseJitter = true,
+
+        // The default honours Retry-After without a limit; this one honours it up to ReadRetryAfterCap and otherwise falls back to the backoff above.
+        ShouldRetryAfterHeader = false,
+        DelayGenerator = args => ValueTask.FromResult(RetryAfterDelay(args.Outcome.Result, TimeProvider.System.GetUtcNow())),
         ShouldHandle = args => ValueTask.FromResult(args.Outcome switch
         {
             { Exception: HttpRequestException or TimeoutException } => true,
@@ -76,6 +87,22 @@
         }),
     };
 
+    /// <summary>
+    /// The wait the API asked for in <c>Retry-After</c> (seconds or an HTTP date), capped at <see cref="ReadRetryAfterCap"/>; null when there is no usable header, so the
+    /// exponential backoff applies. A date in the past asks for no wait beyond the backoff.
+    /// </summary>
+    internal static TimeSpan? RetryAfterDelay(HttpResponseMessage? response, DateTimeOffset now)
+    {
+        var retryAfter = response?.Headers.RetryAfter;
+        TimeSpan? requested = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - now : null);
+        if (requested is not { } wait || wait <= TimeSpan.Zero)
+        {
+            return null;
+        }
+
+        return wait < ReadRetryAfterCap ? wait : ReadRetryAfterCap;
+    }
+
     private static void ConfigureClient(IServiceProvider services, HttpClient client, int minimumTimeoutSeconds)
     {
         var api = services.GetRequiredService<IOptions<ApiOptions>>().Value;
```

```diff
--- a/src/TechStrap.Admin/Clients/AttachmentPassThrough.cs
+++ b/src/TechStrap.Admin/Clients/AttachmentPassThrough.cs
@@ -28,7 +28,7 @@
         {
             upstream = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
         }
-        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or Polly.CircuitBreaker.BrokenCircuitException
+        catch (Exception ex) when (ex is HttpRequestException or TimeoutException
                                        || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
         {
             http.Response.StatusCode = StatusCodes.Status502BadGateway;
```

```diff
--- a/src/TechStrap.Admin/Components/Layout/NavMenu.razor.cs
+++ b/src/TechStrap.Admin/Components/Layout/NavMenu.razor.cs
@@ -1,4 +1,5 @@
 using Microsoft.AspNetCore.Components;
+using Microsoft.Extensions.Logging;
 using TechStrap.Admin.Auth;
 using TechStrap.Admin.Components.Ui;
 using TechStrap.Admin.Features.Shell;
@@ -20,6 +21,9 @@
 
     [Inject]
     private AgentSession Session { get; set; } = default!;
+
+    [Inject]
+    private ILogger<NavMenu> Logger { get; set; } = default!;
 
     [Inject]
     private FailedEmailCounter Failed { get; set; } = default!;
@@ -49,6 +53,12 @@
         {
             // The circuit or the layout went away while the read was in flight.
         }
+        catch (Exception ex)
+        {
+            // The rail sits outside every error boundary, so an exception out of OnAfterRenderAsync would end the circuit. A badge is not worth that: the count stays
+            // as it was and the rail keeps working. Only the type is logged, never the message.
+            Logger.LogWarning("The failed-email badge could not be refreshed ({ExceptionType}).", ex.GetType().Name);
+        }
     }
 
     private void OnChanged()
```

```diff
--- a/src/TechStrap.Admin/Components/Layout/MainLayout.razor.cs
+++ b/src/TechStrap.Admin/Components/Layout/MainLayout.razor.cs
@@ -1,4 +1,5 @@
 using Microsoft.AspNetCore.Components;
+using Microsoft.Extensions.Logging;
 using TechStrap.Admin.Features.Shell;
 
 namespace TechStrap.Admin.Components.Layout;
@@ -15,6 +16,9 @@
 
     [Inject]
     private ShortcutService Shortcuts { get; set; } = default!;
+
+    [Inject]
+    private ILogger<MainLayout> Logger { get; set; } = default!;
 
     [Inject]
     private NavigationManager Navigation { get; set; } = default!;
@@ -49,8 +53,17 @@
         if (firstRender)
         {
             // The stored preferences first: they set the theme and whether single-key shortcuts act. Neither call throws for a script or storage failure.
-            await Preferences.LoadAsync();
-            await Shortcuts.StartAsync();
+            try
+            {
+                await Preferences.LoadAsync();
+                await Shortcuts.StartAsync();
+            }
+            catch (Exception ex) when (ex is not OperationCanceledException)
+            {
+                // The layout sits outside every error boundary, so an exception out of OnAfterRenderAsync would end the circuit. Without the listener the keyboard layer
+                // is off but every page still works; the next start attempt may succeed. Only the type is logged, never the message.
+                Logger.LogWarning("The keyboard shortcuts could not be started ({ExceptionType}).", ex.GetType().Name);
+            }
         }
     }
 
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release`
Expected: PASS. To see the pins guard (do not commit these edits), make each change below, rerun `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "SessionExpiryTests|AgentGateSessionExpiryTests|LayoutResilienceTests|ReadRetryAfterTests|AttachmentPassThroughTests|AgentGateTests|AgentSessionTests"`, expect the failures named, and restore the file with `git checkout -- <file>`. These were run in the scratch copy:

- `ApiConnection.ReportIfUnauthenticated`: replace `expiry.Report();` with `_ = expiry;`. Fails `SessionExpiryTests.A_401_on_a_read_moves_a_ready_session_to_expired_and_keeps_the_agent`, `A_401_on_a_write_expires_the_session_too` (three rows), `A_401_on_a_write_with_a_body_expires_the_session` and `Several_401s_raise_one_change_so_the_banner_does_not_flicker`.
- `ApiClientRegistration.ReadRetry`: replace `ShouldRetryAfterHeader = false,` and the `DelayGenerator` line below it with `ShouldRetryAfterHeader = true,` (the uncapped default). Fails `ReadRetryAfterTests.A_503_that_asks_for_two_minutes_is_retried_after_at_most_the_cap` (it times out: the retries wait for the 120 seconds).
- `ApiClientRegistration.ReadRetryAfterCap`: 2 seconds becomes 20 seconds. Fails `ReadRetryAfterTests.A_503_that_asks_for_two_minutes_is_retried_after_at_most_the_cap` and `A_delay_in_seconds_is_honoured_up_to_the_cap` (the 3 and 120 second rows).
- `AgentGate.razor`: replace `@if (Session.IsAdmitted)` with `@if (Session.State == AgentSessionState.Ready)` (the content leaves its place when the session expires). Fails `AgentGateSessionExpiryTests.A_session_that_expires_while_working_shows_the_banner_and_keeps_the_same_page_mounted` (the page component is disposed and created again).
- `AgentSession.IsAdmin`: replace `IsAdmitted &&` with `State == AgentSessionState.Ready &&` (the rail loses the admin links on expiry). Fails `LayoutResilienceTests.The_rail_and_an_admin_page_stay_complete_when_the_session_expires_while_working`.
- `SessionExpiredBanner.razor.cs`: make `SignInHref` return only `AdminAuthentication.SignInStartPath`. Fails `AgentGateSessionExpiryTests.The_banner_links_to_sign_in_with_the_current_local_address` and `The_banner_link_follows_the_agent_when_they_navigate`.
- `AgentGate.razor.cs`: change `catch (Exception ex)` to `catch (ArgumentOutOfRangeException ex)` (the fault escapes). Fails `AgentGateSessionExpiryTests.An_unexpected_fault_while_loading_shows_unavailable_with_retry_and_logs_only_the_type`.
- `AgentGate.razor.cs`: log the exception itself (`Logger.LogWarning(ex, "The agent session could not be loaded.")`). Fails the same test (the message `secret-detail sam@orbitly.test` reaches the log).
- `NavMenu.razor.cs`: change `catch (Exception ex)` to `catch (ArgumentOutOfRangeException ex)`. Fails `LayoutResilienceTests.A_failing_badge_read_leaves_the_rail_working_and_logs_only_the_type`.
- `MainLayout.razor.cs`: replace the `catch (Exception ex) when (ex is not OperationCanceledException)` with `catch (ArgumentOutOfRangeException ex)`. Fails `LayoutResilienceTests.A_shortcut_listener_that_cannot_start_leaves_the_pages_working_and_logs_only_the_type`.
- `AttachmentPassThrough.cs`: add `or Polly.CircuitBreaker.BrokenCircuitException` back to the catch. Fails `AttachmentPassThroughTests.The_pass_through_does_not_name_a_circuit_breaker_the_read_client_cannot_throw`.

- [ ] **Step 6: Whole-project check**

Run: `dotnet build TechStrap.slnx -c Release` (0 warnings), then `dotnet test --project tests/TechStrap.Admin.Tests -c Release` and `dotnet test --project tests/TechStrap.Architecture.Tests -c Release`
Expected: PASS. The host and leak tests in `TechStrap.Api.Tests` run in Task 3's whole-project check.

- [ ] **Step 7: Commit**

```bash
git add src/TechStrap.Admin tests/TechStrap.Admin.Tests tests/TechStrap.Api.Tests/AdminLeakTests.cs
git diff --cached --stat
git commit -m "feat(admin): mid-session 401 banner, Retry-After cap and hardened shell (P07c)" -m "ApiConnection reports every 401 to a scoped SessionExpiry, so AgentSession moves to SessionExpired in one place and keeps the agent: the gate shows a banner over the same mounted page and an unsent reply survives. A 401 on the first load stays the full page. The read client honours Retry-After up to 2 s. The dead BrokenCircuitException catch is removed. NavMenu, MainLayout and AgentGate log the exception type only and degrade instead of ending the circuit." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```

### Task 3: Shared host wiring in TechStrap.Hosting (Admin and Portal adopt it; all four hosts drop the HttpClient logging)

**Review Focus pin:**
- **(4)** A secret leaks through `HttpClient` logging in any host. The `IHttpClientFactory` default logging puts each request header into the structured log state at Trace: the formatted text shows `x-api-key: *`, but the state carries the value as a string array, which a Serilog sink stores as a property. Pinned by `FactoryClientLeakTests` (a client sends a secret header from each of the four hosts through the real factory, a local listener proves the request went out, and the whole Verbose log is scanned; its first test is the negative control, a plain factory, which does leak) and `HostWiringTests` (the handler chain of every host has no logging handler, and a plain factory does). `OtlpLeakTests` and `AdminLeakTests.An_OTLP_header_secret_appears_in_no_log_event_even_at_Verbose` are the end-to-end guard for an OTLP header, with a positive control that an export was attempted.
- **Finding, recorded for the owner:** with the current exporter packages the OTLP exporter's calls do not show up in the host log even with the default removed, so the 07b claim that the exporter goes through the factory is not reproduced. The default protects every other factory client (the two API clients, any future one), and `FactoryClientLeakTests` is what proves it.
- **(1)** and the download sandbox. Adding the shared security headers to the Admin would have silently dropped `Content-Security-Policy: sandbox` from attachment downloads: the package middleware sets the CSP when the response starts and overwrites what the endpoint set. Pinned by `SecurityHeadersHostTests.The_Admin_attachment_download_is_sandboxed_on_top_of_the_page_policy` and `AttachmentPassThroughTests.The_file_is_streamed_for_the_signed_in_agent_as_a_forced_download`.

**Files:**
- Create: `src/TechStrap.Hosting/Wiring/HttpClientDefaults.cs`, `SecurityHeadersExtensions.cs`, `BrowserHostExtensions.cs`, `ObservabilityExtensions.cs`
- Modify: `src/TechStrap.Hosting/TechStrap.Hosting.csproj` (the `SyntaxCircus.AspNetCore.Common` package)
- Modify: `src/TechStrap.Admin/Program.cs`, `src/TechStrap.Admin/Clients/AttachmentPassThrough.cs` (a `Prefix` constant)
- Modify: `src/TechStrap.Portal/Program.cs`, `src/TechStrap.Portal/TechStrap.Portal.csproj` (the Hosting reference)
- Modify: `src/TechStrap.Api/Program.cs`, `src/TechStrap.Worker/Program.cs`
- Modify: `tests/TechStrap.Architecture.Tests/ProjectReferenceDirectionTests.cs`
- Modify: `scripts/tests/Dockerfiles.Tests.ps1` (the Portal image now also needs the Hosting project; the Dockerfiles copy the whole tree, so only the expected list changes)
- Create: `tests/TechStrap.Api.Tests/Hosting/HostWiringTests.cs`, `FactoryClientLeakTests.cs`, `SecurityHeadersHostTests.cs`, `OtlpLeakTests.cs`, `OtlpProbe.cs`
- Modify: `tests/TechStrap.Api.Tests/HostFactory.cs` (the Portal factory takes settings), `tests/TechStrap.Api.Tests/AdminLeakTests.cs` (the OTLP test gets the positive control), `tests/TechStrap.Admin.Tests/AttachmentPassThroughTests.cs`

**Interfaces:**
- Consumes: `SyntaxCircus.AspNetCore.Common` (`AddCorrelationId`, `UseCorrelationId`, `AddTrustedProxyForwardedHeaders`, `AddSecurityHeaders`, `UseSecurityHeaders`), `SyntaxCircus.AspNetCore.Serilog` (`AddStandardSerilog`), `SyntaxCircus.Observability` (`AddSyntaxCircusObservability`), the existing `PiiRedactionEnricher` and `AddSensitiveHeaderScrubbing`, and the test hosts `ApiFactory`, `WorkerFactory`, `AdminFactory`, `PortalFactory` (each exposes `LogSink`).
- Produces (names are fixed; Task 4 and Task 9 use them):

```csharp
namespace TechStrap.Hosting.Wiring;

public static class HttpClientDefaults
{
    public static IServiceCollection AddTechStrapHttpClientDefaults(this IServiceCollection services);   // ConfigureHttpClientDefaults(RemoveAllLoggers); all four hosts
}

public static class BrowserHostExtensions                       // Admin and Portal
{
    public const string NotFoundPath = "/not-found";
    public const string ErrorPath = "/error";
    public static IServiceCollection AddTechStrapWebHost(this IServiceCollection services, IConfiguration configuration, string? contentSecurityPolicy = null);
    public static WebApplication UseTechStrapWebHost(this WebApplication app, params string[] downloadPathPrefixes);   // forwarded headers, correlation id, [download sandbox], security headers
    public static WebApplication UseTechStrapErrorPages(this WebApplication app);                                      // /error outside Development, re-executed 404, only 404
}

public static class SecurityHeadersExtensions
{
    public static IServiceCollection AddTechStrapSecurityHeaders(this IServiceCollection services, IConfiguration configuration, string? contentSecurityPolicy = null);
}

public static class ObservabilityExtensions                     // Admin and Portal
{
    public static SyntaxCircusObservabilityRegistration AddTechStrapObservability(this WebApplicationBuilder builder, string serviceName);
}
```

**Rules:**
1. **What is shared, and what is not.** Shared today and now in Hosting: the correlation id, health checks, trusted-proxy forwarded headers, the data-protection key ring, the security headers, the status-page re-execute with the "only 404" filter, the Serilog and Sentry wiring (with `PiiRedactionEnricher` and the header scrubber), and the `HttpClient` logging default. Not shared: token forwarding, sign-in, the Admin options and anything that needs the Admin's packages. The Portal is still a placeholder; it takes the shared wiring and nothing else.
2. **The `HttpClient` default is host-wide, in all four hosts.** The exporters' clients are not named in app code, so it cannot be done per client. The Api and the Worker call `AddTechStrapHttpClientDefaults()` directly; the Admin and Portal get it from `AddTechStrapWebHost`. Only the logging handlers are removed: the auth, forwarded-IP and resilience handlers are untouched. The per-client `RemoveAllLoggers()` calls in `ApiClientRegistration` stay (they are harmless and keep the clients safe if the default is ever dropped).
3. **The order is part of the contract.** `UseTechStrapWebHost` runs forwarded headers, correlation id, the download sandbox and then the security headers; `UseTechStrapErrorPages` follows it. The security headers sit before the error pages, so the re-executed 404 and the error page carry them. The auth middleware stays after both, in the host.
4. **The package replaces the CSP at response start.** `SecurityHeadersOptions` is init-only and the middleware sets the CSP in an `OnStarting` callback, so (a) a CSP is supplied by layering `SecurityHeaders:ContentSecurityPolicy` over the configuration the package binds, and (b) a download path needs its own start callback registered before the package's, which runs after it (callbacks run last-registered-first) and appends `sandbox`. `UseTechStrapWebHost("/attachments")` does that for the Admin; the endpoint's own `Content-Security-Policy: sandbox` in `AttachmentPassThrough` stays as a second layer.
5. **The leak tests are never vacuous.** `FactoryClientLeakTests` sends a request with a secret header through the host's real `IHttpClientFactory` to an `OtlpProbe` (a TCP listener that accepts and never answers), asserts the probe saw the connection, asserts Debug events were captured (Verbose really took effect), and scans every event, structured properties included. Its first test is the negative control: the same request through a plain factory leaks. The OTLP tests enable the exporter with a header secret, wait while the host is still running for the exporter to connect (a flush during shutdown runs after the logger is gone and would log nothing), then dispose and scan. They set process environment variables, so they run in `ProcessEnvironmentCollection`.
6. **The Hosting package list changes by one.** `Hosting` now declares `SyntaxCircus.AspNetCore.Common`; the architecture test that pins the list is updated, and the Portal's project references are now pinned to exactly Contracts and Hosting.
7. **Encoding.** All new strings are ASCII; run `grep -nP "[^\x00-\x7F]"` over the new files afterwards.

- [ ] **Step 1: Write the failing tests**

1. `tests/TechStrap.Api.Tests/Hosting/OtlpProbe.cs` (test support, a TCP listener that counts connections):

```csharp
using System.Net;
using System.Net.Sockets;

namespace TechStrap.Api.Tests.Hosting;

/// <summary>
/// A local TCP listener that stands in for an OTLP collector. It never answers; it only counts connections. A leak test points a host's OTLP exporter at
/// <see cref="Endpoint"/> and waits on <see cref="WaitForConnectionAsync"/>: that is the positive control proving an export was really attempted through the
/// <c>HttpClient</c> factory, so "the secret is in no log line" cannot pass just because the exporter never ran.
/// </summary>
public sealed class OtlpProbe : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<TcpClient> _clients = [];
    private readonly Task _accepting;
    private int _connections;

    public OtlpProbe()
    {
        _listener.Start();
        Endpoint = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
        _accepting = AcceptAsync();
    }

    /// <summary>The URL to hand to <c>OpenTelemetry:OtlpEndpoint</c>.</summary>
    public string Endpoint { get; }

    /// <summary>How many connections the exporter has opened so far.</summary>
    public int Connections => Volatile.Read(ref _connections);

    /// <summary>Waits until at least one connection arrived; false when none did within <paramref name="timeout"/>.</summary>
    public async Task<bool> WaitForConnectionAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (Connections == 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50, cancellationToken);
        }

        return Connections > 0;
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                lock (_clients)
                {
                    _clients.Add(client);
                }

                Interlocked.Increment(ref _connections);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // The probe was disposed.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _accepting;
        lock (_clients)
        {
            foreach (var client in _clients)
            {
                client.Dispose();
            }
        }

        _stop.Dispose();
    }
}
```

2. `tests/TechStrap.Api.Tests/Hosting/OtlpLeakTests.cs` (the Api and the Portal):

```csharp
using Serilog.Events;

namespace TechStrap.Api.Tests.Hosting;

/// <summary>
/// An OTLP exporter configured with a header such as <c>x-api-key</c> must never put that secret in a log. Each test enables OTLP with a header secret, points the exporter at a
/// local listener (<see cref="OtlpProbe"/>) and waits, while the host is still running, for the exporter to connect: that positive control proves an export was really attempted,
/// so "the secret is in no log event" cannot pass just because nothing was ever sent. Then the host is disposed and every event, at every level, is scanned.
/// With the current exporter packages the export does not go through <c>IHttpClientFactory</c> (removing the default logging does not make these tests fail; checked), so the
/// test that proves the factory default itself is <see cref="FactoryClientLeakTests"/>. These tests stay as the end-to-end guard: if an upgrade moves the exporter onto the factory,
/// or any other path starts logging the header, they fail.
/// </summary>
/// <remarks>The tests set process environment variables (the exporter options bind before host settings exist), so the class runs in the non-parallel <see cref="ProcessEnvironmentCollection"/>.</remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class OtlpLeakTests
{
    internal const string Secret = "otlp-secret-0123456789abcdef0123456789abcdef";

    internal static readonly IReadOnlyDictionary<string, string?> VerboseLogging = new Dictionary<string, string?>
    {
        ["Serilog:MinimumLevel:Default"] = "Verbose",
        ["Serilog:MinimumLevel:Override:Microsoft"] = "Verbose",
        ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore"] = "Verbose",
        ["Serilog:MinimumLevel:Override:System"] = "Verbose",
    };

    private static string Everything(LogEvent e) => string.Join('\n', [e.RenderMessage(), e.Exception?.ToString() ?? string.Empty, .. e.Properties.Values.Select(v => v.ToString())]);

    /// <summary>Sets the OTLP environment variables for one host start and clears them afterwards. The exporter reads them while Program.cs builds the host.</summary>
    internal static async Task WithOtlpAsync(OtlpProbe probe, Func<Task> run)
    {
        var variables = new Dictionary<string, string>
        {
            ["OpenTelemetry__Enabled"] = "true",
            ["OpenTelemetry__OtlpEndpoint"] = probe.Endpoint,
            ["OpenTelemetry__OtlpProtocol"] = "http/protobuf",
            ["OpenTelemetry__Headers"] = $"x-api-key={Secret}",

            // The batch exporter sends every five seconds by default; this makes the first export happen while the test is still running.
            ["OTEL_BSP_SCHEDULE_DELAY"] = "250",
        };
        foreach (var (key, value) in variables)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        try
        {
            await run();
        }
        finally
        {
            foreach (var key in variables.Keys)
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }
    }

    /// <summary>
    /// Waits, while the host is still running, until the exporter has connected to the probe, then gives the HttpClient pipeline a moment to write its log events. The wait must
    /// happen before the host is disposed: a flush during shutdown runs after the logger is gone, so it would log nothing and the leak scan below could never fail.
    /// </summary>
    internal static async Task WaitForExportAsync(OtlpProbe probe, CancellationToken cancellationToken)
    {
        (await probe.WaitForConnectionAsync(TimeSpan.FromSeconds(20), cancellationToken))
            .ShouldBeTrue("the host never connected to the OTLP endpoint, so this test would pass without exercising the exporter's HttpClient");
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
    }

    /// <summary>The checks every host shares, run after the host was disposed.</summary>
    internal static async Task AssertNoLeakAsync(OtlpProbe probe, CollectingSink sink, CancellationToken cancellationToken)
    {
        (await probe.WaitForConnectionAsync(TimeSpan.FromSeconds(20), cancellationToken))
            .ShouldBeTrue("the host never connected to the OTLP endpoint, so this test would pass without exercising the exporter's HttpClient");
        sink.Events.ShouldContain(e => e.Level <= LogEventLevel.Debug, "the Verbose setting must have taken effect, or the scan only saw Information and above");
        sink.Events.Select(Everything).ShouldAllBe(text => !text.Contains(Secret));
    }

    [Fact]
    public async Task An_Api_OTLP_header_secret_appears_in_no_log_event_even_at_Verbose()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var probe = new OtlpProbe();
        CollectingSink sink = null!;
        await WithOtlpAsync(probe, async () =>
        {
            var factory = new ApiFactory(settings: VerboseLogging);
            sink = factory.LogSink;
            try
            {
                using var client = factory.CreateClient();
                (await client.GetStringAsync("/health/live", ct)).ShouldNotContain(Secret);
                await WaitForExportAsync(probe, ct);
            }
            finally
            {
                await factory.DisposeAsync();
            }
        });

        await AssertNoLeakAsync(probe, sink, ct);
    }

    [Fact]
    public async Task A_Portal_OTLP_header_secret_appears_in_no_log_event_even_at_Verbose()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var probe = new OtlpProbe();
        CollectingSink sink = null!;
        await WithOtlpAsync(probe, async () =>
        {
            var factory = new PortalFactory(settings: VerboseLogging);
            sink = factory.LogSink;
            try
            {
                using var client = factory.CreateClient();
                (await client.GetStringAsync("/", ct)).ShouldNotContain(Secret);
                await WaitForExportAsync(probe, ct);
            }
            finally
            {
                await factory.DisposeAsync();
            }
        });

        await AssertNoLeakAsync(probe, sink, ct);
    }
}
```

3. `tests/TechStrap.Api.Tests/Hosting/FactoryClientLeakTests.cs` (the direct proof, with its negative control):

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace TechStrap.Api.Tests.Hosting;

/// <summary>
/// The direct proof behind <c>AddTechStrapHttpClientDefaults</c>: a client the factory creates, sending a header that carries a secret, never has that header written to a
/// log, at any level, in any host. The request really goes out (a local listener sees the connection), so the test cannot pass because nothing was sent. The first test is the
/// negative control: the same request through a plain factory with the default logging does put the header value into the log state at Trace (the formatted text shows
/// <c>x-api-key: *</c>, but the structured state carries the value as a string array), so the check is capable of failing.
/// </summary>
public sealed class FactoryClientLeakTests
{
    private const string Secret = "factory-secret-0123456789abcdef0123456789abcdef";

    private static string Everything(LogEvent e) => string.Join('\n', [e.RenderMessage(), e.Exception?.ToString() ?? string.Empty, .. e.Properties.Values.Select(v => v.ToString())]);

    /// <summary>Sends one request with the secret header to the probe, which accepts the connection and never answers, so the request is abandoned after a moment.</summary>
    private static async Task SendAsync(IHttpClientFactory factory, OtlpProbe probe, CancellationToken cancellationToken)
    {
        var client = factory.CreateClient("secret-header-probe");
        client.DefaultRequestHeaders.Add("x-api-key", Secret);
        using var request = new HttpRequestMessage(HttpMethod.Post, probe.Endpoint) { Content = new ByteArrayContent([1, 2, 3]) };
        using var abandon = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        abandon.CancelAfter(TimeSpan.FromSeconds(2));
        await Should.ThrowAsync<OperationCanceledException>(() => client.SendAsync(request, abandon.Token));
        (await probe.WaitForConnectionAsync(TimeSpan.FromSeconds(10), cancellationToken)).ShouldBeTrue("nothing connected, so no request was sent and this test proves nothing");
    }

    [Fact]
    public async Task Control_a_plain_factory_with_the_default_logging_puts_the_header_value_in_the_log_state_at_Trace()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var probe = new OtlpProbe();
        var lines = new List<string>();
        var services = new ServiceCollection()
            .AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(new LineProvider(lines)))
            .AddHttpClient("secret-header-probe").Services
            .BuildServiceProvider();

        await SendAsync(services.GetRequiredService<IHttpClientFactory>(), probe, ct);

        lock (lines)
        {
            lines.ShouldContain(line => line.Contains(Secret, StringComparison.Ordinal), "the factory's default logging must leak here, or the host checks below cannot fail");
        }
    }

    [Fact]
    public async Task The_Api_never_logs_a_factory_clients_header()
    {
        await using var factory = new ApiFactory(settings: OtlpLeakTests.VerboseLogging);
        await AssertNoLeakAsync(factory.Services, factory.LogSink);
    }

    [Fact]
    public async Task The_Worker_never_logs_a_factory_clients_header()
    {
        await using var factory = new WorkerFactory(settings: OtlpLeakTests.VerboseLogging);
        await AssertNoLeakAsync(factory.Services, factory.LogSink);
    }

    [Fact]
    public async Task The_Admin_never_logs_a_factory_clients_header()
    {
        await using var factory = new AdminFactory(settings: OtlpLeakTests.VerboseLogging);
        await AssertNoLeakAsync(factory.Services, factory.LogSink);
    }

    [Fact]
    public async Task The_Portal_never_logs_a_factory_clients_header()
    {
        await using var factory = new PortalFactory(settings: OtlpLeakTests.VerboseLogging);
        await AssertNoLeakAsync(factory.Services, factory.LogSink);
    }

    private static async Task AssertNoLeakAsync(IServiceProvider services, CollectingSink sink)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var probe = new OtlpProbe();

        await SendAsync(services.GetRequiredService<IHttpClientFactory>(), probe, ct);

        sink.Events.ShouldContain(e => e.Level <= LogEventLevel.Debug, "the Verbose setting must have taken effect, or the scan only saw Information and above");
        sink.Events.Select(Everything).ShouldAllBe(text => !text.Contains(Secret));
    }

    /// <summary>Records every line a logger writes, including the structured state's own text (where the factory puts the header values).</summary>
    private sealed class LineProvider(List<string> lines) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Recorder(lines);

        public void Dispose()
        {
        }

        private sealed class Recorder(List<string> lines) : ILogger
        {
            // The factory writes the header values as a string array in the structured state ("x-api-key" = [value]), so the array's contents must be read, not its type name.
            private static string Text(object? value) => value switch
            {
                string text => text,
                System.Collections.IEnumerable items => string.Join(',', items.Cast<object?>().Select(Text)),
                _ => value?.ToString() ?? string.Empty,
            };

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(' ', pairs.Select(p => $"{p.Key}={Text(p.Value)}")) : string.Empty;
                lock (lines)
                {
                    lines.Add($"{logLevel} {formatter(state, exception)} {values} {exception}");
                }
            }
        }
    }
}
```

4. `tests/TechStrap.Api.Tests/Hosting/HostWiringTests.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentry;
using Sentry.AspNetCore;
using TechStrap.Hosting.Sentry;
using TechStrap.Hosting.Wiring;

namespace TechStrap.Api.Tests.Hosting;

/// <summary>
/// The shared host wiring in TechStrap.Hosting (PHASE-07c): every host drops the default <c>HttpClient</c> logging, and the two browser hosts get the same redaction
/// and Sentry scrubbing, so the Portal no longer lags the Admin.
/// </summary>
public sealed class HostWiringTests
{
    private const string Token = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE";   // exactly 43 base64url characters

    /// <summary>The handlers the factory puts in front of the transport for any client name, outermost first.</summary>
    private static IReadOnlyList<string> HandlerChain(IServiceProvider services)
    {
        var names = new List<string>();
        for (HttpMessageHandler? handler = services.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler("any-client-at-all");
             handler is not null;
             handler = (handler as DelegatingHandler)?.InnerHandler)
        {
            names.Add(handler.GetType().Name);
        }

        return names;
    }

    private static bool LoggingSuppressed(IServiceProvider services) =>
        !HandlerChain(services).Any(name => name.StartsWith("Logging", StringComparison.Ordinal));

    [Fact]
    public async Task The_Api_drops_the_default_HttpClient_logging()
    {
        await using var factory = new ApiFactory();

        LoggingSuppressed(factory.Services).ShouldBeTrue();
    }

    [Fact]
    public async Task The_Worker_drops_the_default_HttpClient_logging()
    {
        await using var factory = new WorkerFactory();

        LoggingSuppressed(factory.Services).ShouldBeTrue();
    }

    [Fact]
    public async Task The_Admin_drops_the_default_HttpClient_logging()
    {
        await using var factory = new AdminFactory();

        LoggingSuppressed(factory.Services).ShouldBeTrue();
    }

    [Fact]
    public async Task The_Portal_drops_the_default_HttpClient_logging()
    {
        await using var factory = new PortalFactory();

        LoggingSuppressed(factory.Services).ShouldBeTrue();
    }

    [Fact]
    public void A_host_that_does_not_call_the_default_keeps_the_factory_logging_so_the_check_above_can_fail()
    {
        var services = new ServiceCollection().AddLogging().AddHttpClient("probe").Services.BuildServiceProvider();

        HandlerChain(services).ShouldContain("LoggingHttpMessageHandler");
        LoggingSuppressed(services).ShouldBeFalse();
    }

    [Fact]
    public async Task The_Portal_redacts_what_application_code_logs()
    {
        await using var factory = new PortalFactory();
        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RedactionProbe");

        logger.LogWarning("Probe {Email} {Token}", "ada@example.com", Token);

        var probe = factory.LogSink.Events.Single(e => e.MessageTemplate.Text.StartsWith("Probe ", StringComparison.Ordinal));
        probe.RenderMessage().ShouldBe("Probe \"[email]\" \"[token]\"");
    }

    [Fact]
    public void The_shared_observability_wiring_scrubs_credential_headers_from_Sentry_events_and_transactions()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Sentry:Dsn"] = "https://key@example.invalid/1" });

        builder.AddTechStrapObservability("techstrap-wiring-test");

        using var app = builder.Build();
        var sentry = app.Services.GetRequiredService<IOptions<SentryAspNetCoreOptions>>().Value;
        sentry.GetAllEventProcessors().OfType<SensitiveHeaderSentryProcessor>().ShouldHaveSingleItem();
        sentry.GetAllTransactionProcessors().OfType<SensitiveHeaderSentryProcessor>().ShouldHaveSingleItem();
        sentry.AutoSessionTracking.ShouldBeFalse();
    }
}
```

5. `tests/TechStrap.Api.Tests/Hosting/SecurityHeadersHostTests.cs`:

```csharp
using System.Net;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Api.Tests.Hosting;

/// <summary>
/// The headers every response of the three web hosts carries (PHASE-07c): no sniffing, no framing, a strict referrer, no camera, microphone or location. The
/// Content-Security-Policy itself is pinned in <c>ContentSecurityPolicyHostTests</c>. Each page kind is checked, because the static pages and the re-executed error pages
/// are separate paths through the pipeline.
/// </summary>
public sealed class SecurityHeadersHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static void AssertCommonHeaders(HttpResponseMessage response, string where)
    {
        string Header(string name) => response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : string.Empty;

        Header("X-Content-Type-Options").ShouldBe("nosniff", where);
        Header("X-Frame-Options").ShouldBe("DENY", where);
        Header("Referrer-Policy").ShouldBe("strict-origin-when-cross-origin", where);
        Header("Permissions-Policy").ShouldContain("camera=()", Case.Sensitive, where);
        Header("Permissions-Policy").ShouldContain("microphone=()", Case.Sensitive, where);
        Header("Permissions-Policy").ShouldContain("geolocation=()", Case.Sensitive, where);
        Header("Content-Security-Policy").ShouldContain("frame-ancestors 'none'", Case.Sensitive, where);
    }

    [Theory]
    [InlineData("/signin")]
    [InlineData("/error")]
    public async Task The_Admin_static_pages_carry_the_headers(string path)
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssertCommonHeaders(response, "Admin " + path);
    }

    [Fact]
    public async Task The_Admin_signed_in_page_and_the_re_executed_404_carry_the_headers()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        using var home = await client.GetAsync("/", Ct);
        using var missing = await client.GetAsync("/no-such-page", Ct);

        home.StatusCode.ShouldBe(HttpStatusCode.OK);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        AssertCommonHeaders(home, "Admin /");
        AssertCommonHeaders(missing, "Admin 404");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Fact]
    public async Task The_Admin_attachment_download_is_sandboxed_on_top_of_the_page_policy()
    {
        await using var factory = new AdminFactory();
        var id = Guid.NewGuid();
        factory.Api.On(HttpMethod.Get, $"/api/attachments/{id}", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        using var response = await client.GetAsync($"/attachments/{id}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var policy = response.Headers.GetValues("Content-Security-Policy").Single().Split(';', StringSplitOptions.TrimEntries);
        policy.ShouldContain("sandbox", "a download must never run script in the Admin's origin");
        policy.ShouldContain("frame-ancestors 'none'", "the page policy is still there");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/no-such-page")]
    public async Task The_Portal_pages_carry_the_headers(string path)
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        AssertCommonHeaders(response, "Portal " + path);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/openapi/v1.json")]
    public async Task The_Api_responses_carry_the_headers(string path)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssertCommonHeaders(response, "Api " + path);
    }
}
```

6. The Portal factory takes settings (so a leak test can set Verbose), and the existing Admin OTLP test gets the positive control. In `tests/TechStrap.Api.Tests/HostFactory.cs`:

```diff
-public sealed class PortalFactory(string environment = "Development")
-    : HostFactory<TechStrap.Portal.Program>(environment);
+public sealed class PortalFactory(string environment = "Development", IReadOnlyDictionary<string, string?>? settings = null)
+    : HostFactory<TechStrap.Portal.Program>(environment, settings);
```

In `tests/TechStrap.Api.Tests/AdminLeakTests.cs` replace the last test (from the comment above `OtlpSecret` to the end of the class) with:

```csharp
    // The OTLP exporter makes its HTTP calls through IHttpClientFactory. The factory's default logging handler writes raw header values into structured log state at Trace, which would put an
    // OTLP "x-api-key" in the logs. Every host removes the default logging handler from every factory client (TechStrap.Hosting), so the secret never reaches a log event. The probe is the
    // positive control: the exporter must really have connected, or this test proves nothing. The Api and the Portal have the same test in OtlpLeakTests.
    [Fact]
    public async Task An_OTLP_header_secret_appears_in_no_log_event_even_at_Verbose()
    {
        await using var probe = new Hosting.OtlpProbe();
        CollectingSink sink = null!;
        await Hosting.OtlpLeakTests.WithOtlpAsync(probe, async () =>
        {
            var factory = VerboseFactory();
            sink = factory.LogSink;
            try
            {
                using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);
                (await client.GetStringAsync("/", Ct)).ShouldNotContain(Hosting.OtlpLeakTests.Secret);
                (await client.GetStringAsync("/queue/spam", Ct)).ShouldNotContain(Hosting.OtlpLeakTests.Secret);
                await Hosting.OtlpLeakTests.WaitForExportAsync(probe, Ct);
            }
            finally
            {
                await factory.DisposeAsync();
            }
        });

        await Hosting.OtlpLeakTests.AssertNoLeakAsync(probe, sink, Ct);
    }
}
```

7. The download keeps its sandbox: in `tests/TechStrap.Admin.Tests/AttachmentPassThroughTests.cs`, in `The_file_is_streamed_for_the_signed_in_agent_as_a_forced_download`, after the `X-Content-Type-Options` assertion:

```csharp
        // The shared security headers must not replace this: a download is sandboxed, whatever the page policy is.
        response.Headers.GetValues("Content-Security-Policy").Single().Split(';', StringSplitOptions.TrimEntries).ShouldContain("sandbox");
```

8. The architecture test:

```diff
--- a/tests/TechStrap.Architecture.Tests/ProjectReferenceDirectionTests.cs
+++ b/tests/TechStrap.Architecture.Tests/ProjectReferenceDirectionTests.cs
@@ -43,19 +43,18 @@
     {
         var graph = ProjectGraph.LoadSourceProjects(ProjectGraph.FindRepositoryRoot());
 
-        // Admin signs agents in and logs, so it uses the shared Hosting helpers; Portal joins in PHASE-09. Neither may ever see Application, Infrastructure or Domain.
+        // Both browser hosts share the Hosting wiring (headers, error pages, redaction, forwarded headers). Neither may ever see Application, Infrastructure or Domain.
         graph[ReferenceRules.Admin].ProjectReferences.Order().ShouldBe([ReferenceRules.Contracts, ReferenceRules.Hosting]);
-        graph[ReferenceRules.Portal].ProjectReferences.ShouldContain(ReferenceRules.Contracts);
-        graph[ReferenceRules.Portal].ProjectReferences.Except([ReferenceRules.Contracts, ReferenceRules.Hosting]).ShouldBeEmpty();
+        graph[ReferenceRules.Portal].ProjectReferences.Order().ShouldBe([ReferenceRules.Contracts, ReferenceRules.Hosting]);
     }
 
     [Fact]
-    public void Hosting_references_no_TechStrap_project_and_only_the_logging_and_telemetry_packages()
+    public void Hosting_references_no_TechStrap_project_and_only_the_web_logging_and_telemetry_packages()
     {
         var graph = ProjectGraph.LoadSourceProjects(ProjectGraph.FindRepositoryRoot());
 
         graph[ReferenceRules.Hosting].ProjectReferences.ShouldBeEmpty();
-        graph[ReferenceRules.Hosting].PackageReferences.Order().ShouldBe(["SyntaxCircus.AspNetCore.Serilog", "SyntaxCircus.Observability"]);
+        graph[ReferenceRules.Hosting].PackageReferences.Order().ShouldBe(["SyntaxCircus.AspNetCore.Common", "SyntaxCircus.AspNetCore.Serilog", "SyntaxCircus.Observability"]);
     }
 
     [Fact]
```

9. The script test that pins the Portal's clean-publish project list:

```diff
--- a/scripts/tests/Dockerfiles.Tests.ps1
+++ b/scripts/tests/Dockerfiles.Tests.ps1
@@ -131,7 +131,7 @@
 Describe 'clean publish copy list' {
     It '<Project> copies itself and every project it references, and no other TechStrap project' -ForEach @(
         @{ Project = 'TechStrap.Admin'; Expected = @('TechStrap.Admin', 'TechStrap.Contracts', 'TechStrap.Hosting') }
-        @{ Project = 'TechStrap.Portal'; Expected = @('TechStrap.Contracts', 'TechStrap.Portal') }
+        @{ Project = 'TechStrap.Portal'; Expected = @('TechStrap.Contracts', 'TechStrap.Hosting', 'TechStrap.Portal') }
     ) {
         Get-ProjectReferenceClosure -Project $Project | Should -Be $Expected
     }
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet build tests/TechStrap.Api.Tests -c Release`
Expected: FAIL to build (`AddTechStrapObservability` and the `TechStrap.Hosting.Wiring` namespace do not exist).

- [ ] **Step 3: Add the Hosting wiring**

1. `src/TechStrap.Hosting/TechStrap.Hosting.csproj` declares the package it now uses:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <PackageReference Include="SyntaxCircus.AspNetCore.Common" />
    <PackageReference Include="SyntaxCircus.AspNetCore.Serilog" />
    <PackageReference Include="SyntaxCircus.Observability" />
  </ItemGroup>

  <ItemGroup>
    <!-- LogRedactionTests construct PiiRedactionEnricher through its internal failure seam (a throwing text redactor) to prove redaction fails closed. -->
    <InternalsVisibleTo Include="TechStrap.Api.Tests" />
  </ItemGroup>

</Project>
```

2. `src/TechStrap.Hosting/Wiring/HttpClientDefaults.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace TechStrap.Hosting.Wiring;

public static class HttpClientDefaults
{
    /// <summary>
    /// Removes the default logging handlers from every <c>HttpClient</c> the factory creates, in every host (Api, Worker, Admin, Portal). The factory's default
    /// logging writes each raw request header into structured log state at Trace (event id 102): an <c>Authorization</c> header, or an OTLP exporter's
    /// <c>x-api-key</c>. The exporters use the factory too, so the default must be host-wide, not per client. Only the logging handlers go; the auth,
    /// forwarded-IP and resilience handlers are untouched. The leak tests scan every level (Verbose) and prove an export was attempted, so they cannot pass
    /// vacuously.
    /// </summary>
    public static IServiceCollection AddTechStrapHttpClientDefaults(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.ConfigureHttpClientDefaults(http => http.RemoveAllLoggers());
        return services;
    }
}
```

3. `src/TechStrap.Hosting/Wiring/SecurityHeadersExtensions.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SyntaxCircus.AspNetCore.Common;

namespace TechStrap.Hosting.Wiring;

public static class SecurityHeadersExtensions
{
    /// <summary>The configuration key the package reads its Content-Security-Policy from.</summary>
    internal const string ContentSecurityPolicyKey = "SecurityHeaders:ContentSecurityPolicy";

    /// <summary>
    /// Registers the package's security headers (<c>Referrer-Policy</c>, <c>X-Frame-Options</c>, <c>X-Content-Type-Options</c>, <c>Permissions-Policy</c>,
    /// <c>Strict-Transport-Security</c> and a <c>Content-Security-Policy</c>), bound from the <c>SecurityHeaders</c> section. When
    /// <paramref name="contentSecurityPolicy"/> is given it replaces the policy from configuration: it is layered over the configuration the package binds, because the
    /// options type is init-only. The middleware is added by <see cref="BrowserHostExtensions.UseTechStrapWebHost"/>.
    /// </summary>
    public static IServiceCollection AddTechStrapSecurityHeaders(this IServiceCollection services, IConfiguration configuration, string? contentSecurityPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var source = string.IsNullOrWhiteSpace(contentSecurityPolicy)
            ? configuration
            : new ConfigurationBuilder()
                .AddConfiguration(configuration)
                .AddInMemoryCollection(new Dictionary<string, string?> { [ContentSecurityPolicyKey] = contentSecurityPolicy })
                .Build();
        services.AddSecurityHeaders(source);
        return services;
    }
}
```

4. `src/TechStrap.Hosting/Wiring/BrowserHostExtensions.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SyntaxCircus.AspNetCore.Common;

namespace TechStrap.Hosting.Wiring;

/// <summary>
/// The wiring the two browser hosts (Admin and Portal) share. Each host calls <see cref="AddTechStrapWebHost"/> with its services, <see cref="UseTechStrapWebHost"/> first
/// in its pipeline and <see cref="UseTechStrapErrorPages"/> right after it, so a change to forwarded headers, correlation, security headers or the error pages is made once.
/// Admin-only behaviour (token forwarding, sign-in) stays in the Admin.
/// </summary>
public static class BrowserHostExtensions
{
    /// <summary>The route the 404 re-executes to. Both hosts have a page at it.</summary>
    public const string NotFoundPath = "/not-found";

    /// <summary>The route unhandled exceptions are sent to outside Development.</summary>
    public const string ErrorPath = "/error";

    /// <summary>
    /// Registers what every browser host needs: the correlation id, health checks, trusted-proxy forwarded headers (production fails to start without
    /// configuration), the data-protection key ring (antiforgery and circuit state need a stable key; containers mount a volume at
    /// <c>DataProtection:KeyRingPath</c>), the host-wide <c>HttpClient</c> logging default and the security headers.
    /// </summary>
    /// <param name="contentSecurityPolicy">The Content-Security-Policy to send. Null keeps the package default (which only restricts framing, forms and the base URI).</param>
    public static IServiceCollection AddTechStrapWebHost(this IServiceCollection services, IConfiguration configuration, string? contentSecurityPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddCorrelationId();
        services.AddHealthChecks();
        services.AddTrustedProxyForwardedHeaders(configuration);
        var keyRingPath = configuration["DataProtection:KeyRingPath"];
        if (!string.IsNullOrWhiteSpace(keyRingPath))
        {
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        }

        services.AddTechStrapHttpClientDefaults();
        services.AddTechStrapSecurityHeaders(configuration, contentSecurityPolicy);
        return services;
    }

    /// <summary>
    /// Forwarded headers, then the correlation id, then the security headers. The security headers come before the error pages, so the re-executed 404 page and the
    /// error page carry them too.
    /// </summary>
    /// <param name="downloadPathPrefixes">
    /// Paths that stream a user's file (the Admin's <c>/attachments</c>). Their responses get <c>sandbox</c> appended to the Content-Security-Policy, so a file that is
    /// opened rather than saved cannot run script in the app's origin. The shared security-headers middleware sets the policy when the response starts and would
    /// overwrite a value the endpoint set itself; this step is registered before it, and start callbacks run last-registered-first, so it runs after it and appends.
    /// </param>
    public static WebApplication UseTechStrapWebHost(this WebApplication app, params string[] downloadPathPrefixes)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseForwardedHeaders();
        app.UseCorrelationId();
        if (downloadPathPrefixes.Length > 0)
        {
            var prefixes = downloadPathPrefixes.Select(prefix => new PathString(prefix)).ToArray();
            app.Use(async (context, next) =>
            {
                if (prefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix)))
                {
                    context.Response.OnStarting(() =>
                    {
                        var headers = context.Response.Headers;
                        var existing = headers.ContentSecurityPolicy.ToString();
                        if (!existing.Contains("sandbox", StringComparison.Ordinal))
                        {
                            headers.ContentSecurityPolicy = string.IsNullOrEmpty(existing) ? "sandbox" : $"{existing}; sandbox";
                        }

                        return Task.CompletedTask;
                    });
                }

                await next();
            });
        }

        app.UseSecurityHeaders();
        return app;
    }

    /// <summary>
    /// The plain error page for an unhandled exception (outside Development; BRAND.md section 3), the branded not-found page for a 404 (re-executed, so the 404 status
    /// is kept), and nothing else: humour never covers an error that blocks work, so every other status code keeps its own response.
    /// </summary>
    public static WebApplication UseTechStrapErrorPages(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler(ErrorPath, createScopeForErrors: true);
        }

        app.UseStatusCodePagesWithReExecute(NotFoundPath, createScopeForStatusCodePages: true);
        app.Use(async (context, next) =>
        {
            await next();
            if (context.Response.StatusCode != StatusCodes.Status404NotFound)
            {
                context.Features.Get<IStatusCodePagesFeature>()?.Enabled = false;
            }
        });
        return app;
    }
}
```

5. `src/TechStrap.Hosting/Wiring/ObservabilityExtensions.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Sentry;
using Serilog;
using SyntaxCircus.AspNetCore.Serilog;
using SyntaxCircus.Observability;
using TechStrap.Hosting.Logging;
using TechStrap.Hosting.Sentry;

namespace TechStrap.Hosting.Wiring;

public static class ObservabilityExtensions
{
    /// <summary>
    /// The telemetry, logging and Sentry wiring of a browser host (Admin, Portal): OpenTelemetry, Serilog with <see cref="PiiRedactionEnricher"/> (emails, tokens and
    /// hashes never reach a log), and, when Sentry is enabled, <see cref="SentryOptionsExtensions.AddSensitiveHeaderScrubbing"/>. Health checks and the Blazor
    /// circuit's <c>/_blazor</c> transactions are not sampled. The host calls <c>telemetry.LogStartupWarning(app.Logger)</c> after it builds.
    /// </summary>
    public static SyntaxCircusObservabilityRegistration AddTechStrapObservability(this WebApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var telemetry = builder.AddSyntaxCircusObservability(serviceName);
        builder.AddStandardSerilog(configureEnrichment: logger =>
        {
            telemetry.ConfigureSerilog(logger);
            logger.Enrich.With<PiiRedactionEnricher>();
        });
        if (telemetry.Options.Sentry.IsEnabled)
        {
            builder.WebHost.UseSentry(options =>
            {
                telemetry.ConfigureSentry(options, context =>
                    context.TransactionContext.Name.Contains("/health", StringComparison.OrdinalIgnoreCase)
                        || context.TransactionContext.Name.Contains("/_blazor", StringComparison.OrdinalIgnoreCase) ? 0d : null);
                options.AddSensitiveHeaderScrubbing();
                options.AutoSessionTracking = false;
            });
        }

        return telemetry;
    }
}
```

- [ ] **Step 4: Adopt it in the four hosts**

```diff
--- a/src/TechStrap.Admin/Program.cs
+++ b/src/TechStrap.Admin/Program.cs
@@ -1,19 +1,13 @@
-using Microsoft.AspNetCore.Diagnostics;
-using Microsoft.AspNetCore.DataProtection;
-using Sentry;
 using SyntaxCircus.AspNetCore.Common;
-using SyntaxCircus.AspNetCore.Serilog;
 using SyntaxCircus.Blazor.Auth;
 using SyntaxCircus.DotEnv;
-using SyntaxCircus.Observability;
 using TechStrap.Admin.Auth;
 using TechStrap.Admin.Clients;
 using TechStrap.Admin.Components;
 using TechStrap.Admin.Features.Shell;
 using TechStrap.Admin.Features.Tickets;
 using TechStrap.Admin.Options;
-using TechStrap.Hosting.Logging;
-using TechStrap.Hosting.Sentry;
+using TechStrap.Hosting.Wiring;
 
 const string ServiceName = "techstrap-admin";
 
@@ -25,34 +19,11 @@
     builder.Configuration.AddSyntaxCircusDotEnvFiles(builder.Environment.ContentRootPath);
 }
 
-var telemetry = builder.AddSyntaxCircusObservability(ServiceName);
-builder.AddStandardSerilog(configureEnrichment: logger =>
-{
-    telemetry.ConfigureSerilog(logger);
-    logger.Enrich.With<PiiRedactionEnricher>();
-});
-if (telemetry.Options.Sentry.IsEnabled)
-{
-    builder.WebHost.UseSentry(options =>
-    {
-        telemetry.ConfigureSentry(options, context =>
-            context.TransactionContext.Name.Contains("/health", StringComparison.OrdinalIgnoreCase)
-                || context.TransactionContext.Name.Contains("/_blazor", StringComparison.OrdinalIgnoreCase) ? 0d : null);
-        options.AddSensitiveHeaderScrubbing();
-        options.AutoSessionTracking = false;
-    });
-}
+var telemetry = builder.AddTechStrapObservability(ServiceName);
 
-builder.Services.AddCorrelationId();
-builder.Services.AddHealthChecks();
-// Trust X-Forwarded-* only from the reverse proxy. Production fails to start without configuration.
-builder.Services.AddTrustedProxyForwardedHeaders(builder.Configuration);
-// Antiforgery and circuit state need a stable key ring; containers mount a volume here.
-var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
-if (!string.IsNullOrWhiteSpace(keyRingPath))
-{
-    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
-}
+// Correlation id, health checks, trusted-proxy forwarded headers, the data-protection key ring, the host-wide HttpClient logging default and the security headers,
+// shared with the Portal (TechStrap.Hosting).
+builder.Services.AddTechStrapWebHost(builder.Configuration);
 
 // Required settings are validated when the host starts (not read here), so a missing Auth or Api key stops the start with a clear message.
 builder.Services.AddAdminOptions(builder.Configuration);
@@ -60,9 +31,6 @@
 
 builder.Services.AddAdminAuthentication();
 builder.Services.AddCascadingAuthenticationState();
-// The default HttpClient logging handler writes raw request header values into structured log state at Trace (an OTLP exporter's x-api-key, an Authorization header). No factory client keeps it:
-// this default applies to every client the factory creates, the OTLP exporters' included, and removes only the logging handlers (auth, forwarded-IP and resilience handlers are untouched).
-builder.Services.ConfigureHttpClientDefaults(http => http.RemoveAllLoggers());
 // The named API clients, the typed clients over them and the scoped AgentSession (the layout's AgentGate asks it who is signed in).
 builder.Services.AddTechStrapApiClients();
 builder.Services.AddShell();
@@ -74,25 +42,10 @@
 var app = builder.Build();
 telemetry.LogStartupWarning(app.Logger);
 
-app.UseForwardedHeaders();
-app.UseCorrelationId();
-if (!app.Environment.IsDevelopment())
-{
-    // Plain error page for unhandled exceptions (BRAND.md section 3); the branded window is for the Admin 404 only.
-    app.UseExceptionHandler("/error", createScopeForErrors: true);
-}
-
-// An address that matches no page gets the branded 404 (re-executed, so the 404 status code is kept).
-app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
-// BRAND.md section 3: humour never covers an error that blocks work, so only 404 is re-executed to the not-found page.
-app.Use(async (context, next) =>
-{
-    await next();
-    if (context.Response.StatusCode != StatusCodes.Status404NotFound)
-    {
-        context.Features.Get<IStatusCodePagesFeature>()?.Enabled = false;
-    }
-});
+// Forwarded headers, correlation id and security headers first, then the plain error page (BRAND.md section 3; the branded window is for the Admin 404 only)
+// and the branded 404, which is the only status that is re-executed.
+app.UseTechStrapWebHost(AttachmentPassThrough.Prefix);
+app.UseTechStrapErrorPages();
 // Order matters: the token cache middleware needs the authenticated user and must run before antiforgery (SyntaxCircus.Blazor.Auth).
 app.UseAuthentication();
 app.UseAuthorization();
```

```diff
--- a/src/TechStrap.Admin/Clients/AttachmentPassThrough.cs
+++ b/src/TechStrap.Admin/Clients/AttachmentPassThrough.cs
@@ -12,6 +12,9 @@
 public static class AttachmentPassThrough
 {
     public const string Route = "/attachments/{id:guid}";
+
+    /// <summary>The path every download is under. The host adds <c>sandbox</c> to the Content-Security-Policy of these responses (the shared security headers would overwrite a value set here).</summary>
+    public const string Prefix = "/attachments";
 
     public static IEndpointRouteBuilder MapAttachmentPassThrough(this IEndpointRouteBuilder endpoints)
     {
```

```diff
--- a/src/TechStrap.Portal/Program.cs
+++ b/src/TechStrap.Portal/Program.cs
@@ -1,10 +1,6 @@
-using Microsoft.AspNetCore.Diagnostics;
-using Microsoft.AspNetCore.DataProtection;
-using Sentry;
 using SyntaxCircus.AspNetCore.Common;
-using SyntaxCircus.AspNetCore.Serilog;
 using SyntaxCircus.DotEnv;
-using SyntaxCircus.Observability;
+using TechStrap.Hosting.Wiring;
 using TechStrap.Portal.Components;
 using TechStrap.Portal.Components.Ui;
 
@@ -18,29 +14,11 @@
     builder.Configuration.AddSyntaxCircusDotEnvFiles(builder.Environment.ContentRootPath);
 }
 
-var telemetry = builder.AddSyntaxCircusObservability(ServiceName);
-builder.AddStandardSerilog(configureEnrichment: telemetry.ConfigureSerilog);
-if (telemetry.Options.Sentry.IsEnabled)
-{
-    builder.WebHost.UseSentry(options =>
-    {
-        telemetry.ConfigureSentry(options, context =>
-            context.TransactionContext.Name.Contains("/health", StringComparison.OrdinalIgnoreCase)
-                || context.TransactionContext.Name.Contains("/_blazor", StringComparison.OrdinalIgnoreCase) ? 0d : null);
-        options.AutoSessionTracking = false;
-    });
-}
+var telemetry = builder.AddTechStrapObservability(ServiceName);
 
-builder.Services.AddCorrelationId();
-builder.Services.AddHealthChecks();
-// Trust X-Forwarded-* only from the reverse proxy. Production fails to start without configuration.
-builder.Services.AddTrustedProxyForwardedHeaders(builder.Configuration);
-// Antiforgery and circuit state need a stable key ring; containers mount a volume here.
-var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
-if (!string.IsNullOrWhiteSpace(keyRingPath))
-{
-    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
-}
+// Correlation id, health checks, trusted-proxy forwarded headers, the data-protection key ring, the host-wide HttpClient logging default and the security headers,
+// shared with the Admin (TechStrap.Hosting).
+builder.Services.AddTechStrapWebHost(builder.Configuration);
 
 builder.Services.AddRazorComponents();
 // Installation-wide switch for the "Powered by TechStrap" footer (D-024); shown unless set to false.
@@ -57,25 +35,9 @@
 var app = builder.Build();
 telemetry.LogStartupWarning(app.Logger);
 
-app.UseForwardedHeaders();
-app.UseCorrelationId();
-if (!app.Environment.IsDevelopment())
-{
-    // Plain error page for unhandled exceptions (BRAND.md section 3); the branded window is for the Admin 404 only.
-    app.UseExceptionHandler("/error", createScopeForErrors: true);
-}
-
-// An address that matches no page gets the not-found page (re-executed, so the 404 status code is kept).
-app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
-// BRAND.md section 3: humour never covers an error that blocks work, so only 404 is re-executed to the not-found page.
-app.Use(async (context, next) =>
-{
-    await next();
-    if (context.Response.StatusCode != StatusCodes.Status404NotFound)
-    {
-        context.Features.Get<IStatusCodePagesFeature>()?.Enabled = false;
-    }
-});
+// Forwarded headers, correlation id and security headers first, then the plain error page and the not-found page (only 404 is re-executed).
+app.UseTechStrapWebHost();
+app.UseTechStrapErrorPages();
 app.UseAntiforgery();
 app.MapStandardHealthChecks();
 app.MapRazorComponentsWithStaticAssets<App>();
```

```diff
--- a/src/TechStrap.Portal/TechStrap.Portal.csproj
+++ b/src/TechStrap.Portal/TechStrap.Portal.csproj
@@ -7,6 +7,7 @@
 
   <ItemGroup>
     <ProjectReference Include="../TechStrap.Contracts/TechStrap.Contracts.csproj" />
+    <ProjectReference Include="../TechStrap.Hosting/TechStrap.Hosting.csproj" />
   </ItemGroup>
 
   <ItemGroup>
```

```diff
--- a/src/TechStrap.Api/Program.cs
+++ b/src/TechStrap.Api/Program.cs
@@ -10,6 +10,7 @@
 using TechStrap.Api.Startup;
 using TechStrap.Hosting.Logging;
 using TechStrap.Hosting.Sentry;
+using TechStrap.Hosting.Wiring;
 using TechStrap.Infrastructure.Intake;
 using TechStrap.Infrastructure.Persistence;
 using TechStrap.Infrastructure.Security;
@@ -42,6 +43,8 @@
 }
 
 builder.Services.AddCorrelationId();
+// No HttpClient the factory creates (the OTLP exporters' included) logs its request headers: the default logging writes Authorization and x-api-key at Trace.
+builder.Services.AddTechStrapHttpClientDefaults();
 builder.Services.AddSecurityHeaders(builder.Configuration);
 builder.Services.AddProblemDetailsExceptionHandling();
 builder.Services.AddControllers();
```

```diff
--- a/src/TechStrap.Worker/Program.cs
+++ b/src/TechStrap.Worker/Program.cs
@@ -4,6 +4,7 @@
 using SyntaxCircus.DotEnv;
 using SyntaxCircus.Observability;
 using TechStrap.Hosting.Logging;
+using TechStrap.Hosting.Wiring;
 using TechStrap.Infrastructure.AutoClose;
 using TechStrap.Infrastructure.Email;
 using TechStrap.Infrastructure.Persistence;
@@ -38,6 +39,8 @@
 }
 
 builder.Services.AddCorrelationId();
+// No HttpClient the factory creates (the OTLP exporters' included) logs its request headers: the default logging writes Authorization and x-api-key at Trace.
+builder.Services.AddTechStrapHttpClientDefaults();
 builder.Services.AddTechStrapPersistence();
 builder.Services.AddTechStrapEmail(builder.Configuration);
 builder.Services.AddHostedService<EmailOutboxWorker>();
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter "HostWiringTests|SecurityHeadersHostTests|OtlpLeakTests|AdminLeakTests"` and `dotnet test --project tests/TechStrap.Architecture.Tests -c Release`
Expected: PASS. To see the pins guard (do not commit these edits), make each change below, rerun the first command, expect the failures named, and restore the file with `git checkout -- <file>`. These were run in the scratch copy:

- `BrowserHostExtensions.AddTechStrapWebHost`: delete `services.AddTechStrapHttpClientDefaults();`. Fails `HostWiringTests.The_Admin_drops_the_default_HttpClient_logging`, `The_Portal_drops_the_default_HttpClient_logging`, and `FactoryClientLeakTests.The_Admin_never_logs_a_factory_clients_header` and `The_Portal_never_logs_a_factory_clients_header` (the secret header value is in the log state).
- `src/TechStrap.Api/Program.cs`: delete `builder.Services.AddTechStrapHttpClientDefaults();`. Fails `HostWiringTests.The_Api_drops_the_default_HttpClient_logging` and `FactoryClientLeakTests.The_Api_never_logs_a_factory_clients_header`.
- `src/TechStrap.Worker/Program.cs`: delete the same line. Fails `HostWiringTests.The_Worker_drops_the_default_HttpClient_logging` and `FactoryClientLeakTests.The_Worker_never_logs_a_factory_clients_header`.
- `ObservabilityExtensions.AddTechStrapObservability`: delete `options.AddSensitiveHeaderScrubbing();`. Fails `HostWiringTests.The_shared_observability_wiring_scrubs_credential_headers_from_Sentry_events_and_transactions`.
- `ObservabilityExtensions.AddTechStrapObservability`: delete `logger.Enrich.With<PiiRedactionEnricher>();`. Fails `HostWiringTests.The_Portal_redacts_what_application_code_logs`.
- `BrowserHostExtensions.UseTechStrapWebHost`: delete `app.UseSecurityHeaders();`. Fails `SecurityHeadersHostTests` for the Admin `/signin`, `/error`, `/` and the 404, the Portal `/` and `/no-such-page`, and the download test.
- `src/TechStrap.Admin/Program.cs`: call `app.UseTechStrapWebHost();` without `AttachmentPassThrough.Prefix`. Fails `SecurityHeadersHostTests.The_Admin_attachment_download_is_sandboxed_on_top_of_the_page_policy` (without the step, the package overwrites the sandbox the endpoint set).
- `OtlpLeakTests`: point `OpenTelemetry__OtlpEndpoint` at `"http://127.0.0.1:1/"` instead of `probe.Endpoint`. Fails the Api, Portal and Admin OTLP tests (the positive control: no export reached the probe). Removing the logging default does not fail the OTLP tests with the current exporter packages, which do not send through the factory; `FactoryClientLeakTests` is the test that pins the default.

- [ ] **Step 6: Whole-project check**

Run: `dotnet build TechStrap.slnx -c Release` (0 warnings), then `dotnet test --solution TechStrap.CI.slnf -c Release` (the Api and integration tests need Docker) and `pwsh -File scripts/Invoke-ScriptTests.ps1`
Expected: PASS. The existing `NotFoundHostTests`, `ErrorStatusHostTests` and `UnhandledErrorHostTests` of the Admin and the Portal are the regression cover for the shared error pages.

- [ ] **Step 7: Commit**

```bash
git add src/TechStrap.Hosting src/TechStrap.Admin src/TechStrap.Portal src/TechStrap.Api src/TechStrap.Worker \
  tests/TechStrap.Architecture.Tests tests/TechStrap.Api.Tests tests/TechStrap.Admin.Tests/AttachmentPassThroughTests.cs
git diff --cached --stat
git commit -m "refactor(hosting): shared browser-host wiring and a host-wide HttpClient logging default (P07-T20)" -m "TechStrap.Hosting now holds the correlation id, health checks, forwarded headers, key ring, security headers, error pages and the Serilog and Sentry wiring that Admin and Portal duplicated. The Portal gains the PII enricher and the Sentry header scrubber. All four hosts remove the IHttpClientFactory logging handlers, so an OTLP header secret cannot reach a log; the leak tests prove an export was attempted. Attachment downloads keep their sandbox under the shared headers." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```

### Task 4: Security headers and the Content-Security-Policy (Opus reviews this task)

**Review Focus pin:**
- **(1)** A CSP that breaks the app or sign-in in a real browser, or is weaker than decided. The policy is pinned directive by directive on the responses of the Admin (static `/signin` and `/error`, the signed-in page, the re-executed 404), the Portal and the Api by `ContentSecurityPolicyHostTests`; the stylesheet half (every `url()` in the compiled CSS is covered) by `CspStyleTests`; the inline-markup half (no page renders an inline `<script>`, a `<style>` element or an `on*=` attribute, which also catches a returning `<ImportMap />`) by the two `..._renders_no_inline_script...` tests. What only a browser can show (the Blazor WebSocket under `connect-src 'self'`, the OIDC redirect under `form-action`, the colours under `style-src-attr`) is the owner's Chrome console check, in Step 7.
- **D-042 (owner decisions, 2026-10-04):** `script-src 'self'` stays strict; `style-src 'self'` plus `style-src-attr 'unsafe-inline'`.

**Files:**
- Create: `src/TechStrap.Hosting/Security/CspBuilder.cs`, `src/TechStrap.Hosting/Security/TechStrapCsp.cs`
- Modify: `src/TechStrap.Admin/Program.cs`, `src/TechStrap.Portal/Program.cs`, `src/TechStrap.Api/Program.cs`
- Modify: `src/TechStrap.Admin/Components/App.razor` and `src/TechStrap.Portal/Components/App.razor` (the import map component goes)
- Modify: `tests/TechStrap.Architecture.Tests/AdminRules.cs`, `AdminRuleTests.cs` (Task 1's rule now flags `<ImportMap />`)
- Modify: `tests/TechStrap.Api.Tests/HostFactory.cs` (the Admin test host sets the default authority before Program.cs reads it)
- Create: `tests/TechStrap.Api.Tests/Hosting/CspBuilderTests.cs`, `tests/TechStrap.Api.Tests/Hosting/ContentSecurityPolicyHostTests.cs`
- Create: `tests/TechStrap.Admin.Tests/CspStyleTests.cs`

**Interfaces:**
- Consumes: `AddTechStrapWebHost(services, configuration, contentSecurityPolicy)` and `AddTechStrapSecurityHeaders` (Task 3), `AdminTestSettings.Authority`, `CompiledCss` (test support), the host factories.
- Produces (names are fixed; the docs in Task 10 and the later hosts use them):

```csharp
namespace TechStrap.Hosting.Security;

public sealed class CspBuilder                                   // validated, ordered directives
{
    public CspBuilder Directive(string name, params string[] sources);   // set (replace)
    public CspBuilder Allow(string name, params string[] sources);       // add, no repeats
    public string Build();                                               // "name src src; name src; ..."
}

public static class TechStrapCsp
{
    public static string ForBlazorApp(IEnumerable<string?>? formActionOrigins = null, bool allowLoopbackImages = false);   // Admin, Portal
    public static string ForApi();                                                                                         // Api
    public static string? OriginOf(string? url);                                                                           // scheme://host[:port] or null
}
```

The decided Blazor policy (`ForBlazorApp()`), exactly:

```text
default-src 'self'; script-src 'self'; style-src 'self'; style-src-attr 'unsafe-inline'; img-src 'self' https: data:; connect-src 'self'; font-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'
```

with, for the Admin, the identity provider's origin appended to `form-action`, and, in Development only, `http://localhost:* http://127.0.0.1:*` appended to `img-src`.

**Rules:**
1. **Script is strict and nothing weakens it.** No `'unsafe-inline'`, no `'unsafe-eval'`, no host and no `*` in `script-src`; `'unsafe-inline'` appears in exactly one directive, `style-src-attr`. The test asserts that across the whole policy, so a later "just this once" edit fails.
2. **Why `style-src-attr 'unsafe-inline'`.** Blazor writes `style="..."` attributes, and the product colours (`TagChip`, `AccentPreview`, the Portal's accent scope) are arbitrary validated hex values that classes cannot cover. A nonce or hash does not help an attribute, and a CSSOM approach would leave the prerendered HTML unstyled. Style elements and stylesheets stay same-origin, and an attribute style cannot run script.
3. **Why `img-src ... https: data:`.** `https:` because a product logo is an https URL on any host (D-041). `data:` because the compiled Bootstrap CSS draws the form-select arrow, the checkbox tick and the close icon as `data:` SVG backgrounds; `CspStyleTests` fails if the stylesheet ever references a kind of URL the policy does not cover, and fails if `data:` is dropped while a `data:` image is still used. Loopback `http` logos (the D-041 Development form) are allowed only in Development.
4. **`connect-src 'self'` and the circuit.** CSP 3 says `'self'` matches `ws:` and `wss:` for the same host and port. Chromium and Firefox implement that; old Safari (before 15.4) did not. No explicit `ws:` or `wss:` is added, because that would allow a socket to any host. The Step 7 check proves it in the owner's browser; if a target browser needs it, add the page's own `wss://host` per request, never a wildcard.
5. **`form-action` carries the identity provider.** The sign-in link (`/signin/start`), the gate's re-sign-in and the sign-out form (`POST /signout`) all answer with a redirect to the provider, and Chromium applies `form-action` to the redirects that follow a form submission. The origin comes from `Auth:Authority` through `TechStrapCsp.OriginOf`, which keeps only `scheme://host[:port]` and refuses a non-http(s) value, a relative path and user info. `CspBuilder` refuses any source with a semicolon, a comma, whitespace or a control character, so configuration can never add a directive.
6. **The import map goes.** `<ImportMap />` renders an inline `<script type="importmap">`, which `script-src 'self'` blocks, and the page would silently lose the fingerprint mapping. The Admin's modules (`dialog.js`, `shortcuts.js`, `queue.js`, `preferences.js`, `clipboard.js`, and the reconnect modal's `ReconnectModal.razor.js`) are imported by plain path, and `MapStaticAssets` serves each plain path (with revalidation), which `Every_module_the_Admin_imports_is_served_from_its_plain_path` proves. A hash was rejected: the map's content changes with every build's fingerprints.
7. **No `upgrade-insecure-requests`.** The package default had it. It would rewrite a Development `http://localhost` logo to https, and `img-src` already refuses http images; the transport is protected by `Strict-Transport-Security`, which the shared headers keep.
8. **The Api gets the strictest policy.** It serves JSON and downloads, never a page: `default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'`. Downloads still get `sandbox` appended by `UseAttachmentSandbox`, which stays registered before the security headers.
9. **One policy per host kind, built in code.** The hosts pass the string to the shared wiring, which layers it over the configuration last, so the in-code policy wins over any `SecurityHeaders__ContentSecurityPolicy` setting. No `appsettings*.json` file has a `SecurityHeaders` section today and none should gain a policy.
10. **Encoding.** All new strings are ASCII.

- [ ] **Step 1: Write the failing tests**

1. `tests/TechStrap.Api.Tests/Hosting/CspBuilderTests.cs`:

```csharp
using TechStrap.Hosting.Security;

namespace TechStrap.Api.Tests.Hosting;

public sealed class CspBuilderTests
{
    [Fact]
    public void Directives_are_written_in_the_order_they_were_added_separated_by_semicolons()
    {
        var policy = new CspBuilder()
            .Directive("default-src", "'self'")
            .Directive("img-src", "'self'", "https:", "data:")
            .Directive("upgrade-insecure-requests")
            .Build();

        policy.ShouldBe("default-src 'self'; img-src 'self' https: data:; upgrade-insecure-requests");
    }

    [Fact]
    public void Directive_replaces_and_Allow_adds_without_repeating_a_source()
    {
        var policy = new CspBuilder()
            .Directive("form-action", "'self'")
            .Allow("form-action", "https://idp.test", "'self'")
            .Allow("form-action", "https://idp.test")
            .Allow("img-src", "https:")
            .Directive("img-src", "data:")
            .Build();

        policy.ShouldBe("form-action 'self' https://idp.test; img-src data:");
    }

    [Theory]
    [InlineData("https://idp.test; script-src *")]
    [InlineData("https://idp.test;script-src")]
    [InlineData(";")]
    [InlineData("https://idp.test, https://evil.test")]
    [InlineData("https://idp.test script-src")]
    [InlineData("https://idp.test\nscript-src *")]
    [InlineData("https://idp.test\t")]
    [InlineData("")]
    public void A_source_that_could_end_the_directive_or_start_another_is_refused(string source)
    {
        Should.Throw<ArgumentException>(() => new CspBuilder().Directive("form-action", source));
        Should.Throw<ArgumentException>(() => new CspBuilder().Allow("form-action", source));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Script-Src")]
    [InlineData("script src")]
    [InlineData("script-src;")]
    [InlineData("1script")]
    public void A_directive_name_that_is_not_lower_case_words_is_refused(string name)
    {
        Should.Throw<ArgumentException>(() => new CspBuilder().Directive(name, "'self'"));
    }

    [Theory]
    [InlineData("https://idp.test/application/o/techstrap-admin/", "https://idp.test")]
    [InlineData("https://idp.test:8443/realms/x", "https://idp.test:8443")]
    [InlineData("HTTPS://IDP.TEST:443/", "https://idp.test")]
    [InlineData("http://localhost:9000/application/o/x/", "http://localhost:9000")]
    [InlineData("  https://idp.test/a  ", "https://idp.test")]
    public void OriginOf_keeps_the_scheme_host_and_a_non_default_port_and_drops_the_path(string url, string expected)
    {
        TechStrapCsp.OriginOf(url).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/application/o/x/")]
    [InlineData("idp.test")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("ftp://idp.test/")]
    [InlineData("https://user:pass@idp.test/")]
    public void OriginOf_refuses_anything_that_is_not_an_absolute_http_or_https_URL_without_user_info(string? url)
    {
        TechStrapCsp.OriginOf(url).ShouldBeNull();
    }

    [Fact]
    public void The_blazor_policy_is_exactly_the_decided_one()
    {
        TechStrapCsp.ForBlazorApp().ShouldBe(
            "default-src 'self'; script-src 'self'; style-src 'self'; style-src-attr 'unsafe-inline'; img-src 'self' https: data:; connect-src 'self'; "
            + "font-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'");
    }

    [Fact]
    public void The_blazor_policy_adds_the_identity_provider_origin_to_form_action_and_ignores_unusable_entries()
    {
        var policy = TechStrapCsp.ForBlazorApp(["https://idp.test/application/o/techstrap-admin/", null, "", "javascript:alert(1)", "https://idp.test/other"]);

        policy.ShouldEndWith("form-action 'self' https://idp.test");
    }

    [Fact]
    public void Loopback_logo_images_are_allowed_only_when_asked_for()
    {
        TechStrapCsp.ForBlazorApp().ShouldNotContain("localhost");
        TechStrapCsp.ForBlazorApp(allowLoopbackImages: true)
            .ShouldContain("img-src 'self' https: data: http://localhost:* http://127.0.0.1:*;");
    }

    [Fact]
    public void The_api_policy_allows_nothing()
    {
        TechStrapCsp.ForApi().ShouldBe("default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'");
    }
}
```

2. `tests/TechStrap.Api.Tests/Hosting/ContentSecurityPolicyHostTests.cs`:

```csharp
using System.Net;
using System.Text.RegularExpressions;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Api.Tests.Hosting;

/// <summary>
/// Review Focus 1: the policy each web host really sends, read back from the response, directive by directive (never computed from the code that builds it). A policy
/// that is weaker than decided, or that breaks sign-in or the page, fails here; the owner's browser check (ADMIN-APP.md) covers what only a browser can show.
/// </summary>
/// <remarks>One test sets the <c>Auth__Authority</c> process environment variable (Program.cs reads it while it builds the host), so the class runs in the non-parallel <see cref="ProcessEnvironmentCollection"/>.</remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed partial class ContentSecurityPolicyHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [GeneratedRegex(@"<script\b(?![^>]*\bsrc\s*=)[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InlineScriptTag();

    [GeneratedRegex(@"<style\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StyleElement();

    [GeneratedRegex(@"\son[a-z]+\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EventHandlerAttribute();

    private static Dictionary<string, string[]> Policy(HttpResponseMessage response)
    {
        response.Headers.TryGetValues("Content-Security-Policy", out var values).ShouldBeTrue("the response has no Content-Security-Policy");
        var header = values!.Single();
        return header.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(directive => directive.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToDictionary(parts => parts[0], parts => parts[1..], StringComparer.Ordinal);
    }

    private static void AssertBrowserPolicy(Dictionary<string, string[]> policy, string[] formAction, bool loopbackImages)
    {
        policy["default-src"].ShouldBe(["'self'"]);
        policy["script-src"].ShouldBe(["'self'"]);
        policy["style-src"].ShouldBe(["'self'"]);
        policy["style-src-attr"].ShouldBe(["'unsafe-inline'"]);
        policy["connect-src"].ShouldBe(["'self'"]);
        policy["font-src"].ShouldBe(["'self'"]);
        policy["object-src"].ShouldBe(["'none'"]);
        policy["frame-ancestors"].ShouldBe(["'none'"]);
        policy["base-uri"].ShouldBe(["'self'"]);
        policy["form-action"].ShouldBe(formAction);
        policy["img-src"].Take(3).ShouldBe(["'self'", "https:", "data:"]);
        policy["img-src"].Skip(3).ShouldBe(loopbackImages ? ["http://localhost:*", "http://127.0.0.1:*"] : []);

        // Nothing weaker than decided: script never allows inline code or eval, and 'unsafe-inline' appears in exactly one directive.
        policy.Keys.ShouldNotContain("script-src-attr");
        policy.Where(d => d.Value.Contains("'unsafe-inline'")).Select(d => d.Key).ShouldBe(["style-src-attr"]);
        policy.Values.SelectMany(v => v).ShouldNotContain("'unsafe-eval'");
        policy.Values.SelectMany(v => v).ShouldNotContain("*");
        policy.Values.SelectMany(v => v).ShouldNotContain("http:");
    }

    [Fact]
    public async Task The_Admin_pages_carry_the_decided_policy_with_the_identity_provider_in_form_action()
    {
        await using var factory = new AdminFactory();
        using var anonymous = factory.CreateClient();
        using var signedIn = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);
        var idp = new Uri(AdminTestSettings.Authority).GetLeftPart(UriPartial.Authority);

        using var signIn = await anonymous.GetAsync("/signin", Ct);
        using var error = await anonymous.GetAsync("/error", Ct);
        using var home = await signedIn.GetAsync("/", Ct);
        using var missing = await signedIn.GetAsync("/no-such-page", Ct);

        foreach (var (response, where) in new[] { (signIn, "/signin"), (error, "/error"), (home, "/"), (missing, "404") })
        {
            AssertBrowserPolicy(Policy(response), ["'self'", idp], loopbackImages: true);
        }

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Fact]
    public async Task In_Production_the_Admin_does_not_allow_loopback_logos()
    {
        // Production refuses to start without a trusted proxy network, which Program.cs binds before the factory's settings exist.
        Environment.SetEnvironmentVariable("TrustedProxy__TrustedNetworks__0", "192.0.2.0/24");
        try
        {
            await using var factory = new AdminFactory("Production");
            using var client = factory.CreateClient();

            using var response = await client.GetAsync("/signin", Ct);

            AssertBrowserPolicy(Policy(response), ["'self'", new Uri(AdminTestSettings.Authority).GetLeftPart(UriPartial.Authority)], loopbackImages: false);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TrustedProxy__TrustedNetworks__0", null);
        }
    }

    [Fact]
    public async Task The_form_action_origin_follows_the_configured_authority()
    {
        Environment.SetEnvironmentVariable("Auth__Authority", "https://sso.example.test:8443/application/o/techstrap-admin/");
        try
        {
            await using var factory = new AdminFactory(settings: new Dictionary<string, string?> { ["Auth:Authority"] = "https://sso.example.test:8443/application/o/techstrap-admin/" });
            using var client = factory.CreateClient();

            using var response = await client.GetAsync("/signin", Ct);

            Policy(response)["form-action"].ShouldBe(["'self'", "https://sso.example.test:8443"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Auth__Authority", AdminTestSettings.Authority);
        }
    }

    [Fact]
    public async Task The_Portal_carries_the_decided_policy_with_no_identity_provider()
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        using var home = await client.GetAsync("/", Ct);
        using var missing = await client.GetAsync("/no-such-page", Ct);

        AssertBrowserPolicy(Policy(home), ["'self'"], loopbackImages: true);
        AssertBrowserPolicy(Policy(missing), ["'self'"], loopbackImages: true);
    }

    [Fact]
    public async Task The_Api_policy_allows_nothing_and_a_download_is_sandboxed_on_top()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var health = await client.GetAsync("/health/live", Ct);
        using var openApi = await client.GetAsync("/openapi/v1.json", Ct);

        foreach (var response in new[] { health, openApi })
        {
            var policy = Policy(response);
            policy["default-src"].ShouldBe(["'none'"]);
            policy["frame-ancestors"].ShouldBe(["'none'"]);
            policy["base-uri"].ShouldBe(["'none'"]);
            policy["form-action"].ShouldBe(["'none'"]);
        }
    }

    // The policy forbids inline script and event-handler attributes. Every page the hosts render must already obey it: a page that renders an inline <script> (the import map
    // component does) would work in a test and be blocked in a browser, so the rendered HTML is checked, not only the header.
    [Fact]
    public async Task No_Admin_page_renders_an_inline_script_a_style_element_or_an_event_handler_attribute()
    {
        await using var factory = new AdminFactory();
        using var anonymous = factory.CreateClient();
        using var signedIn = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        // The sign-in page is a standalone static page; the others are rendered by App.razor and load the framework script, which proves the scan sees a real page.
        var pages = new List<(string Html, bool Framework)>
        {
            (await anonymous.GetStringAsync("/signin", Ct), false),
            (await anonymous.GetStringAsync("/error", Ct), true),
            (await signedIn.GetStringAsync("/", Ct), true),
            (await signedIn.GetStringAsync("/queue/mine", Ct), true),
        };

        foreach (var (html, framework) in pages)
        {
            html.ShouldContain("<html", Case.Sensitive);
            if (framework)
            {
                html.ShouldContain("blazor.web", Case.Sensitive, "the framework script must be there, or this check scans nothing");
            }

            InlineScriptTag().Matches(html).Select(m => m.Value).ShouldBeEmpty();
            StyleElement().IsMatch(html).ShouldBeFalse();
            EventHandlerAttribute().IsMatch(html).ShouldBeFalse();
        }

        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Fact]
    public async Task The_Portal_renders_no_inline_script_a_style_element_or_an_event_handler_attribute()
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/", Ct);

        html.ShouldContain("blazor.web", Case.Sensitive);
        InlineScriptTag().Matches(html).Select(m => m.Value).ShouldBeEmpty();
        StyleElement().IsMatch(html).ShouldBeFalse();
        EventHandlerAttribute().IsMatch(html).ShouldBeFalse();
    }

    // Without the import map component the modules load from their own paths, so each one must still be served (and not need a fingerprint to be found).
    [Theory]
    [InlineData("/js/dialog.js")]
    [InlineData("/js/shortcuts.js")]
    [InlineData("/js/queue.js")]
    [InlineData("/js/preferences.js")]
    [InlineData("/js/clipboard.js")]
    [InlineData("/_content/SyntaxCircus.Blazor.Components/ReconnectModal.razor.js")]
    public async Task Every_module_the_Admin_imports_is_served_from_its_plain_path(string path)
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
```

3. `tests/TechStrap.Admin.Tests/CspStyleTests.cs`:

```csharp
using System.Text.RegularExpressions;
using TechStrap.Hosting.Security;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>
/// Review Focus 1, the style half: the policy lets the stylesheet draw everything it draws. The compiled CSS may reference only same-origin files (fonts, images) and
/// <c>data:</c> images, and the policy must allow exactly those, or a colour, a font or an icon silently disappears in a browser while every other test is green.
/// </summary>
public sealed partial class CspStyleTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    [GeneratedRegex(@"url\(\s*(?<q>[""']?)(?<target>.*?)\k<q>\s*\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex UrlReference();

    [GeneratedRegex(@"@import\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ImportRule();

    private static string[] Sources(string directive) =>
        TechStrapCsp.ForBlazorApp().Split(';', StringSplitOptions.TrimEntries)
            .Select(d => d.Split(' '))
            .Single(parts => parts[0] == directive)[1..];

    private static IReadOnlyList<string> Targets() => [.. UrlReference().Matches(Css.Text).Select(m => m.Groups["target"].Value)];

    [Fact]
    public void The_stylesheet_references_fonts_and_images_so_this_check_scans_something()
    {
        Targets().ShouldContain(t => t.EndsWith(".woff2", StringComparison.Ordinal), "the self-hosted fonts");
        Targets().ShouldContain(t => t.StartsWith("data:image/", StringComparison.Ordinal), "Bootstrap's inline SVG icons");
    }

    [Fact]
    public void The_stylesheet_loads_nothing_from_another_host_and_imports_nothing()
    {
        Targets().ShouldAllBe(t => !t.StartsWith("http:", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("https:", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("//", StringComparison.Ordinal));
        ImportRule().IsMatch(Css.Text).ShouldBeFalse("style-src 'self' would block a remote stylesheet, and an @import hides one from this check");
    }

    [Fact]
    public void The_policy_allows_every_kind_of_url_the_stylesheet_uses()
    {
        var targets = Targets();
        if (targets.Any(t => t.StartsWith("data:image/", StringComparison.Ordinal)))
        {
            Sources("img-src").ShouldContain("data:");
        }

        if (targets.Any(t => t.EndsWith(".woff2", StringComparison.Ordinal)))
        {
            Sources("font-src").ShouldBe(["'self'"]);
        }

        targets.Where(t => !t.StartsWith("data:", StringComparison.Ordinal) && !t.EndsWith(".woff2", StringComparison.Ordinal))
            .ShouldBeEmpty("a url() of a kind the policy was not written for: decide which directive covers it");
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet build tests/TechStrap.Admin.Tests tests/TechStrap.Api.Tests -c Release`
Expected: FAIL to build (`CspBuilder` and `TechStrapCsp` do not exist).

- [ ] **Step 3: Add the builder and the policies**

1. `src/TechStrap.Hosting/Security/CspBuilder.cs`:

```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace TechStrap.Hosting.Security;

/// <summary>
/// Builds a Content-Security-Policy header value one directive at a time. A directive name and every source are validated, so a value that comes from configuration
/// (an identity-provider origin) can never close the directive early and smuggle in another one: a source may not contain a semicolon, a comma, whitespace or a control
/// character. Directives keep the order they were first added in, so the header is stable and a test can compare it as text.
/// </summary>
public sealed partial class CspBuilder
{
    private readonly List<string> _order = [];
    private readonly Dictionary<string, List<string>> _directives = new(StringComparer.Ordinal);

    [GeneratedRegex("^[a-z][a-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex DirectiveName();

    /// <summary>Sets a directive, replacing any earlier sources of the same name. A directive with no source is written as its name alone (<c>upgrade-insecure-requests</c>, <c>sandbox</c>).</summary>
    public CspBuilder Directive(string name, params string[] sources)
    {
        Validate(name, sources);
        if (!_directives.ContainsKey(name))
        {
            _order.Add(name);
        }

        _directives[name] = [.. sources.Distinct(StringComparer.Ordinal)];
        return this;
    }

    /// <summary>Adds sources to a directive, creating it when it does not exist yet. A source that is already there is not repeated.</summary>
    public CspBuilder Allow(string name, params string[] sources)
    {
        Validate(name, sources);
        if (!_directives.TryGetValue(name, out var existing))
        {
            _order.Add(name);
            existing = [];
            _directives[name] = existing;
        }

        foreach (var source in sources.Where(source => !existing.Contains(source, StringComparer.Ordinal)))
        {
            existing.Add(source);
        }

        return this;
    }

    /// <summary>The header value: <c>name source source; name source; ...</c></summary>
    public string Build()
    {
        var text = new StringBuilder();
        foreach (var name in _order)
        {
            if (text.Length > 0)
            {
                text.Append("; ");
            }

            text.Append(name);
            foreach (var source in _directives[name])
            {
                text.Append(' ').Append(source);
            }
        }

        return text.ToString();
    }

    private static void Validate(string name, string[] sources)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(sources);
        if (!DirectiveName().IsMatch(name))
        {
            throw new ArgumentException($"'{name}' is not a CSP directive name (lower-case letters, digits and hyphens).", nameof(name));
        }

        foreach (var source in sources)
        {
            if (string.IsNullOrEmpty(source) || source.Any(c => c is ';' or ',' || char.IsWhiteSpace(c) || char.IsControl(c)))
            {
                throw new ArgumentException($"A source for '{name}' is empty or holds a semicolon, a comma, whitespace or a control character, which would end the directive or start another.", nameof(sources));
            }
        }
    }
}
```

2. `src/TechStrap.Hosting/Security/TechStrapCsp.cs`:

```csharp
namespace TechStrap.Hosting.Security;

/// <summary>
/// The Content-Security-Policy of each kind of TechStrap host (D-042). The policies are plain strings built by <see cref="CspBuilder"/>; the hosts pass them to
/// <c>AddTechStrapWebHost</c> or <c>AddTechStrapSecurityHeaders</c>.
/// </summary>
public static class TechStrapCsp
{
    private const string Self = "'self'";
    private const string None = "'none'";

    /// <summary>
    /// The policy for a Blazor Server host that serves HTML (Admin, Portal). Scripts are strict: only files of this origin, so no inline script, no <c>eval</c>, no other
    /// host. Styles are strict too (<c>style-src 'self'</c>), with one deliberate relaxation: <c>style-src-attr 'unsafe-inline'</c>. Blazor writes <c>style="..."</c> attributes, and the
    /// product colours (<c>TagChip</c>, <c>AccentPreview</c>, the Portal's accent scope) are arbitrary validated hex values that classes cannot cover, so inline style
    /// attributes are allowed while style elements and stylesheets stay same-origin. Attribute styles cannot run script.
    /// </summary>
    /// <param name="formActionOrigins">
    /// Origins a form may submit or redirect to besides this one. The Admin passes its identity provider's origin: the sign-in link and the sign-out form answer with a
    /// redirect to the provider, and Chromium applies <c>form-action</c> to the redirects that follow a form submission. Null, blank and non-http(s) entries are ignored.
    /// </param>
    /// <param name="allowLoopbackImages">
    /// Development only: lets a product logo on <c>http://localhost</c> or <c>http://127.0.0.1</c> show in the branding preview (the logo rule accepts that form, D-041).
    /// A real browser never loads a customer's loopback address, so it is not needed or allowed elsewhere.
    /// </param>
    public static string ForBlazorApp(IEnumerable<string?>? formActionOrigins = null, bool allowLoopbackImages = false)
    {
        var images = new List<string> { Self, "https:", "data:" };
        if (allowLoopbackImages)
        {
            images.AddRange(["http://localhost:*", "http://127.0.0.1:*"]);
        }

        // img-src https: because a product logo is an https URL on any host (D-041); data: because the compiled Bootstrap CSS draws the form-select arrow, the checkbox tick and
        // the close icon as data: SVG background images (CspStyleTests proves every url() in app.css is covered). connect-src 'self' also covers the circuit's WebSocket in
        // browsers that implement CSP 3 (Chromium and Firefox); see ADMIN-APP.md for the browser check. font-src and the rest are same-origin: the fonts are self-hosted.
        var policy = new CspBuilder()
            .Directive("default-src", Self)
            .Directive("script-src", Self)
            .Directive("style-src", Self)
            .Directive("style-src-attr", "'unsafe-inline'")
            .Directive("img-src", [.. images])
            .Directive("connect-src", Self)
            .Directive("font-src", Self)
            .Directive("object-src", None)
            .Directive("frame-ancestors", None)
            .Directive("base-uri", Self)
            .Directive("form-action", Self);
        foreach (var origin in (formActionOrigins ?? []).Select(OriginOf).OfType<string>())
        {
            policy.Allow("form-action", origin);
        }

        return policy.Build();
    }

    /// <summary>
    /// The policy for the API, which serves JSON and file downloads and never a page: nothing may load, frame or submit anything. Downloads get <c>sandbox</c> appended by
    /// the attachment middleware.
    /// </summary>
    public static string ForApi() => new CspBuilder()
        .Directive("default-src", None)
        .Directive("base-uri", None)
        .Directive("form-action", None)
        .Directive("frame-ancestors", None)
        .Build();

    /// <summary>
    /// <c>scheme://host[:port]</c> of an absolute http or https URL (the port only when it is not the scheme's default), or null for anything else: blank text, a relative
    /// path, another scheme (<c>javascript:</c>), a URL with user info. Used to turn a configured identity-provider authority into a CSP source.
    /// </summary>
    public static string? OriginOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || string.IsNullOrEmpty(uri.Host))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }
}
```

- [ ] **Step 4: Apply the policy in the hosts and remove the import map**

```diff
--- a/src/TechStrap.Admin/Program.cs
+++ b/src/TechStrap.Admin/Program.cs
@@ -7,6 +7,7 @@
 using TechStrap.Admin.Features.Shell;
 using TechStrap.Admin.Features.Tickets;
 using TechStrap.Admin.Options;
+using TechStrap.Hosting.Security;
 using TechStrap.Hosting.Wiring;
 
 const string ServiceName = "techstrap-admin";
@@ -23,7 +24,10 @@
 
 // Correlation id, health checks, trusted-proxy forwarded headers, the data-protection key ring, the host-wide HttpClient logging default and the security headers,
 // shared with the Portal (TechStrap.Hosting).
-builder.Services.AddTechStrapWebHost(builder.Configuration);
+// The Content-Security-Policy lets the sign-in and sign-out redirects reach the identity provider (form-action) and, in Development only, a product logo on localhost.
+builder.Services.AddTechStrapWebHost(
+    builder.Configuration,
+    TechStrapCsp.ForBlazorApp([TechStrapCsp.OriginOf(builder.Configuration["Auth:Authority"])], allowLoopbackImages: builder.Environment.IsDevelopment()));
 
 // Required settings are validated when the host starts (not read here), so a missing Auth or Api key stops the start with a clear message.
 builder.Services.AddAdminOptions(builder.Configuration);
```

```diff
--- a/src/TechStrap.Portal/Program.cs
+++ b/src/TechStrap.Portal/Program.cs
@@ -1,5 +1,6 @@
 using SyntaxCircus.AspNetCore.Common;
 using SyntaxCircus.DotEnv;
+using TechStrap.Hosting.Security;
 using TechStrap.Hosting.Wiring;
 using TechStrap.Portal.Components;
 using TechStrap.Portal.Components.Ui;
@@ -18,7 +19,8 @@
 
 // Correlation id, health checks, trusted-proxy forwarded headers, the data-protection key ring, the host-wide HttpClient logging default and the security headers,
 // shared with the Admin (TechStrap.Hosting).
-builder.Services.AddTechStrapWebHost(builder.Configuration);
+// The Content-Security-Policy; the Portal has no sign-in, so form-action is this origin only. Loopback logos only in Development.
+builder.Services.AddTechStrapWebHost(builder.Configuration, TechStrapCsp.ForBlazorApp(allowLoopbackImages: builder.Environment.IsDevelopment()));
 
 builder.Services.AddRazorComponents();
 // Installation-wide switch for the "Powered by TechStrap" footer (D-024); shown unless set to false.
```

```diff
--- a/src/TechStrap.Api/Program.cs
+++ b/src/TechStrap.Api/Program.cs
@@ -9,6 +9,7 @@
 using TechStrap.Api.Security;
 using TechStrap.Api.Startup;
 using TechStrap.Hosting.Logging;
+using TechStrap.Hosting.Security;
 using TechStrap.Hosting.Sentry;
 using TechStrap.Hosting.Wiring;
 using TechStrap.Infrastructure.Intake;
@@ -45,7 +46,8 @@
 builder.Services.AddCorrelationId();
 // No HttpClient the factory creates (the OTLP exporters' included) logs its request headers: the default logging writes Authorization and x-api-key at Trace.
 builder.Services.AddTechStrapHttpClientDefaults();
-builder.Services.AddSecurityHeaders(builder.Configuration);
+// The API serves JSON and downloads, never a page, so its policy allows nothing; UseAttachmentSandbox adds sandbox to downloads.
+builder.Services.AddTechStrapSecurityHeaders(builder.Configuration, TechStrapCsp.ForApi());
 builder.Services.AddProblemDetailsExceptionHandling();
 builder.Services.AddControllers();
 builder.Services.AddOpenApi();
```

```diff
--- a/src/TechStrap.Admin/Components/App.razor
+++ b/src/TechStrap.Admin/Components/App.razor
@@ -11,7 +11,6 @@
     <link rel="icon" type="image/png" sizes="32x32" href="favicon-32.png" />
     <link rel="apple-touch-icon" href="apple-touch-icon.png" />
     <link rel="stylesheet" href="@Assets["css/app.css"]" />
-    <ImportMap />
     <HeadOutlet @rendermode="PageRenderMode" />
 </head>
 
```

```diff
--- a/src/TechStrap.Portal/Components/App.razor
+++ b/src/TechStrap.Portal/Components/App.razor
@@ -9,7 +9,6 @@
     <link rel="icon" type="image/png" sizes="32x32" href="favicon-32.png" />
     <link rel="apple-touch-icon" href="apple-touch-icon.png" />
     <link rel="stylesheet" href="@Assets["css/app.css"]" />
-    <ImportMap />
     <HeadOutlet />
 </head>
 
```

Task 1's rule now flags the import map component too (it renders an inline script whenever the app has fingerprinted assets to map; the Portal does, the Admin's own pages render nothing today, so only the source rule can keep it out of the Admin):

```diff
--- a/tests/TechStrap.Architecture.Tests/AdminRules.cs
+++ b/tests/TechStrap.Architecture.Tests/AdminRules.cs
@@ -41,8 +41,11 @@
     [GeneratedRegex(@"(@attribute\s*\[|^\s*\[)\s*(?:[\w.]+\s*,\s*)*(?:Microsoft\.AspNetCore\.Authorization\.)?AllowAnonymous\b", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
     private static partial Regex AllowAnonymousAttribute();
 
-    /// <summary>An element that carries code or style in the page: a script without a src, or any style element.</summary>
-    [GeneratedRegex(@"<style\b|<script\b(?![^>]*\bsrc\s*=)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
+    /// <summary>
+    /// An element that carries code or style in the page: a script without a src, any style element, or the import map component, which renders an inline
+    /// <c>&lt;script type="importmap"&gt;</c> whenever the app has fingerprinted assets to map.
+    /// </summary>
+    [GeneratedRegex(@"<style\b|<script\b(?![^>]*\bsrc\s*=)|<ImportMap\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
     private static partial Regex InlineScriptOrStyle();
 
     public static IReadOnlyList<string> PackageViolations(ProjectNode admin) =>
@@ -59,7 +62,7 @@
     public static IReadOnlyList<string> InlineMarkupViolations(IEnumerable<(string Path, string Text)> files) =>
         [.. files
             .Where(f => f.Path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) && InlineScriptOrStyle().IsMatch(f.Text))
-            .Select(f => $"{f.Path} has an inline <script> or a <style> element. The CSP allows neither; use wwwroot/js modules and Styles/_*.scss.")];
+            .Select(f => $"{f.Path} has an inline <script>, a <style> element or <ImportMap />. The CSP allows none of them; use wwwroot/js modules and Styles/_*.scss.")];
 
     public static IReadOnlyList<string> StaticPageViolations(IEnumerable<(string Path, string Text)> files)
     {
```

```diff
--- a/tests/TechStrap.Architecture.Tests/AdminRuleTests.cs
+++ b/tests/TechStrap.Architecture.Tests/AdminRuleTests.cs
@@ -129,14 +129,15 @@
     [InlineData("<SCRIPT type=\"text/javascript\">x()</SCRIPT>")]
     [InlineData("<style>.a { color: red }</style>")]
     [InlineData("<style media=\"print\"></style>")]
-    public void An_inline_script_or_style_element_is_flagged(string markup)
+    [InlineData("<ImportMap />")]
+    public void An_inline_script_or_style_element_or_the_import_map_is_flagged(string markup)
     {
         AdminRules.InlineMarkupViolations([("Components/Bad.razor", markup)]).Count.ShouldBe(1);
     }
 
     [Fact]
-    public void An_external_script_and_an_import_map_component_are_not_flagged()
+    public void An_external_script_is_not_flagged()
     {
-        AdminRules.InlineMarkupViolations([("Components/App.razor", "<ImportMap />\n<script src=\"@Assets[\"_framework/blazor.web.js\"]\"></script>")]).ShouldBeEmpty();
+        AdminRules.InlineMarkupViolations([("Components/App.razor", "<script src=\"@Assets[\"_framework/blazor.web.js\"]\"></script>")]).ShouldBeEmpty();
     }
 }
```

`Program.cs` reads `Auth:Authority` while it builds the host, before the test factory's in-memory settings exist (the same reason the trusted proxy and the test issuer arrive as environment variables), so the Api test project's Admin factory sets the default authority that way:

```diff
--- a/tests/TechStrap.Api.Tests/HostFactory.cs
+++ b/tests/TechStrap.Api.Tests/HostFactory.cs
@@ -105,6 +105,13 @@
 /// </summary>
 public sealed class AdminFactory : HostFactory<TechStrap.Admin.Program>
 {
+    static AdminFactory()
+    {
+        // Program.cs reads Auth:Authority while it builds the host, to put the identity provider's origin into the Content-Security-Policy (form-action), which is before the
+        // factory's in-memory settings exist. So, like the trusted proxy and the test issuer, the default authority arrives as an environment variable.
+        Environment.SetEnvironmentVariable("Auth__Authority", AdminTestSettings.Authority);
+    }
+
     public AdminFactory(
         string environment = "Development",
         IReadOnlyDictionary<string, string?>? settings = null,
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter "CspBuilderTests|ContentSecurityPolicyHostTests|SecurityHeadersHostTests|AdminLeakTests"` and `dotnet test --project tests/TechStrap.Admin.Tests -c Release`
Expected: PASS (`ContentSecurityPolicyHostTests` runs in `ProcessEnvironmentCollection`: two tests set environment variables). To see the pins guard (do not commit these edits), make each change below, rerun the Api command (the Admin command for the CSS ones, the Architecture command for the rule), expect the failures named, and restore the file with `git checkout -- <file>`. These were run in the scratch copy:

- `TechStrapCsp.ForBlazorApp`: add `'unsafe-inline'` to `script-src` (or `'unsafe-eval'`). Fails `CspBuilderTests.The_blazor_policy_is_exactly_the_decided_one` and the Admin and Portal policy tests of `ContentSecurityPolicyHostTests`.
- `TechStrapCsp.ForBlazorApp`: delete the `style-src-attr` directive. Fails the same four tests (the colours would be blocked in a browser).
- `src/TechStrap.Admin/Program.cs`: call `TechStrapCsp.ForBlazorApp([])` instead of passing the authority origin. Fails `ContentSecurityPolicyHostTests.The_Admin_pages_carry_the_decided_policy_with_the_identity_provider_in_form_action`, `The_form_action_origin_follows_the_configured_authority` and `In_Production_the_Admin_does_not_allow_loopback_logos` (sign-in would break in Chromium).
- `TechStrapCsp.ForBlazorApp`: drop `"data:"` from the image list. Fails the four policy tests and, in `TechStrap.Admin.Tests`, `CspStyleTests.The_policy_allows_every_kind_of_url_the_stylesheet_uses` (the compiled CSS draws Bootstrap icons as `data:` images).
- `src/TechStrap.Admin/Styles/_shell.scss`: add a rule with `background: url(https://cdn.example.test/a.png)`. Fails `CspStyleTests.The_stylesheet_loads_nothing_from_another_host_and_imports_nothing` and `The_policy_allows_every_kind_of_url_the_stylesheet_uses`.
- `src/TechStrap.Portal/Components/App.razor`: put `<ImportMap />` back in `<head>`. Fails `ContentSecurityPolicyHostTests.The_Portal_renders_no_inline_script_a_style_element_or_an_event_handler_attribute` (the page renders an inline `<script type="importmap">`).
- `src/TechStrap.Admin/Components/App.razor`: put `<ImportMap />` back. Fails `TechStrap.Architecture.Tests` `AdminRuleTests.No_razor_file_has_an_inline_script_or_a_style_element` (the Admin's pages render no import map today, so only the source rule can see it).
- `CspBuilder.Validate`: stop refusing `;` (`c is ';' or ',' ||` becomes `c is ',' ||`). Fails `CspBuilderTests.A_source_that_could_end_the_directive_or_start_another_is_refused` for the `;` and `https://idp.test;script-src` rows.
- `TechStrapCsp.OriginOf`: delete the user-info check. Fails `CspBuilderTests.OriginOf_refuses_anything_that_is_not_an_absolute_http_or_https_URL_without_user_info` (the `https://user:pass@idp.test/` row).
- `src/TechStrap.Api/Program.cs`: call `AddTechStrapSecurityHeaders(builder.Configuration)` without `TechStrapCsp.ForApi()`. Fails `ContentSecurityPolicyHostTests.The_Api_policy_allows_nothing_and_a_download_is_sandboxed_on_top`.
- `src/TechStrap.Admin/Program.cs`: pass `allowLoopbackImages: true` instead of `builder.Environment.IsDevelopment()`. Fails `ContentSecurityPolicyHostTests.In_Production_the_Admin_does_not_allow_loopback_logos`.

- [ ] **Step 6: Whole-project check**

Run: `dotnet build TechStrap.slnx -c Release` (0 warnings), then `dotnet test --solution TechStrap.CI.slnf -c Release`
Expected: PASS.

- [ ] **Step 7: The owner's browser check (record the result in the PR; Authentik is not needed for the first two lines)**

Run the Admin in Development (`dotnet run --project src/TechStrap.Admin`, with the local compose Authentik placeholders) and open the browser console (Chrome) on each page, expecting no line that starts with `Refused to ...` or `Content-Security-Policy`:
- `/signin` and `/not-found`: the page, the fonts and the favicon load.
- `/_styleguide` (Development only): every swatch and tag chip shows its colour (this is the `style-src-attr` check), and the icons drawn from `data:` images show.
- After Authentik exists (owner action 7): sign in, confirm the redirect to the provider and back works (this is the `form-action` check), open `/queue`, confirm the circuit connects (no WebSocket refusal under `connect-src 'self'`), reply to a ticket, open the shortcut help dialog, and sign out.

Scratch-copy evidence: the Admin was run in Development and loaded in headless Edge (`msedge --headless=new --enable-logging=stderr --dump-dom`); `/signin` and `/_styleguide` produced no console line about a refused script, style, font or image. That run cannot show the circuit, the redirect or an interactive page (no identity provider exists), so the last line stays the owner's.

If a refusal appears, note the directive and the blocked URL in the PR. Do not loosen `script-src`; for `connect-src` add only the page's own `wss://host`.

- [ ] **Step 8: Commit**

```bash
git add src/TechStrap.Hosting src/TechStrap.Admin src/TechStrap.Portal src/TechStrap.Api \
  tests/TechStrap.Api.Tests tests/TechStrap.Admin.Tests/CspStyleTests.cs tests/TechStrap.Architecture.Tests
git diff --cached --stat
git commit -m "feat(security): Content-Security-Policy for Admin, Portal and Api (P07-T20)" -m "A validated CspBuilder and one policy per host kind. Blazor hosts: script-src and style-src 'self', style-src-attr 'unsafe-inline' for the product colours, img-src https: and data:, form-action 'self' plus the identity provider, frame-ancestors none. The Api allows nothing. The inline import map component is removed because the policy would block it; every module loads from its plain path. Host tests read the header back directive by directive and check the rendered pages for inline script, style elements and event-handler attributes." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```

### Task 5: Theme init: apply the stored theme before the first paint (D-041)

**Review Focus pin:** none of the five. This closes the D-041 deferral (the theme flashed until the circuit connected) without an inline script, so it fits the Task 4 policy (`script-src 'self'`). The behaviours are pinned by `theme-init.test.mjs` (including the "never throws" cases and the parity test with `preferences.js`) and by `ThemeInitHostTests` (the script is served, first in `<head>`, blocking and classic).

**Files:**
- Create: `src/TechStrap.Admin/wwwroot/js/theme-init.js`
- Modify: `src/TechStrap.Admin/Components/App.razor`, `src/TechStrap.Admin/Components/Pages/SignInLanding.razor` (the sign-in page is its own document, outside `App.razor`)
- Create: `tests/TechStrap.Admin.Tests/js/theme-init.test.mjs`
- Modify: `scripts/tests/AdminScripts.Tests.ps1`
- Modify: `tests/TechStrap.Admin.Tests/ScriptHostTests.cs`

**Interfaces:**
- Consumes: `wwwroot/js/preferences.js` (its storage key `techstrap.admin.theme`, its `THEMES` and `writePreference`, used only by the parity test), the static asset fingerprinting that `@Assets[...]` already uses in `App.razor`, and `AdminFactory` (test support).
- Produces:

```text
wwwroot/js/theme-init.js            a classic script (no import, no export); storage key 'techstrap.admin.theme'
App.razor and SignInLanding.razor <head>   <script src="@Assets["js/theme-init.js"]"></script> after the icons and before the stylesheet link
```

**Rules:**
1. **Classic and blocking.** No `type="module"`, no `defer`, no `async`: a module is deferred and runs after the first paint, which is the flash this task removes.
2. **Same key, same meaning as `preferences.js`.** `light` and `dark` set `data-bs-theme` on `<html>`; every other value, and a missing value, removes it. The parity test writes each theme with `writePreference` and reads it back through the script, so a renamed key or a new theme fails the test.
3. **It must never throw.** Storage can be missing, blocked or full; `window` or `document` may be absent. Every access is inside `try`.
4. **`PreferencesService` is unchanged.** `preferences.js` still loads and applies the theme after the circuit connects, and still writes the key; the early script only removes the flash.
5. **No inline script.** The tag has a `src`. Task 1's rule and Task 4's host test keep it that way.
6. **Every document, not only `App.razor`.** The anonymous sign-in page is rendered by the `GET /signin` endpoint as a whole document of its own, so it loads the script too. the host tests in `ScriptHostTests` cover `/signin`, `/error` and a signed-in page.

- [ ] **Step 1: Write the failing tests**

1. `tests/TechStrap.Admin.Tests/js/theme-init.test.mjs`:

```javascript
// Runs with `node --test` (no browser, no jsdom): the script is evaluated in a vm context whose window and document are plain objects.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, it } from 'node:test';
import vm from 'node:vm';
import { THEMES, writePreference } from '../../../src/TechStrap.Admin/wwwroot/js/preferences.js';

const source = readFileSync(new URL('../../../src/TechStrap.Admin/wwwroot/js/theme-init.js', import.meta.url), 'utf8');

const memoryStorage = (initial = {}) => {
    const items = new Map(Object.entries(initial));
    return {
        items,
        getItem: (key) => (items.has(key) ? items.get(key) : null),
        setItem: (key, value) => items.set(key, String(value)),
    };
};

const page = (initial = {}) => {
    const attributes = new Map(Object.entries(initial));
    return {
        attributes,
        documentElement: {
            setAttribute: (name, value) => attributes.set(name, value),
            removeAttribute: (name) => attributes.delete(name),
        },
    };
};

/** Runs the script as the browser would: once, as a classic script, with no module scope and no exports. */
const run = (globals) => vm.runInNewContext(source, globals);

describe('theme-init.js', () => {
    it('sets data-bs-theme for a stored light or dark choice', () => {
        for (const theme of ['light', 'dark']) {
            const document = page();
            run({ window: { localStorage: memoryStorage({ 'techstrap.admin.theme': theme }) }, document });
            assert.equal(document.attributes.get('data-bs-theme'), theme);
        }
    });

    it('removes the attribute for auto, so the system preference decides', () => {
        const document = page({ 'data-bs-theme': 'dark' });
        run({ window: { localStorage: memoryStorage({ 'techstrap.admin.theme': 'auto' }) }, document });
        assert.equal(document.attributes.has('data-bs-theme'), false);
    });

    it('removes the attribute when nothing is stored or the stored value is not a theme', () => {
        for (const stored of [{}, { 'techstrap.admin.theme': 'purple' }, { 'techstrap.admin.theme': '' }, { 'techstrap.admin.theme': 'LIGHT' }]) {
            const document = page({ 'data-bs-theme': 'dark' });
            run({ window: { localStorage: memoryStorage(stored) }, document });
            assert.equal(document.attributes.has('data-bs-theme'), false, JSON.stringify(stored));
        }
    });

    it('never throws when the storage is blocked, and treats it as auto', () => {
        const blocked = { getItem: () => { throw new Error('SecurityError: storage is blocked'); } };
        const document = page({ 'data-bs-theme': 'dark' });
        assert.doesNotThrow(() => run({ window: { localStorage: blocked }, document }));
        assert.equal(document.attributes.has('data-bs-theme'), false);
    });

    it('never throws when reading window.localStorage itself throws', () => {
        const window = {};
        Object.defineProperty(window, 'localStorage', { get() { throw new Error('SecurityError'); } });
        assert.doesNotThrow(() => run({ window, document: page() }));
    });

    it('never throws when there is no window, no document, or the element refuses the change', () => {
        assert.doesNotThrow(() => run({}));
        assert.doesNotThrow(() => run({ window: { localStorage: memoryStorage({ 'techstrap.admin.theme': 'dark' }) } }));
        const refusing = { documentElement: { setAttribute: () => { throw new Error('frozen'); }, removeAttribute: () => { throw new Error('frozen'); } } };
        assert.doesNotThrow(() => run({ window: { localStorage: memoryStorage({ 'techstrap.admin.theme': 'dark' }) }, document: refusing }));
    });

    it('is a classic script: no import, no export, no top-level await', () => {
        assert.doesNotMatch(source, /^\s*(import|export)\s/m);
        assert.doesNotMatch(source, /\bawait\b/);
    });

    it('reads the key preferences.js writes, for every theme, so the two cannot drift apart', () => {
        for (const theme of THEMES) {
            const storage = memoryStorage();
            assert.equal(writePreference(storage, 'theme', theme), true);
            const document = page({ 'data-bs-theme': 'light' });
            run({ window: { localStorage: storage }, document });
            const expected = theme === 'auto' ? undefined : theme;
            assert.equal(document.attributes.get('data-bs-theme'), expected, theme);
        }
    });
});
```

2. In `tests/TechStrap.Admin.Tests/ScriptHostTests.cs` add the host tests (the class gains a `using System.Text.RegularExpressions;`):

```diff
--- a/tests/TechStrap.Admin.Tests/ScriptHostTests.cs
+++ b/tests/TechStrap.Admin.Tests/ScriptHostTests.cs
@@ -1,9 +1,14 @@
 using System.Net;
+using System.Text.RegularExpressions;
+using TechStrap.Tests.Shared.AdminHost;
 
 namespace TechStrap.Admin.Tests;
 
-public sealed class ScriptHostTests
+public sealed partial class ScriptHostTests
 {
+    [GeneratedRegex(@"<script\b[^>]*theme-init[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
+    private static partial Regex ThemeScriptTag();
+
     [Theory]
     [InlineData("/js/dialog.js")]
     [InlineData("/js/shortcuts.js")]
@@ -21,4 +26,46 @@
         response.Content.Headers.ContentType!.MediaType.ShouldBeOneOf("text/javascript", "application/javascript");
         (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("export ");
     }
+
+    // The theme script is not a module: a module is deferred and would run after the first paint, which is the flash it exists to prevent.
+    [Fact]
+    public async Task The_theme_script_is_served_without_signing_in_and_is_a_classic_script()
+    {
+        await using var factory = new AdminFactory();
+        using var client = factory.CreateClient();
+
+        using var response = await client.GetAsync("/js/theme-init.js", TestContext.Current.CancellationToken);
+        var script = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
+
+        response.StatusCode.ShouldBe(HttpStatusCode.OK);
+        response.Content.Headers.ContentType!.MediaType.ShouldBeOneOf("text/javascript", "application/javascript");
+        script.ShouldContain("techstrap.admin.theme");
+        script.ShouldNotContain("export ");
+        script.ShouldNotContain("import ");
+    }
+
+    [Theory]
+    [InlineData("/signin", false)]
+    [InlineData("/error", false)]
+    [InlineData("/", true)]
+    public async Task Every_page_loads_the_theme_script_in_head_before_the_styles_as_a_blocking_classic_script(string path, bool signedIn)
+    {
+        await using var factory = new AdminFactory();
+        using var client = signedIn ? factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent) : factory.CreateClient();
+
+        var html = await client.GetStringAsync(path, TestContext.Current.CancellationToken);
+
+        var head = html[..html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase)];
+        var tag = ThemeScriptTag().Match(head);
+        tag.Success.ShouldBeTrue("the theme script must be in <head>");
+        tag.Value.ShouldNotContain("defer", Case.Insensitive);
+        tag.Value.ShouldNotContain("async", Case.Insensitive);
+        tag.Value.ShouldNotContain("module", Case.Insensitive);
+        tag.Value.ShouldNotContain("type=", Case.Insensitive);
+        head.IndexOf(tag.Value, StringComparison.Ordinal).ShouldBeLessThan(head.IndexOf("rel=\"stylesheet\"", StringComparison.Ordinal), "before the first stylesheet, so the theme is set before the first paint");
+        if (signedIn)
+        {
+            factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
+        }
+    }
 }
```

3. In `scripts/tests/AdminScripts.Tests.ps1`, add an `It` after the preferences one:

```diff
--- a/scripts/tests/AdminScripts.Tests.ps1
+++ b/scripts/tests/AdminScripts.Tests.ps1
@@ -30,6 +30,19 @@
         $LASTEXITCODE | Should -Be 0 -Because $output
     }
 
+    It 'passes the node:test suite for the early theme script (never throws, same key and meaning as preferences.js)' {
+        if (-not $script:Node) {
+            Set-ItResult -Skipped -Because 'node is not installed, so the theme-init.js tests cannot run'
+            return
+        }
+
+        $testFile = Join-Path $script:RepoRoot 'tests/TechStrap.Admin.Tests/js/theme-init.test.mjs'
+
+        $output = & node --test $testFile 2>&1 | Out-String
+
+        $LASTEXITCODE | Should -Be 0 -Because $output
+    }
+
     It 'passes the node:test suite for the clipboard helpers' {
         if (-not $script:Node) {
             Set-ItResult -Skipped -Because 'node is not installed, so the clipboard.js tests cannot run'
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `node --test tests/TechStrap.Admin.Tests/js/theme-init.test.mjs`
Expected: FAIL (`ENOENT` for `theme-init.js`).

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "ScriptHostTests"`
Expected: FAIL (`/js/theme-init.js` answers 404, and the page has no theme script).

- [ ] **Step 3: Add the script and load it**

1. `src/TechStrap.Admin/wwwroot/js/theme-init.js`:

```javascript
// Applies the stored colour theme before the first paint (D-041, D-042). It is a classic, blocking script in <head>, loaded before the stylesheets, so a light or
// dark choice does not flash the other theme while the circuit connects. A module would be deferred and would run after the page had painted.
//
// It reads the same localStorage key as preferences.js ('techstrap.admin.theme') and sets or removes data-bs-theme on <html> exactly as applyTheme() there does:
// 'light' and 'dark' set it, anything else (auto, nothing stored, a bad value) removes it so the system preference decides (_color-mode.scss).
// It must never throw: storage can be missing, blocked or full, and a page that cannot be themed still works. tests/TechStrap.Admin.Tests/js/theme-init.test.mjs
// runs it under node:test and checks it stays in step with preferences.js.
(function () {
    try {
        var stored = null;
        try {
            stored = window.localStorage.getItem('techstrap.admin.theme');
        } catch (storageError) {
            stored = null;
        }

        var root = document.documentElement;
        if (stored === 'light' || stored === 'dark') {
            root.setAttribute('data-bs-theme', stored);
        } else {
            root.removeAttribute('data-bs-theme');
        }
    } catch (error) {
        // No window, no document, a frozen element: leave the page as it is.
    }
})();
```

2. `src/TechStrap.Admin/Components/App.razor`: the script goes in `<head>` before the stylesheet, as a plain `<script src>` (fingerprinted through `@Assets`, like `blazor.web.js`):

```diff
--- a/src/TechStrap.Admin/Components/App.razor
+++ b/src/TechStrap.Admin/Components/App.razor
@@ -10,6 +10,8 @@
     <link rel="icon" href="favicon.ico" sizes="any" />
     <link rel="icon" type="image/png" sizes="32x32" href="favicon-32.png" />
     <link rel="apple-touch-icon" href="apple-touch-icon.png" />
+    @* A classic, blocking script (never a module, never deferred): it sets data-bs-theme from the stored choice before the stylesheet paints, so a light or dark choice does not flash. *@
+    <script src="@Assets["js/theme-init.js"]"></script>
     <link rel="stylesheet" href="@Assets["css/app.css"]" />
     <HeadOutlet @rendermode="PageRenderMode" />
 </head>
```

```diff
--- a/src/TechStrap.Admin/Components/Pages/SignInLanding.razor
+++ b/src/TechStrap.Admin/Components/Pages/SignInLanding.razor
@@ -11,6 +11,8 @@
     <link rel="icon" href="favicon.ico" sizes="any" />
     <link rel="icon" type="image/png" sizes="32x32" href="favicon-32.png" />
     <link rel="apple-touch-icon" href="apple-touch-icon.png" />
+    @* The sign-in page is its own document (outside App.razor), so it loads the early theme script too, before the stylesheet. *@
+    <script src="@Assets["js/theme-init.js"]"></script>
     <link rel="stylesheet" href="@Assets["css/app.css"]" />
 </head>
 
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `node --test tests/TechStrap.Admin.Tests/js/theme-init.test.mjs`
Expected: PASS, 8 tests.

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "ScriptHostTests|SourceEncodingTests|SourceEscapeTests"`
Expected: PASS. To see the pins guard (do not commit these edits), make each change below, rerun the command named, expect the failures named, and restore the file with `git checkout -- <file>`. These were run in the scratch copy:

- `App.razor`: delete the `<script src="@Assets["js/theme-init.js"]"></script>` line. Fails `ScriptHostTests.Every_page_loads_the_theme_script_in_head_before_the_styles_as_a_blocking_classic_script` for `/error` and `/`.
- `SignInLanding.razor`: delete the same line. Fails the same test for `/signin` (the sign-in page is its own document, outside `App.razor`).
- `App.razor`: add `defer` to the script tag. Fails the same test (`/error` and `/`).
- `App.razor`: move the script below the stylesheet link. Fails the same test (it must come before the first stylesheet).
- `theme-init.js`: change the key to `techstrap.theme`. Fails `theme-init.test.mjs` `sets data-bs-theme for a stored light or dark choice` and `reads the key preferences.js writes, for every theme, so the two cannot drift apart`.
- `theme-init.js`: accept only `'light'` (drop `|| stored === 'dark'`). Fails the same two tests.
- `theme-init.js`: replace the inner `catch (storageError)` with `finally`, so a blocked storage can throw. Fails `never throws when the storage is blocked, and treats it as auto` (and the two above, because the script no longer parses).

- [ ] **Step 5: Whole-project check**

Run: `dotnet build TechStrap.slnx -c Release` (0 warnings), then `dotnet test --project tests/TechStrap.Admin.Tests -c Release` and `pwsh -File scripts/Invoke-ScriptTests.ps1`
Expected: PASS (the script run is the node suite inside Pester).

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Admin/wwwroot/js/theme-init.js src/TechStrap.Admin/Components/App.razor \
  src/TechStrap.Admin/Components/Pages/SignInLanding.razor \
  tests/TechStrap.Admin.Tests/js/theme-init.test.mjs tests/TechStrap.Admin.Tests/ScriptHostTests.cs \
  scripts/tests/AdminScripts.Tests.ps1
git diff --cached --stat
git commit -m "feat(admin): apply the stored theme before first paint (D-041)" -m "An external, blocking, classic script in head reads the same storage key as preferences.js and sets or removes data-bs-theme before the stylesheets load, so a light or dark choice no longer flashes the other theme while the circuit connects. It never throws and needs no inline script, so it fits the CSP." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```

---

### Task 6: Local time: the browser's zone, `LocalTimeService` and `RelativeTime` (D-040, D-042)

**Review Focus pins:** (5) a time shows the wrong instant: DST, an unknown zone, or the prerender versus the interactive render (`LocalTimeServiceTests`, `TicketDisplayTests`, `tz.test.mjs`).

**Shared files with Tasks 1 to 5.** The hunks in this task were written against `main`. Where Tasks 1 to 5 changed the same file, apply the intent of each hunk to the current file instead of matching its text. Files to expect this in: `MainLayout.razor.cs`, `NavMenu.razor.cs`, `AdminComponentTest.cs`, `ScriptHostTests.cs`, `AdminScripts.Tests.ps1`, `Dockerfiles.Tests.ps1`, Api `Program.cs`, `ApiFactory` and `AdminFactory`, and also `ShellServiceCollectionExtensions.cs`, `NavMenu.razor`, `MainLayout.razor`, `TicketDetailPage.razor.cs`, `ShortcutService.cs`, `SentryOptionsExtensions.cs`, `ci.yml`, `ComposeFiles.Tests.ps1`, `app.scss` and the three docs files that Task 10 edits. The same note applies to Tasks 7 to 10.

**Files:**
- Modify: `Dockerfile.admin`
- Modify: `scripts/tests/AdminScripts.Tests.ps1`
- Modify: `scripts/tests/Dockerfiles.Tests.ps1`
- Modify: `src/TechStrap.Admin/Components/Layout/MainLayout.razor.cs`
- Modify: `src/TechStrap.Admin/Components/Ui/RelativeTime.razor`
- Modify: `src/TechStrap.Admin/Components/Ui/RelativeTime.razor.cs`
- Modify: `src/TechStrap.Admin/Components/Ui/TicketDisplay.cs`
- Modify: `src/TechStrap.Admin/Components/Ui/TintedEntry.razor`
- Modify: `src/TechStrap.Admin/Components/Ui/TintedEntry.razor.cs`
- Create: `src/TechStrap.Admin/Features/Shell/LocalTimeService.cs`
- Modify: `src/TechStrap.Admin/Features/Shell/ShellServiceCollectionExtensions.cs`
- Modify: `src/TechStrap.Admin/Features/Tickets/MessageBubble.razor`
- Modify: `src/TechStrap.Admin/Features/Tickets/MessageBubble.razor.cs`
- Create: `src/TechStrap.Admin/wwwroot/js/tz.js`
- Create: `tests/TechStrap.Admin.Tests/Components/LocalTimeServiceTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/Components/TicketDisplayTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/ScriptHostTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/Support/AdminComponentTest.cs`
- Create: `tests/TechStrap.Admin.Tests/js/tz.test.mjs`

**Interfaces:**
- Consumes:
  - `RelativeTime` (every call site: the queue row, the ticket header, the timeline, dead letters, audit, agents, API keys), `TicketDisplay.Relative` and `Absolute`, `TintedEntry`, `MessageBubble`, `AddShell` (the scoped shell services), `MainLayout.OnAfterRenderAsync` (loads `PreferencesService` first, then starts the key listener), the browser-module pattern of `PreferencesService` (import once, never throw for a script failure) and its bUnit tests (`PreferencesServiceTests`, strict JS mode with `JSInterop.SetupModule`).
  - The test base `AdminComponentTest` (a `FakeTimeProvider` fixed at 2026-10-04 12:00 UTC, the module doubles for `dialog.js`, `shortcuts.js` and `preferences.js`), `ScriptHostTests` (every module is served as JavaScript without signing in), `scripts/tests/AdminScripts.Tests.ps1` (one Pester `It` per node:test file), `Dockerfile.admin` and `scripts/tests/Dockerfiles.Tests.ps1`.
  - Assumed from Tasks 1 to 5: Task 2 hardens `MainLayout.OnAfterRenderAsync` against non-cancellation exceptions. This task adds one line to the same method, directly after `await Preferences.LoadAsync();`; if Task 2 has put that block inside a `try`, the new line goes inside it too (`LocalTimeService.LoadAsync` never throws for a script failure, so it needs no handling of its own).
- Produces:
  - `wwwroot/js/tz.js`: `resolveZone(intl)` (pure) and `zone()`, both returning the IANA name or `null`, never throwing.
  - `LocalTimeService` (scoped, in `AddShell`): `Zone` (UTC until known), `IsLoaded`, `Changed`, `LoadAsync()` (once; never throws for a script failure), `ToLocal(when)`, static `Resolve(string? id)` (null, blank or unknown is UTC), `ModulePath`.
  - `TicketDisplay.Relative(when, now, zone = null)` and `Absolute(when, zone = null)`; `RelativeTime` draws again when the zone arrives; `TintedEntry.When` (messages show a `<time>` with the tooltip, not plain text); `AdminComponentTest.Tz`.
  - `Dockerfile.admin` installs `tzdata`.

**Rules:**
1. **The zone comes from the browser only** (`Intl.DateTimeFormat().resolvedOptions().timeZone`), read once per circuit after the first interactive render, next to the preferences. There is no API field and no stored preference (D-042). Until it arrives, and whenever it cannot be known, every time is UTC: that is what the prerender shows, so the first interactive render does not change a time that was already correct.
2. **Resolution.** `TimeZoneInfo.FindSystemTimeZoneById` with the IANA name, which works on .NET 10 on Linux from `/usr/share/zoneinfo` and on Windows with ICU. A missing, blank or unknown name, and a script that fails, are UTC and still count as loaded. The runtime itself refuses a name with a dot or a backslash, so a hostile value cannot reach the file system. `LoadAsync` runs once however often it is asked.
3. **Display.** "5 min ago", "3 h ago" and "2 d ago" do not depend on the zone (an instant is an instant). From a week on the visible text is the date **in the agent's zone** (a message sent at 23:30 UTC on 1 July is 2 July in London and in Auckland). The tooltip is the local time, the zone id and the UTC time ("2026-10-04 12:55 Europe/London, 2026-10-04 11:55 UTC"); in UTC it stays "2026-10-04 11:55 UTC". The `datetime` attribute is always the UTC ISO instant. A message in the timeline gets the same `<time>` element and tooltip as every other time.
4. **Daylight saving belongs to the instant**, not to today: `ToLocal` converts with the zone's rules (`TimeZoneInfo.ConvertTime`), never with a fixed offset.
5. **Every time cell listens** for `Changed` and unsubscribes when it goes, so the zone arriving redraws what is on screen and a removed cell is not kept alive.
6. **The Admin image installs `tzdata`**, so the zone files exist at run time; without them every zone would fall back to UTC. A Pester check pins it.

- [ ] **Step 1: Write the failing tests**

The node test, the new service tests, the changes to the existing test files and the Pester `It`. `AdminComponentTest` gains the `tz.js` module double (MainLayout now imports it on the first render); a test that wants another zone sets `Tz.Setup<string?>("zone", ...)` up again, the way `PreferencesServiceTests` re-sets `load`.

`scripts/tests/AdminScripts.Tests.ps1`

Replace

```powershell
        $LASTEXITCODE | Should -Be 0 -Because $output
    }

    It 'passes the node:test suite for the dialog module (Esc and a stray native close can never dismiss a locked dialog)' {
        if (-not $script:Node) {
            Set-ItResult -Skipped -Because 'node is not installed, so the dialog.js tests cannot run'
```

with

```powershell
        $LASTEXITCODE | Should -Be 0 -Because $output
    }

    It 'passes the node:test suite for the time zone module (the zone is read without ever throwing)' {
        if (-not $script:Node) {
            Set-ItResult -Skipped -Because 'node is not installed, so the tz.js tests cannot run'
            return
        }

        $testFile = Join-Path $script:RepoRoot 'tests/TechStrap.Admin.Tests/js/tz.test.mjs'

        $output = & node --test $testFile 2>&1 | Out-String

        $LASTEXITCODE | Should -Be 0 -Because $output
    }

    It 'passes the node:test suite for the dialog module (Esc and a stray native close can never dismiss a locked dialog)' {
        if (-not $script:Node) {
            Set-ItResult -Skipped -Because 'node is not installed, so the dialog.js tests cannot run'
```

`scripts/tests/Dockerfiles.Tests.ps1`

Replace

```powershell
    It 'pre-creates and chowns the storage, logs and dataprotection-keys mount points' {
        $script:Text | Should -Match 'mkdir -p /app/storage /app/logs /app/dataprotection-keys'
        $script:Text | Should -Match 'chown -R 10001:10001 /app/storage /app/logs /app/dataprotection-keys'
    }
}
```

with

```powershell
    It 'pre-creates and chowns the storage, logs and dataprotection-keys mount points' {
        $script:Text | Should -Match 'mkdir -p /app/storage /app/logs /app/dataprotection-keys'
        $script:Text | Should -Match 'chown -R 10001:10001 /app/storage /app/logs /app/dataprotection-keys'
    }
}

Describe 'Dockerfile.admin time zone data' {
    # The Admin shows every time in the agent's browser zone and finds it with TimeZoneInfo.FindSystemTimeZoneById, which reads /usr/share/zoneinfo on Linux (D-042).
    It 'installs tzdata in the runtime image' {
        (Get-DockerfileText -Name 'admin') | Should -Match 'apt-get install -y --no-install-recommends curl tzdata'
    }
}
```

`tests/TechStrap.Admin.Tests/Components/LocalTimeServiceTests.cs`

```csharp
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using TechStrap.Admin.Components.Layout;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Tests.Support;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// Review Focus 5, local time. The zone comes from the browser once per circuit; the prerender and a circuit that has no zone show UTC; an unknown zone is UTC; the offset belongs to the instant
/// (daylight saving); the machine-readable <c>datetime</c> stays UTC.
/// </summary>
public sealed class LocalTimeServiceTests : AdminComponentTest
{
    private LocalTimeService Service => Services.GetRequiredService<LocalTimeService>();

    private void BrowserZone(string? zone) => Tz.Setup<string?>("zone", _ => true).SetResult(zone);

    [Fact]
    public void Before_the_zone_is_loaded_the_service_is_utc_and_not_loaded()
    {
        Service.Zone.ShouldBe(TimeZoneInfo.Utc);
        Service.IsLoaded.ShouldBeFalse();
    }

    [Fact]
    public async Task Loading_reads_the_browser_zone_once_and_announces_it()
    {
        BrowserZone("Europe/London");
        var service = Service;
        var changes = 0;
        service.Changed += () => changes++;

        await Task.WhenAll(service.LoadAsync(), service.LoadAsync());
        await service.LoadAsync();

        service.IsLoaded.ShouldBeTrue();
        service.Zone.Id.ShouldBe("Europe/London");
        Tz.VerifyInvoke("zone", 1);
        changes.ShouldBe(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("../../etc/passwd")]
    [InlineData("Europe\\London")]
    [InlineData("Europe/London; DROP")]
    public async Task A_missing_unknown_or_oddly_shaped_zone_is_utc_and_still_counts_as_loaded(string? zone)
    {
        BrowserZone(zone);
        var service = Service;

        await service.LoadAsync();

        service.Zone.ShouldBe(TimeZoneInfo.Utc);
        service.IsLoaded.ShouldBeTrue();
    }

    [Fact]
    public async Task A_zone_name_longer_than_any_iana_name_is_utc()
    {
        BrowserZone("Europe/" + new string('a', 80));
        var service = Service;

        await service.LoadAsync();

        service.Zone.ShouldBe(TimeZoneInfo.Utc);
    }

    [Fact]
    public async Task A_script_that_fails_leaves_utc_and_never_throws()
    {
        Tz.Setup<string?>("zone", _ => true).SetException(new JSException("no Intl"));
        var service = Service;

        await service.LoadAsync();

        service.Zone.ShouldBe(TimeZoneInfo.Utc);
        service.IsLoaded.ShouldBeTrue();
    }

    [Fact]
    public async Task A_disconnected_circuit_is_not_an_error()
    {
        Tz.Setup<string?>("zone", _ => true).SetException(new JSDisconnectedException("The circuit is gone."));
        var service = Service;

        await service.LoadAsync();

        service.Zone.ShouldBe(TimeZoneInfo.Utc);
    }

    [Theory]
    [InlineData("Europe/London", "2026-01-15T12:00:00Z", "2026-01-15T12:00:00+00:00")]
    [InlineData("Europe/London", "2026-07-15T12:00:00Z", "2026-07-15T13:00:00+01:00")]
    [InlineData("America/New_York", "2026-03-08T06:59:00Z", "2026-03-08T01:59:00-05:00")]
    [InlineData("America/New_York", "2026-03-08T07:00:00Z", "2026-03-08T03:00:00-04:00")]
    [InlineData("Asia/Kolkata", "2026-10-04T11:55:00Z", "2026-10-04T17:25:00+05:30")]
    public async Task The_offset_belongs_to_the_instant_so_daylight_saving_is_the_zones_own(string zone, string utc, string expected)
    {
        BrowserZone(zone);
        var service = Service;
        await service.LoadAsync();

        var local = service.ToLocal(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture));

        local.ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture).ShouldBe(expected);
    }

    [Fact]
    public async Task The_layout_asks_the_browser_for_the_zone_once_and_after_the_preferences()
    {
        this.AddAgentShell();
        var order = new List<string>();
        Preferences.Setup<StoredPreferences>("load", _ =>
        {
            order.Add("preferences");
            return true;
        }).SetResult(new StoredPreferences(SingleKeyShortcuts: true, Theme: "auto"));
        Tz.Setup<string?>("zone", _ =>
        {
            order.Add("zone");
            return true;
        }).SetResult("Europe/London");

        var cut = Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, (RenderFragment)(b => { })));
        await cut.InvokeAsync(() => Task.CompletedTask);

        Tz.VerifyInvoke("zone", 1);
        order.ShouldBe(["preferences", "zone"]);
        Service.Zone.Id.ShouldBe("Europe/London");
    }

    [Fact]
    public async Task A_time_cell_shows_utc_until_the_zone_arrives_then_draws_again_in_local_time()
    {
        // 23:30 UTC on 1 July is half past eleven the next morning in Auckland (NZST, UTC+12): the date is the one that moves.
        var when = new DateTimeOffset(2026, 7, 1, 23, 30, 0, TimeSpan.Zero);
        BrowserZone("Pacific/Auckland");
        var cut = Render<RelativeTime>(p => p.Add(c => c.When, when));
        var time = cut.Find("time");
        time.TextContent.ShouldBe("2026-07-01");
        time.GetAttribute("title").ShouldBe("2026-07-01 23:30 UTC");

        await cut.InvokeAsync(() => Service.LoadAsync());

        cut.WaitForAssertion(() => cut.Find("time").TextContent.ShouldBe("2026-07-02"));
        cut.Find("time").GetAttribute("title").ShouldBe("2026-07-02 11:30 Pacific/Auckland, 2026-07-01 23:30 UTC");
        cut.Find("time").GetAttribute("datetime").ShouldBe("2026-07-01T23:30:00.0000000Z");
    }

    [Fact]
    public async Task A_message_in_the_timeline_follows_the_zone_too()
    {
        var when = new DateTimeOffset(2026, 7, 1, 23, 30, 0, TimeSpan.Zero);
        BrowserZone("Europe/London");
        var cut = Render<TintedEntry>(p => p
            .Add(e => e.Kind, EntryKind.PublicReply)
            .Add(e => e.Author, "Sam")
            .Add(e => e.When, when));
        cut.Find(".ts-entry-time time").TextContent.ShouldBe("2026-07-01");

        await cut.InvokeAsync(() => Service.LoadAsync());

        cut.WaitForAssertion(() => cut.Find(".ts-entry-time time").TextContent.ShouldBe("2026-07-02"));
        cut.Find(".ts-entry-time time").GetAttribute("title")!.ShouldContain("UTC");
    }
}
```

`tests/TechStrap.Admin.Tests/Components/TicketDisplayTests.cs`

Replace

```csharp
        TicketDisplay.Relative(now.AddSeconds(-secondsAgo), now).ShouldBe(expected);
    }

    [Fact]
    public void A_time_in_the_future_reads_as_just_now()
    {
```

with

```csharp
        TicketDisplay.Relative(now.AddSeconds(-secondsAgo), now).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Europe/London", "2026-03-01T23:30:00Z", "2026-03-01")]
    [InlineData("Europe/London", "2026-07-01T23:30:00Z", "2026-07-02")]
    [InlineData("America/Los_Angeles", "2026-07-02T03:30:00Z", "2026-07-01")]
    [InlineData("America/Los_Angeles", "2026-12-02T03:30:00Z", "2026-12-01")]
    [InlineData("Pacific/Auckland", "2026-07-01T12:30:00Z", "2026-07-02")]
    [InlineData("UTC", "2026-07-01T23:30:00Z", "2026-07-01")]
    public void After_a_week_the_date_is_the_date_in_the_agents_zone(string zone, string utc, string expected)
    {
        var when = DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture);
        var now = when.AddDays(30);

        TicketDisplay.Relative(when, now, TimeZoneInfo.FindSystemTimeZoneById(zone)).ShouldBe(expected);
    }

    [Fact]
    public void Within_a_week_the_zone_changes_nothing_and_no_zone_means_utc()
    {
        var now = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

        TicketDisplay.Relative(now.AddHours(-3), now, tokyo).ShouldBe("3 h ago");
        TicketDisplay.Relative(now.AddDays(-8), now).ShouldBe("2026-09-26");
        TicketDisplay.Relative(now.AddDays(-8), now, TimeZoneInfo.Utc).ShouldBe("2026-09-26");
    }

    [Fact]
    public void The_tooltip_gives_the_local_time_and_the_utc_time()
    {
        var when = new DateTimeOffset(2026, 10, 4, 11, 55, 0, TimeSpan.Zero);

        TicketDisplay.Absolute(when).ShouldBe("2026-10-04 11:55 UTC");
        TicketDisplay.Absolute(when, TimeZoneInfo.Utc).ShouldBe("2026-10-04 11:55 UTC");
        TicketDisplay.Absolute(when, TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).ShouldBe("2026-10-04 12:55 Europe/London, 2026-10-04 11:55 UTC");
    }

    [Fact]
    public void A_time_in_the_future_reads_as_just_now()
    {
```

`tests/TechStrap.Admin.Tests/ScriptHostTests.cs`

Replace

```csharp
    [InlineData("/js/queue.js")]
    [InlineData("/js/preferences.js")]
    [InlineData("/js/clipboard.js")]
    public async Task Module_scripts_are_served_as_javascript_without_signing_in(string path)
    {
        await using var factory = new AdminFactory();
```

with

```csharp
    [InlineData("/js/queue.js")]
    [InlineData("/js/preferences.js")]
    [InlineData("/js/clipboard.js")]
    [InlineData("/js/tz.js")]
    public async Task Module_scripts_are_served_as_javascript_without_signing_in(string path)
    {
        await using var factory = new AdminFactory();
```

`tests/TechStrap.Admin.Tests/Support/AdminComponentTest.cs` (2 edits)

Replace

```csharp
        Preferences = JSInterop.SetupModule("./js/preferences.js");
        Preferences.Setup<StoredPreferences>("load", _ => true).SetResult(new StoredPreferences(SingleKeyShortcuts: true, Theme: "auto"));
        Preferences.Setup<bool>("save", _ => true).SetResult(true);
    }

    protected FakeTimeProvider Time { get; }
```

with

```csharp
        Preferences = JSInterop.SetupModule("./js/preferences.js");
        Preferences.Setup<StoredPreferences>("load", _ => true).SetResult(new StoredPreferences(SingleKeyShortcuts: true, Theme: "auto"));
        Preferences.Setup<bool>("save", _ => true).SetResult(true);

        // MainLayout also asks the browser for its time zone on the first render; by default it answers UTC. A test sets it up again to try another zone.
        Tz = JSInterop.SetupModule("./js/tz.js");
        Tz.Setup<string?>("zone", _ => true).SetResult("UTC");
    }

    protected FakeTimeProvider Time { get; }
```

Replace

```csharp
    /// <summary>The <c>preferences.js</c> module double: <c>load</c> answers the defaults and <c>save</c> succeeds; read the arguments from <c>Invocations["save"]</c>.</summary>
    protected BunitJSModuleInterop Preferences { get; }

    protected ShortcutService ShortcutService => Services.GetRequiredService<ShortcutService>();

    protected StatusMessageService StatusMessages => Services.GetRequiredService<StatusMessageService>();
```

with

```csharp
    /// <summary>The <c>preferences.js</c> module double: <c>load</c> answers the defaults and <c>save</c> succeeds; read the arguments from <c>Invocations["save"]</c>.</summary>
    protected BunitJSModuleInterop Preferences { get; }

    /// <summary>The <c>tz.js</c> module double: <c>zone</c> answers "UTC"; set it up again to answer another zone or to fail.</summary>
    protected BunitJSModuleInterop Tz { get; }

    protected ShortcutService ShortcutService => Services.GetRequiredService<ShortcutService>();

    protected StatusMessageService StatusMessages => Services.GetRequiredService<StatusMessageService>();
```

`tests/TechStrap.Admin.Tests/js/tz.test.mjs`

```javascript
// Runs with `node --test` (no browser): resolveZone in tz.js takes the Intl object, so a test hands it a plain object.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { resolveZone, zone } from '../../../src/TechStrap.Admin/wwwroot/js/tz.js';

const intlWith = (timeZone) => ({ DateTimeFormat: () => ({ resolvedOptions: () => ({ timeZone }) }) });

describe('resolveZone', () => {
    it('returns the zone Intl reports', () => {
        assert.equal(resolveZone(intlWith('Europe/London')), 'Europe/London');
        assert.equal(resolveZone(intlWith('UTC')), 'UTC');
    });

    it('returns null for an empty or a non-string zone', () => {
        for (const value of ['', undefined, null, 5, {}]) {
            assert.equal(resolveZone(intlWith(value)), null, String(value));
        }
    });

    it('never throws: no Intl, an Intl that throws, or one that hides the zone', () => {
        assert.equal(resolveZone(null), null);
        assert.equal(resolveZone(undefined), null);
        assert.equal(resolveZone({}), null);
        assert.equal(resolveZone({ DateTimeFormat: () => { throw new RangeError('no zone'); } }), null);
        assert.equal(resolveZone({ DateTimeFormat: () => ({ resolvedOptions: () => { throw new Error('blocked'); } }) }), null);
    });
});

describe('zone', () => {
    it('answers the zone of the real Intl, as a string that Intl itself accepts', () => {
        const name = zone();
        assert.equal(typeof name, 'string');
        assert.doesNotThrow(() => new Intl.DateTimeFormat('en-GB', { timeZone: name }));
    });
});
```

- [ ] **Step 2: Run them and watch them fail**

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter-class "*LocalTimeServiceTests" --filter-class "*TicketDisplayTests"
node --test tests/TechStrap.Admin.Tests/js/tz.test.mjs
```

Expected: the build fails (`LocalTimeService`, `TicketDisplay.Relative(..., zone)`, `TintedEntry.When` and `AdminComponentTest.Tz` do not exist yet), and node fails with `ERR_MODULE_NOT_FOUND` for `tz.js`.

- [ ] **Step 3: Implement**

The browser module, the service, the display functions, the cells that listen, the layout call, the registration and the image. Create `wwwroot/js/tz.js` and `LocalTimeService.cs` as below, then apply the edits.

`Dockerfile.admin`

Replace

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:80
# curl backs the compose health checks. uid/gid 10001 is the fixed non-root runtime user.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 techstrap \
    && useradd --uid 10001 --gid 10001 --no-create-home --shell /usr/sbin/nologin techstrap \
```

with

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:80
# curl backs the compose health checks. tzdata holds the IANA zone files that LocalTimeService reads (D-042); without it every time would fall back to UTC.
# uid/gid 10001 is the fixed non-root runtime user.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl tzdata \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 techstrap \
    && useradd --uid 10001 --gid 10001 --no-create-home --shell /usr/sbin/nologin techstrap \
```

`src/TechStrap.Admin/Components/Layout/MainLayout.razor.cs` (2 edits)

Replace

```csharp

    [Inject]
    private PreferencesService Preferences { get; set; } = default!;

    /// <summary>
    /// The page has <c>&lt;base href="/"&gt;</c>, so a bare <c>#main</c> would resolve to the home page. The link names the current address with the fragment replaced.
```

with

```csharp

    [Inject]
    private PreferencesService Preferences { get; set; } = default!;

    [Inject]
    private LocalTimeService LocalTime { get; set; } = default!;

    /// <summary>
    /// The page has <c>&lt;base href="/"&gt;</c>, so a bare <c>#main</c> would resolve to the home page. The link names the current address with the fragment replaced.
```

Replace

```csharp
        {
            // The stored preferences first: they set the theme and whether single-key shortcuts act. Neither call throws for a script or storage failure.
            await Preferences.LoadAsync();
            await Shortcuts.StartAsync();
        }
    }
```

with

```csharp
        {
            // The stored preferences first: they set the theme and whether single-key shortcuts act. Neither call throws for a script or storage failure.
            await Preferences.LoadAsync();

            // The browser's time zone, so every time is drawn again in local time. It never throws for a script failure, and until it arrives every time is UTC.
            await LocalTime.LoadAsync();
            await Shortcuts.StartAsync();
        }
    }
```

`src/TechStrap.Admin/Components/Ui/RelativeTime.razor` (replace the whole file)

```razor
@implements IDisposable

<time datetime="@When.UtcDateTime.ToString("o")" title="@TicketDisplay.Absolute(When, LocalTime.Zone)">@TicketDisplay.Relative(When, Time.GetUtcNow(), LocalTime.Zone)</time>
```

`src/TechStrap.Admin/Components/Ui/RelativeTime.razor.cs`

Replace

```csharp
using Microsoft.AspNetCore.Components;

namespace TechStrap.Admin.Components.Ui;

/// <summary>"5 min ago" as visible text, with the machine-readable <c>datetime</c> and the absolute UTC time in the tooltip.</summary>
public partial class RelativeTime
{
    [Inject]
    private TimeProvider Time { get; set; } = default!;

    [Parameter, EditorRequired]
    public DateTimeOffset When { get; set; }
}
```

with

```csharp
using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// "5 min ago" as visible text, with the machine-readable <c>datetime</c> (always UTC) and the absolute time in the tooltip: the agent's local time and the UTC time. The prerender
/// and a circuit that has not learned the browser's zone yet show UTC; when <see cref="LocalTimeService"/> loads the zone, this draws again.
/// </summary>
public partial class RelativeTime
{
    [Inject]
    private TimeProvider Time { get; set; } = default!;

    [Inject]
    private LocalTimeService LocalTime { get; set; } = default!;

    [Parameter, EditorRequired]
    public DateTimeOffset When { get; set; }

    protected override void OnInitialized() => LocalTime.Changed += OnZoneChanged;

    private void OnZoneChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => LocalTime.Changed -= OnZoneChanged;
}
```

`src/TechStrap.Admin/Components/Ui/TicketDisplay.cs` (2 edits)

Replace

```csharp
        }
    }

    /// <summary>"just now", "5 min ago", "3 h ago", "2 d ago", then the UTC date. The caller supplies <paramref name="now"/> (a <see cref="TimeProvider"/>), so tests control it.</summary>
    public static string Relative(DateTimeOffset when, DateTimeOffset now)
    {
        var delta = now - when;
        if (delta < TimeSpan.FromMinutes(1))
```

with

```csharp
        }
    }

    /// <summary>
    /// "just now", "5 min ago", "3 h ago", "2 d ago", then the date in <paramref name="zone"/> (UTC when it is null). The caller supplies <paramref name="now"/> (a <see cref="TimeProvider"/>), so tests control it.
    /// Only the date depends on the zone: how long ago something happened is the same instant everywhere.
    /// </summary>
    public static string Relative(DateTimeOffset when, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        var delta = now - when;
        if (delta < TimeSpan.FromMinutes(1))
```

Replace

```csharp

        return delta.TotalDays < DaysPerWeek
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)delta.TotalDays} d ago")
            : when.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>The absolute time for a tooltip. Always UTC and labelled so: converting to the agent's zone needs the browser's zone (recorded gap, PHASE-07c).</summary>
    public static string Absolute(DateTimeOffset when) => when.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    /// <summary>Up to two initials for an avatar: "Sam Ortiz" is "SO", "sam" is "S", nothing is "?".</summary>
    public static string Initials(string? name)
```

with

```csharp

        return delta.TotalDays < DaysPerWeek
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)delta.TotalDays} d ago")
            : TimeZoneInfo.ConvertTime(when, zone ?? TimeZoneInfo.Utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The absolute time for a tooltip. In UTC (or with no zone) it is "2026-10-04 11:55 UTC". In another zone it is the local time first and the UTC time after it, so the instant is never in doubt:
    /// "2026-10-04 12:55 Europe/London, 2026-10-04 11:55 UTC".
    /// </summary>
    public static string Absolute(DateTimeOffset when, TimeZoneInfo? zone = null)
    {
        var utc = when.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
        if (zone is null || zone.Equals(TimeZoneInfo.Utc))
        {
            return utc;
        }

        var local = TimeZoneInfo.ConvertTime(when, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"{local} {zone.Id}, {utc}");
    }

    /// <summary>Up to two initials for an avatar: "Sam Ortiz" is "SO", "sam" is "S", nothing is "?".</summary>
    public static string Initials(string? name)
```

`src/TechStrap.Admin/Components/Ui/TintedEntry.razor`

Replace

```razor
        {
            <span class="ts-entry-role">@RoleLabel</span>
        }
        <span class="ts-entry-time">@Time</span>
    </div>
    <div class="ts-entry-body">@ChildContent</div>
</article>
```

with

```razor
        {
            <span class="ts-entry-role">@RoleLabel</span>
        }
        <span class="ts-entry-time">@if (When is { } when) { <RelativeTime When="when" /> } else { @Time }</span>
    </div>
    <div class="ts-entry-body">@ChildContent</div>
</article>
```

`src/TechStrap.Admin/Components/Ui/TintedEntry.razor.cs`

Replace

```csharp
    [Parameter, EditorRequired]
    public string Author { get; set; } = string.Empty;

    /// <summary>The time as the caller wants it shown (already formatted).</summary>
    [Parameter]
    public string Time { get; set; } = string.Empty;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }
```

with

```csharp
    [Parameter, EditorRequired]
    public string Author { get; set; } = string.Empty;

    /// <summary>The time as the caller wants it shown (already formatted). Ignored when <see cref="When"/> is set.</summary>
    [Parameter]
    public string Time { get; set; } = string.Empty;

    /// <summary>The instant, shown as a <see cref="RelativeTime"/> (local time, UTC in the tooltip). Messages use this; the style guide's fixed sample times use <see cref="Time"/>.</summary>
    [Parameter]
    public DateTimeOffset? When { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }
```

`src/TechStrap.Admin/Features/Shell/LocalTimeService.cs`

```csharp
using System.Text.RegularExpressions;
using Microsoft.JSInterop;

namespace TechStrap.Admin.Features.Shell;

/// <summary>
/// The agent's time zone (D-042). The browser knows it (<c>Intl</c>, through <c>wwwroot/js/tz.js</c>); the server does not, and the API has no zone setting, so the zone is read once per
/// circuit after the first interactive render. Until then, and whenever the browser gives no usable zone, every time is UTC, which is also what the prerender shows. When the zone arrives,
/// <see cref="Changed"/> fires and <c>RelativeTime</c> draws again. The browser reports an IANA name; <see cref="TimeZoneInfo.FindSystemTimeZoneById(string)"/> reads IANA names on Linux and,
/// with ICU, on Windows. A name that is not found, or not shaped like an IANA name, is UTC. No call here throws for a script failure. Scoped: one per circuit.
/// </summary>
public sealed partial class LocalTimeService(IJSRuntime js) : IAsyncDisposable
{
    public const string ModulePath = "./js/tz.js";

    private const int MaxZoneIdLength = 64;

    private Task? _loading;
    private bool _disposed;
    private IJSObjectReference? _module;

    /// <summary>The agent's zone, or UTC until it is known (and whenever it cannot be known).</summary>
    public TimeZoneInfo Zone { get; private set; } = TimeZoneInfo.Utc;

    /// <summary>True once <see cref="LoadAsync"/> has finished, with the browser's zone or, when there was none, UTC.</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>Raised after the zone was loaded, so every time on screen can draw again.</summary>
    public event Action? Changed;

    /// <summary>The instant in the agent's zone (UTC before the zone is known). Daylight saving is the zone's: the offset belongs to the instant, not to today.</summary>
    public DateTimeOffset ToLocal(DateTimeOffset when) => TimeZoneInfo.ConvertTime(when, Zone);

    /// <summary>Loads once; concurrent and repeated callers share the first call. Never throws for a script failure.</summary>
    public Task LoadAsync() => _loading ??= LoadCoreAsync();

    private async Task LoadCoreAsync()
    {
        string? name = null;
        try
        {
            _module = await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            name = await _module.InvokeAsync<string?>("zone");
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // No script, no circuit or a prerender: keep UTC. The agent loses nothing but the local clock.
        }

        if (_disposed)
        {
            return;
        }

        Zone = Resolve(name);
        IsLoaded = true;
        Changed?.Invoke();
    }

    /// <summary>The zone for an IANA name, or UTC for anything else: null, blank, too long, not shaped like a name, or not known to this machine.</summary>
    public static TimeZoneInfo Resolve(string? id)
    {
        if (id is null || id.Length > MaxZoneIdLength || !ZoneIdShape().IsMatch(id))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException or System.Security.SecurityException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    // Letters, digits, underscore, hyphen, plus and slash: Europe/London, America/Port-au-Prince, Etc/GMT+5. Never a dot or a backslash.
    [GeneratedRegex("^[A-Za-z0-9_+/-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ZoneIdShape();

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The circuit is already gone.
        }
    }
}
```

`src/TechStrap.Admin/Features/Shell/ShellServiceCollectionExtensions.cs`

Replace

```csharp

public static class ShellServiceCollectionExtensions
{
    /// <summary>Registers the per-circuit shell services. Scoped, so each circuit has its own message slot, key listener, browser preferences and failed-email badge.</summary>
    public static IServiceCollection AddShell(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<StatusMessageService>();
        services.AddScoped<ShortcutService>();
        services.AddScoped<PreferencesService>();
        services.AddScoped<FailedEmailCounter>();
        return services;
    }
```

with

```csharp

public static class ShellServiceCollectionExtensions
{
    /// <summary>Registers the per-circuit shell services. Scoped, so each circuit has its own message slot, key listener, browser preferences, time zone and failed-email badge.</summary>
    public static IServiceCollection AddShell(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<StatusMessageService>();
        services.AddScoped<ShortcutService>();
        services.AddScoped<PreferencesService>();
        services.AddScoped<LocalTimeService>();
        services.AddScoped<FailedEmailCounter>();
        return services;
    }
```

`src/TechStrap.Admin/Features/Tickets/MessageBubble.razor`

Replace

```razor
<TintedEntry Kind="Message.Kind" Author="@Message.Author" Time="@TicketDisplay.Relative(Message.At, Time.GetUtcNow())">
    @* The ONE place the Admin renders HTML from the API. MessageDto.BodyHtml is sanitised on the server; nothing else may use MarkupString (MarkupStringSiteTests). *@
    <div class="ts-message-body">@((MarkupString)Message.BodyHtml)</div>
    <AttachmentList Items="Message.Attachments" />
```

with

```razor
<TintedEntry Kind="Message.Kind" Author="@Message.Author" When="Message.At">
    @* The ONE place the Admin renders HTML from the API. MessageDto.BodyHtml is sanitised on the server; nothing else may use MarkupString (MarkupStringSiteTests). *@
    <div class="ts-message-body">@((MarkupString)Message.BodyHtml)</div>
    <AttachmentList Items="Message.Attachments" />
```

`src/TechStrap.Admin/Features/Tickets/MessageBubble.razor.cs`

Replace

```csharp
/// <summary>One message in the timeline, in the carbon tint code: customer, public reply or internal note. Its body is the only HTML the Admin renders from the API.</summary>
public sealed partial class MessageBubble
{
    [Inject]
    private TimeProvider Time { get; set; } = default!;

    [Parameter, EditorRequired]
    public MessageViewModel Message { get; set; } = default!;
}
```

with

```csharp
/// <summary>One message in the timeline, in the carbon tint code: customer, public reply or internal note. Its body is the only HTML the Admin renders from the API.</summary>
public sealed partial class MessageBubble
{
    [Parameter, EditorRequired]
    public MessageViewModel Message { get; set; } = default!;
}
```

`src/TechStrap.Admin/wwwroot/js/tz.js`

```javascript
// The browser's IANA time zone (for example "Europe/London"), for LocalTimeService. The Admin shows every time in the agent's own zone, and the server has
// no other source: the API stores UTC and the profile has no zone (D-042). The Intl object is always handed in, so resolveZone is pure and
// tests/TechStrap.Admin.Tests/js/tz.test.mjs can run it under node:test. Nothing here may throw: a browser without Intl, or one that hides its zone,
// answers null and the page keeps showing UTC.
//
// LocalTimeService (.NET) imports this module once per circuit and calls zone().

/** The zone name Intl reports, or null when there is none (no Intl, an unreadable value, or an empty string). Never throws. */
export function resolveZone(intl) {
    try {
        const zone = intl.DateTimeFormat().resolvedOptions().timeZone;
        return typeof zone === 'string' && zone.length > 0 ? zone : null;
    } catch {
        return null;
    }
}

/** Called once by LocalTimeService after the first interactive render. */
export function zone() {
    return resolveZone(typeof Intl === 'undefined' ? null : Intl);
}
```

- [ ] **Step 4: Run the tests, then prove each pin bites**

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release
node --test tests/TechStrap.Admin.Tests/js/tz.test.mjs
pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/AdminScripts.Tests.ps1
pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/Dockerfiles.Tests.ps1
```

Expected: all PASS (in the scratch copy `TechStrap.Admin.Tests` went from 1346 to 1375 tests; Tasks 1 to 5 add their own).

Mutations, each applied to the finished code, the tests run, and the change reverted. A pin counts only if a named test fails:

| Mutation | Failing tests |
| :-- | :-- |
| `TicketDisplay.Relative`: convert to `TimeZoneInfo.Utc` instead of the zone | `TicketDisplayTests.After_a_week_the_date_is_the_date_in_the_agents_zone` (four rows), `LocalTimeServiceTests.A_time_cell_shows_utc_until_the_zone_arrives_then_draws_again_in_local_time`, `A_message_in_the_timeline_follows_the_zone_too` |
| `LocalTimeService.Resolve`: drop the `catch` around `FindSystemTimeZoneById` | `LocalTimeServiceTests.A_missing_unknown_or_oddly_shaped_zone_is_utc_and_still_counts_as_loaded` (`Mars/Olympus_Mons`) |
| `RelativeTime.OnInitialized`: do not subscribe to `Changed` | `A_time_cell_shows_utc_until_the_zone_arrives_then_draws_again_in_local_time`, `A_message_in_the_timeline_follows_the_zone_too` |
| `ToLocal`: `when.ToOffset(Zone.BaseUtcOffset)` (a fixed offset) | `The_offset_belongs_to_the_instant_so_daylight_saving_is_the_zones_own` (`Europe/London` in July, `America/New_York` after 2026-03-08T07:00Z) |
| `MainLayout`: remove `await LocalTime.LoadAsync();` | `The_layout_asks_the_browser_for_the_zone_once_and_after_the_preferences` |
| `LoadAsync`: forget the first call (`_loading = null` each time) | `Loading_reads_the_browser_zone_once_and_announces_it` |
| `tz.js`: `resolveZone` without its `try/catch` | `tz.test.mjs` "never throws: no Intl, an Intl that throws, or one that hides the zone" |
| `Dockerfile.admin`: install `curl` without `tzdata` | `Dockerfiles.Tests.ps1` "installs tzdata in the runtime image" |

- [ ] **Step 5: Build**

```bash
dotnet build TechStrap.slnx -c Release
```

Expected: 0 warnings, 0 errors. `SourceEncodingTests` and `SourceEscapeTests` pass.

- [ ] **Step 6: Commit**

```bash
git add Dockerfile.admin \
  scripts/tests/AdminScripts.Tests.ps1 \
  scripts/tests/Dockerfiles.Tests.ps1 \
  src/TechStrap.Admin/Components/Layout/MainLayout.razor.cs \
  src/TechStrap.Admin/Components/Ui/RelativeTime.razor \
  src/TechStrap.Admin/Components/Ui/RelativeTime.razor.cs \
  src/TechStrap.Admin/Components/Ui/TicketDisplay.cs \
  src/TechStrap.Admin/Components/Ui/TintedEntry.razor \
  src/TechStrap.Admin/Components/Ui/TintedEntry.razor.cs \
  src/TechStrap.Admin/Features/Shell/LocalTimeService.cs \
  src/TechStrap.Admin/Features/Shell/ShellServiceCollectionExtensions.cs \
  src/TechStrap.Admin/Features/Tickets/MessageBubble.razor \
  src/TechStrap.Admin/Features/Tickets/MessageBubble.razor.cs \
  src/TechStrap.Admin/wwwroot/js/tz.js \
  tests/TechStrap.Admin.Tests/Components/LocalTimeServiceTests.cs \
  tests/TechStrap.Admin.Tests/Components/TicketDisplayTests.cs \
  tests/TechStrap.Admin.Tests/ScriptHostTests.cs \
  tests/TechStrap.Admin.Tests/Support/AdminComponentTest.cs \
  tests/TechStrap.Admin.Tests/js/tz.test.mjs
git diff --cached --stat
git commit -m "feat(admin): show every time in the browser's time zone" -m "The browser's zone is read once per circuit; the prerender and an unknown zone show UTC; the tooltip shows the local and the UTC time. The Admin image installs tzdata." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```

### Task 7: The command palette, the actions menu and the rail: keyboard and screen reader (D-040, D-042, P07-T19)

**Review Focus pins:** (3) the palette never lists or runs an admin command for a plain agent, and a command fires once (`CommandRegistryTests`, `CommandPaletteTests`, `TicketPaletteCommandsTests`); the keys themselves in `shortcuts.test.mjs`, `palette.test.mjs` and `menu.test.mjs`.

**Files:**
- Modify: `scripts/tests/AdminScripts.Tests.ps1`
- Create: `src/TechStrap.Admin/Components/Layout/CommandPalette.razor`
- Create: `src/TechStrap.Admin/Components/Layout/CommandPalette.razor.cs`
- Modify: `src/TechStrap.Admin/Components/Layout/MainLayout.razor`
- Modify: `src/TechStrap.Admin/Components/Layout/MainLayout.razor.cs`
- Modify: `src/TechStrap.Admin/Components/Layout/NavMenu.razor`
- Create: `src/TechStrap.Admin/Components/Layout/RailLink.razor`
- Create: `src/TechStrap.Admin/Components/Layout/RailLink.razor.cs`
- Modify: `src/TechStrap.Admin/Components/Pages/NotFound.razor`
- Modify: `src/TechStrap.Admin/Components/Pages/SignInLanding.razor`
- Modify: `src/TechStrap.Admin/Components/Ui/BrandWindow.razor`
- Create: `src/TechStrap.Admin/Features/Shell/CommandRegistry.cs`
- Create: `src/TechStrap.Admin/Features/Shell/PaletteCopy.cs`
- Modify: `src/TechStrap.Admin/Features/Shell/ShellServiceCollectionExtensions.cs`
- Modify: `src/TechStrap.Admin/Features/Shell/ShortcutCatalog.cs`
- Modify: `src/TechStrap.Admin/Features/Shell/ShortcutService.cs`
- Modify: `src/TechStrap.Admin/Features/Tickets/TicketActions.razor`
- Modify: `src/TechStrap.Admin/Features/Tickets/TicketActions.razor.cs`
- Modify: `src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor.cs`
- Modify: `src/TechStrap.Admin/Features/Tickets/TicketSidebar.razor.cs`
- Modify: `src/TechStrap.Admin/Styles/_brand-window.scss`
- Create: `src/TechStrap.Admin/Styles/_palette.scss`
- Modify: `src/TechStrap.Admin/Styles/app.scss`
- Create: `src/TechStrap.Admin/wwwroot/js/menu.js`
- Create: `src/TechStrap.Admin/wwwroot/js/palette.js`
- Modify: `src/TechStrap.Admin/wwwroot/js/shortcuts.js`
- Modify: `tests/TechStrap.Admin.Tests/Components/BrandWindowTests.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/CommandPaletteTests.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/CommandRegistryTests.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/RailLinkTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/Components/ShellComponentTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/Components/ShortcutServiceTests.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/TicketActionsMenuTests.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/TicketPaletteCommandsTests.cs`
- Create: `tests/TechStrap.Admin.Tests/HeadingHostTests.cs`
- Create: `tests/TechStrap.Admin.Tests/PaletteStyleTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/ScriptHostTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/Support/AdminComponentTest.cs`
- Create: `tests/TechStrap.Admin.Tests/js/menu.test.mjs`
- Create: `tests/TechStrap.Admin.Tests/js/palette.test.mjs`
- Modify: `tests/TechStrap.Admin.Tests/js/shortcuts.test.mjs`

**Interfaces:**
- Consumes:
  - The keyboard layer: `ShortcutService` (`Map`, `Pressed`, `OnKeyAsync`, `ShortcutAction`), `shortcuts.js` (`decide`, `isTyping`), `ShortcutCatalog` (the help list and the status-bar hints), `MainLayout` (owns `ShortcutHelpDialog` and answers the global actions), `StatusBar`.
  - The dialog pattern: `dialog.js` (`open(dialog, focusTarget, handle)`, `close`, and the handle's `NativeClosed` and `EscapePressed`), `ConfirmDialog` (the native `dialog`, `@oncancel` with `preventDefault`), `StatusMessageService`.
  - `AgentSession` (`State`, `IsAdmin`, `Agent`), `QueueViews` and `ShellCopy` (the labels), `TicketDetailPage` (owns the loaded ticket model), `ReplyComposer` (handles `Reply` and `Note`), `TicketSidebar` (`CanAssignToMe`, `AssignToMeAsync`), `TicketActions` (handles `NotSpam`, the overflow menu), `NavMenu`, `BrandWindow`, `NotFound`, `SignInLanding`.
  - Tests: `AdminComponentTest`, `AddAgentShell`, `AgentSessions`, `TestData`, `AdminFactory` host tests, `CompiledCss`, the Pester `It` per node test.
  - Assumed from Tasks 1 to 5:
    - Task 2 turns a 401 into "session expired" mid-session and keeps the page mounted. The palette needs only `AgentSession.State == Ready` and `IsAdmin`, so it simply stops opening while the session is expired; it does not use the banner.
    - Task 2 also hardens `MainLayout.OnAfterRenderAsync` and `NavMenu.OnAfterRenderAsync`. This task edits other methods of both files (`OnShortcutAsync`, the markup), so the edits do not overlap.
    - Task 1's rules (no `HttpClient` in a component, no inline `<script>` or `<style>`) hold: nothing here adds either. Task 4's CSP serves `palette.js` and `menu.js` as same-origin modules, and the palette styles are in the stylesheet.
- Produces:
  - `ShortcutAction.Palette` (Ctrl+K and Cmd+K) and `ShortcutAction.AssignToMe` (no key); `ShortcutService.RaiseAsync(action)`.
  - `PaletteCommand`, `CommandRegistry` (`Available(isAdmin)`, `Register(commands)`, static `Filter`), `PaletteCopy`, `CommandPalette`, `wwwroot/js/palette.js`, `wwwroot/js/menu.js`, `_palette.scss`.
  - `RailLink` (`aria-current="page"`), `BrandWindow.HeadingLevel`, the menu roles of `TicketActions`.
  - `AdminComponentTest.Palette` and `AdminComponentTest.Menu` module doubles.

**Rules:**
1. **Opening.** Ctrl+K or Cmd+K, with no Alt and no Shift, from anywhere: while typing, and when the My settings switch has turned single keys off (a chord is not a single-key shortcut, WCAG 2.1.4). `shortcuts.js` takes the key from the browser (it would focus the address bar) and reports it; `ShortcutService.Map` answers `Palette` before it looks at the single-key rules. It never opens over another modal dialog (a confirmation is waiting for an answer, the help list is open); pressed inside the palette it closes it. `MainLayout` toggles it, and only once `AgentSession.State` is `Ready`: before the API has said who this is, there is nothing to offer and an admin command must never be listed on a guess.
2. **Commands.** A scoped `CommandRegistry` holds the built-in commands: "Queue: Unassigned", "Mine", "Open", "Pending", "All", "Spam" and "My settings" (group "Go to"), and Products, Agents, Tags, Audit and Failed emails (group "Admin", `AdminOnly`). A screen adds its own with `Register(commands)` while it is mounted and disposes the registration when it goes. `TicketDetailPage` registers, for a ticket that is not Closed: "Reply to requester", "Add internal note", "Assign to me" (only when the ticket is not already the agent's) and "Not spam" (only on a flagged ticket), and registers again whenever the model changes, so the list never offers what the ticket no longer allows. Each ticket command raises the shortcut action that does the same thing, so the composer, the sidebar and the actions menu run their own code once.
3. **Who sees what.** `Available(isAdmin)` is the one place that decides: an `AdminOnly` command is never in the list for an agent who is not an Admin, whoever registered it. The palette asks the registry with the current `Session.IsAdmin` on every render, and checks `AdminOnly` again when a command is chosen, because the session can change while the palette is open and a line that is still on screen may be stale.
4. **Filtering and keys.** Typing keeps the commands whose label or group contains every word (case-insensitive) and puts the selection on the first line. ArrowDown and ArrowUp move it and wrap; Home and End jump; Enter runs it; a click runs a line; Esc closes it and the browser returns focus to where it was. `palette.js` takes exactly those five keys from the browser (a script is the only way to stop ArrowUp moving the caret of the input), never a chord and never a key during IME composition.
5. **ARIA.** The input is `role="combobox"` with `aria-controls`, `aria-expanded`, `aria-autocomplete="list"` and `aria-activedescendant`; the list is `role="listbox"`, each line `role="option"` with `aria-selected`; a visually hidden `role="status"` announces "3 commands". The selected line is also marked by a fill, a bar and weight, and by an outline in forced colours.
6. **A command fires once.** The palette closes first and the command runs after the dialog has closed in the browser (a command that moves focus, like Reply, would otherwise find an inert page). The chosen command is cleared before it runs, a second choice while one is pending or running does nothing, and a render after the command finished does not run it again (`OnAfterRenderAsync` runs on every render). A command that throws is logged by exception type only and the status bar says "Couldn't run that command."; a script failure leaves the palette shut. Nothing here may end the circuit.
7. **The actions menu** follows the menu button pattern. The list is `role="menu"` labelled by its button, each entry `role="menuitem"` with `tabindex="-1"`. Opening puts focus on the first item; ArrowDown, ArrowUp, Home and End move between items; ArrowDown on the closed button opens it; Escape closes it, returns focus to the button, and is taken so the page's own Esc ("back to the queue") does not also fire; Tab closes it and lets focus move on. `menu.js` does the keys and tells the component through `OpenMenu` and `CloseMenu`.
8. **The rail** says which page is current with `aria-current="page"` (the framework's `NavLink` only adds a class, which a screen reader never hears, although `_shell.scss` already styled `[aria-current="page"]`). The link is current on its address and below it, compared on whole segments (`/queue/mine` is under `/queue`, `/queued` is not).
9. **Headings.** `BrandWindow.HeadingLevel` (default 2). The 404 page and the sign-in landing page, where the window is the whole page, pass 1, so every screen has one `h1`.
10. **Docs of the keys.** The help list and the status bar list Ctrl+K.

- [ ] **Step 1: Write the failing tests**

The node tests first (`shortcuts.test.mjs` gains the chord; `palette.test.mjs` and `menu.test.mjs` are new), then the bUnit tests. Every palette test goes through `MainLayout`, which owns the palette, with the key arriving the way `shortcuts.js` reports it (`PressAsync("k", ctrl: true, typing: true)`); the two new module doubles are in `AdminComponentTest`.

`scripts/tests/AdminScripts.Tests.ps1`

Replace

```powershell
        $LASTEXITCODE | Should -Be 0 -Because $output
    }

    It 'passes the node:test suite for the dialog module (Esc and a stray native close can never dismiss a locked dialog)' {
        if (-not $script:Node) {
            Set-ItResult -Skipped -Because 'node is not installed, so the dialog.js tests cannot run'
```

with

```powershell
        $LASTEXITCODE | Should -Be 0 -Because $output
    }

    It 'passes the node:test suite for the command palette keys (arrows and Enter are taken from the browser, typing is not)' {
        if (-not $script:Node) {
            Set-ItResult -Skipped -Because 'node is not installed, so the palette.js tests cannot run'
            return
        }

        $testFile = Join-Path $script:RepoRoot 'tests/TechStrap.Admin.Tests/js/palette.test.mjs'

        $output = & node --test $testFile 2>&1 | Out-String

        $LASTEXITCODE | Should -Be 0 -Because $output
    }

    It 'passes the node:test suite for the menu button keys (arrows, Home, End, Escape and Tab)' {
        if (-not $script:Node) {
            Set-ItResult -Skipped -Because 'node is not installed, so the menu.js tests cannot run'
            return
        }

        $testFile = Join-Path $script:RepoRoot 'tests/TechStrap.Admin.Tests/js/menu.test.mjs'

        $output = & node --test $testFile 2>&1 | Out-String

        $LASTEXITCODE | Should -Be 0 -Because $output
    }

    It 'passes the node:test suite for the dialog module (Esc and a stray native close can never dismiss a locked dialog)' {
        if (-not $script:Node) {
            Set-ItResult -Skipped -Because 'node is not installed, so the dialog.js tests cannot run'
```

`tests/TechStrap.Admin.Tests/Components/BrandWindowTests.cs`

Replace

```csharp
    }

    [Fact]
    public void The_mascot_is_the_96px_SVG_head_mark_and_decorative()
    {
        var mark = RenderWindow().Find(".ts-window-body img");
```

with

```csharp
    }

    [Fact]
    public void The_heading_is_an_h2_by_default_and_an_h1_when_the_window_is_the_whole_page()
    {
        var inside = RenderWindow();
        var standalone = Render<BrandWindow>(p => p
            .Add(w => w.Title, "ERROR 404")
            .Add(w => w.Heading, "This page fell out of its strap.")
            .Add(w => w.HeadingLevel, 1));

        inside.FindAll(".ts-window-body h1").ShouldBeEmpty();
        inside.Find(".ts-window-body h2").TextContent.ShouldBe("All caught up");
        standalone.FindAll(".ts-window-body h2").ShouldBeEmpty();
        standalone.Find(".ts-window-body h1").TextContent.ShouldBe("This page fell out of its strap.");
    }

    [Fact]
    public void The_mascot_is_the_96px_SVG_head_mark_and_decorative()
    {
        var mark = RenderWindow().Find(".ts-window-body img");
```

`tests/TechStrap.Admin.Tests/Components/CommandPaletteTests.cs`

```csharp
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Layout;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// Review Focus 3, the command palette: Ctrl+K opens it from anywhere, a plain agent is never shown an admin command, ArrowDown, ArrowUp and Enter work the list, and a command fires once.
/// Every test goes through <c>MainLayout</c>, which owns the palette, with the key arriving the way <c>shortcuts.js</c> reports it.
/// </summary>
public sealed class CommandPaletteTests : AdminComponentTest
{
    private static readonly CancellationToken Ct = Xunit.TestContext.Current.CancellationToken;

    private IAgentsClient _agents = default!;
    private int _ran;

    private CommandRegistry Registry => Services.GetRequiredService<CommandRegistry>();

    private AgentSession Session => Services.GetRequiredService<AgentSession>();

    private async Task<IRenderedComponent<MainLayout>> RenderLayoutAsync(string role = AgentRoles.Agent)
    {
        _agents = this.AddAgentShell(role);
        await Session.EnsureLoadedAsync(Ct);
        return Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, (RenderFragment)(b => { })));
    }

    private async Task<IRenderedComponent<MainLayout>> OpenPaletteAsync(string role = AgentRoles.Agent)
    {
        var cut = await RenderLayoutAsync(role);
        await PressCtrlKAsync();
        cut.WaitForAssertion(() => cut.FindAll("dialog[data-palette] [role=option]").ShouldNotBeEmpty());
        return cut;
    }

    private Task PressCtrlKAsync() => PressAsync("k", ctrl: true, typing: true);

    private static IReadOnlyList<string> Options(IRenderedComponent<MainLayout> cut) =>
        cut.FindAll("dialog[data-palette] [role=option] .ts-palette-label").Select(o => o.TextContent).ToList();

    private static string? Selected(IRenderedComponent<MainLayout> cut) =>
        cut.FindAll("dialog[data-palette] [role=option]").SingleOrDefault(o => o.GetAttribute("aria-selected") == "true")?.QuerySelector(".ts-palette-label")?.TextContent;

    private static Task KeyAsync(IRenderedComponent<MainLayout> cut, string key) =>
        cut.InvokeAsync(() => cut.FindComponent<CommandPalette>().Instance.NavigateKey(key));

    private PaletteCommand Counting(string id = "probe", bool adminOnly = false, Func<Task>? run = null) =>
        new(id, "Probe " + id, "Test", run ?? (() =>
        {
            _ran++;
            return Task.CompletedTask;
        }), AdminOnly: adminOnly);

    [Fact]
    public async Task Ctrl_K_opens_the_palette_with_focus_on_the_input_and_closes_it_again()
    {
        var cut = await RenderLayoutAsync();
        Dialogs.VerifyNotInvoke("open");

        await PressCtrlKAsync();

        cut.WaitForAssertion(() => Dialogs.VerifyInvoke("open", 1));
        var dialogOpen = Dialogs.Invocations["open"].Single();
        dialogOpen.Arguments.Count.ShouldBe(3);
        cut.Find("dialog[data-palette] input[role=combobox]").GetAttribute("aria-expanded").ShouldBe("true");

        await PressCtrlKAsync();

        cut.WaitForAssertion(() => Dialogs.VerifyInvoke("close", 1));
    }

    [Fact]
    public async Task Ctrl_K_works_while_single_key_shortcuts_are_switched_off()
    {
        var cut = await RenderLayoutAsync();
        ShortcutService.SingleKeyEnabled = false;

        await PressCtrlKAsync();

        cut.WaitForAssertion(() => Dialogs.VerifyInvoke("open", 1));
    }

    [Fact]
    public async Task A_plain_agent_is_offered_the_queue_views_and_my_settings_and_never_an_admin_page()
    {
        var cut = await OpenPaletteAsync(AgentRoles.Agent);

        Options(cut).ShouldBe(
        [
            "Queue: Unassigned", "Queue: Mine", "Queue: Open", "Queue: Pending", "Queue: All", "Queue: Spam",
            "My settings",
        ]);
        var palette = cut.Find("dialog[data-palette]").InnerHtml;
        foreach (var admin in new[] { "Products", "Agents", "Tags", "Audit", "Failed emails", "/settings", "/ops" })
        {
            palette.ShouldNotContain(admin);
        }
    }

    [Fact]
    public async Task A_plain_agent_never_sees_an_admin_command_that_a_screen_registered()
    {
        var cut = await RenderLayoutAsync(AgentRoles.Agent);
        Registry.Register([Counting("plain"), Counting("secret", adminOnly: true)]);

        await PressCtrlKAsync();

        cut.WaitForAssertion(() => Options(cut).ShouldContain("Probe plain"));
        Options(cut).ShouldNotContain("Probe secret");
    }

    [Fact]
    public async Task An_admin_is_offered_the_admin_pages_too_and_each_shows_its_group()
    {
        var cut = await OpenPaletteAsync(AgentRoles.Admin);

        Options(cut).ShouldContain("Products");
        Options(cut).ShouldContain("Failed emails");
        Options(cut).Count.ShouldBe(12);
        cut.FindAll(".ts-palette-group").Select(g => g.TextContent).Distinct().ShouldBe(["Go to", "Admin"]);
    }

    [Fact]
    public async Task It_does_not_open_until_the_api_has_said_who_the_agent_is()
    {
        _agents = this.AddAgentShell();
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<AgentDto>("agent-inactive", "Not an agent.", ResultErrorKind.Forbidden));
        await Session.EnsureLoadedAsync(Ct);
        var cut = Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, (RenderFragment)(b => { })));

        await PressCtrlKAsync();

        Session.State.ShouldBe(AgentSessionState.NoAccess);
        Dialogs.VerifyNotInvoke("open");
        cut.FindAll("dialog[data-palette] [role=option]").ShouldBeEmpty();
    }

    [Fact]
    public async Task Typing_filters_the_list_and_selects_the_first_match()
    {
        var cut = await OpenPaletteAsync();

        cut.Find("dialog[data-palette] input").Input("mi");

        Options(cut).ShouldBe(["Queue: Mine"]);
        Selected(cut).ShouldBe("Queue: Mine");
        cut.Find("dialog[data-palette] [role=status]").TextContent.ShouldBe("1 command");
    }

    [Fact]
    public async Task No_match_says_so_and_Enter_does_nothing()
    {
        var cut = await OpenPaletteAsync();
        var navigation = Services.GetRequiredService<NavigationManager>();
        var before = navigation.Uri;

        cut.Find("dialog[data-palette] input").Input("zzzz");
        await KeyAsync(cut, "Enter");

        cut.FindAll("dialog[data-palette] [role=option]").ShouldBeEmpty();
        cut.Find(".ts-palette-empty").TextContent.ShouldBe("No command matches.");
        cut.Find("dialog[data-palette] input").HasAttribute("aria-activedescendant").ShouldBeFalse();
        navigation.Uri.ShouldBe(before);
    }

    [Fact]
    public async Task The_arrow_keys_move_the_selection_and_wrap_and_the_input_names_the_active_option()
    {
        var cut = await OpenPaletteAsync();
        Selected(cut).ShouldBe("Queue: Unassigned");

        await KeyAsync(cut, "ArrowDown");
        Selected(cut).ShouldBe("Queue: Mine");
        cut.Find("dialog[data-palette] input").GetAttribute("aria-activedescendant")
            .ShouldBe(cut.FindAll("[role=option]").Single(o => o.GetAttribute("aria-selected") == "true").Id);

        await KeyAsync(cut, "ArrowUp");
        await KeyAsync(cut, "ArrowUp");
        Selected(cut).ShouldBe("My settings");

        await KeyAsync(cut, "ArrowDown");
        Selected(cut).ShouldBe("Queue: Unassigned");

        await KeyAsync(cut, "End");
        Selected(cut).ShouldBe("My settings");

        await KeyAsync(cut, "Home");
        Selected(cut).ShouldBe("Queue: Unassigned");
    }

    [Fact]
    public async Task Typing_puts_the_selection_back_on_the_first_line()
    {
        var cut = await OpenPaletteAsync();
        await KeyAsync(cut, "End");

        cut.Find("dialog[data-palette] input").Input("queue");

        Selected(cut).ShouldBe("Queue: Unassigned");
    }

    [Fact]
    public async Task Enter_runs_the_selected_command_after_the_dialog_has_closed()
    {
        var cut = await OpenPaletteAsync();
        var navigation = Services.GetRequiredService<NavigationManager>();
        await KeyAsync(cut, "ArrowDown");

        await KeyAsync(cut, "Enter");

        cut.WaitForAssertion(() => new Uri(navigation.Uri).AbsolutePath.ShouldBe("/queue/mine"));
        Dialogs.VerifyInvoke("close", 1);
    }

    [Fact]
    public async Task Clicking_a_line_runs_it()
    {
        var cut = await OpenPaletteAsync();
        var navigation = Services.GetRequiredService<NavigationManager>();

        cut.FindAll("[role=option]").Single(o => o.TextContent.Contains("My settings", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => new Uri(navigation.Uri).AbsolutePath.ShouldBe("/account/notifications"));
    }

    [Fact]
    public async Task A_command_fires_exactly_once_even_when_Enter_arrives_twice_and_the_page_renders_again_afterwards()
    {
        var cut = await RenderLayoutAsync();
        Registry.Register([Counting()]);
        await PressCtrlKAsync();
        cut.WaitForAssertion(() => Options(cut).ShouldContain("Probe probe"));
        cut.Find("dialog[data-palette] input").Input("probe");

        await cut.InvokeAsync(() => Task.WhenAll(
            cut.FindComponent<CommandPalette>().Instance.NavigateKey("Enter"),
            cut.FindComponent<CommandPalette>().Instance.NavigateKey("Enter")));
        cut.WaitForAssertion(() => _ran.ShouldBe(1));

        // The palette draws again (every render of the layout does that): the finished command must not run a second time.
        cut.FindComponent<CommandPalette>().Render();
        cut.FindComponent<CommandPalette>().Render();
        await cut.InvokeAsync(() => Task.CompletedTask);

        _ran.ShouldBe(1);
    }

    [Fact]
    public async Task A_stale_click_on_an_admin_line_does_nothing_once_the_session_is_no_longer_an_admins()
    {
        var cut = await RenderLayoutAsync(AgentRoles.Admin);
        Registry.Register([Counting("secret", adminOnly: true)]);
        await PressCtrlKAsync();
        cut.WaitForAssertion(() => Options(cut).ShouldContain("Probe secret"));
        var staleLine = cut.FindAll("[role=option]").Single(o => o.TextContent.Contains("Probe secret", StringComparison.Ordinal));

        // The API now says this agent is no longer an Admin; the palette has not drawn again yet, so the line is still on screen.
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(new AgentDto(Guid.NewGuid(), "Sam", "sam@orbitly.test", AgentRoles.Agent, true, null, null)));
        await cut.InvokeAsync(() => Session.ReloadAsync(Ct));
        Session.IsAdmin.ShouldBeFalse();
        staleLine.Click();
        await cut.InvokeAsync(() => Task.CompletedTask);

        _ran.ShouldBe(0);
    }

    [Fact]
    public async Task Esc_closes_the_palette_and_a_new_opening_starts_empty()
    {
        var cut = await OpenPaletteAsync();
        cut.Find("dialog[data-palette] input").Input("mine");

        cut.Find("dialog[data-palette]").TriggerEvent("oncancel", EventArgs.Empty);
        cut.WaitForAssertion(() => Dialogs.VerifyInvoke("close", 1));

        await PressCtrlKAsync();
        cut.WaitForAssertion(() => Dialogs.VerifyInvoke("open", 2));
        cut.Find("dialog[data-palette] input").GetAttribute("value").ShouldBeNullOrEmpty();
        Options(cut).Count.ShouldBe(7);
    }

    [Fact]
    public async Task A_browser_that_closed_the_dialog_by_itself_closes_the_palette()
    {
        var cut = await OpenPaletteAsync();

        await cut.InvokeAsync(() => cut.FindComponent<CommandPalette>().Instance.NativeClosed());

        cut.WaitForAssertion(() => Options(cut).ShouldBeEmpty());
    }

    [Fact]
    public async Task A_script_that_fails_leaves_the_palette_shut_and_the_page_alive()
    {
        var cut = await RenderLayoutAsync();
        Dialogs.SetupVoid("open", _ => true).SetException(new JSException("no dialog"));

        await PressCtrlKAsync();
        await cut.InvokeAsync(() => Task.CompletedTask);

        cut.WaitForAssertion(() => Options(cut).ShouldBeEmpty());
        cut.Find(".ts-brand").ShouldNotBeNull();
        await PressCtrlKAsync();
        await cut.InvokeAsync(() => Task.CompletedTask);
    }

    [Fact]
    public async Task A_command_that_throws_is_reported_in_the_status_bar_and_does_not_end_the_circuit()
    {
        var cut = await RenderLayoutAsync();
        Registry.Register([Counting("boom", run: () => throw new InvalidOperationException("secret detail"))]);
        await PressCtrlKAsync();
        cut.WaitForAssertion(() => Options(cut).ShouldContain("Probe boom"));
        cut.Find("dialog[data-palette] input").Input("boom");

        await KeyAsync(cut, "Enter");

        cut.WaitForAssertion(() => StatusMessages.Current.ShouldBe("Couldn't run that command."));
        cut.Find(".ts-statusbar").TextContent.ShouldNotContain("secret detail");
    }
}
```

`tests/TechStrap.Admin.Tests/Components/CommandRegistryTests.cs`

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Tests.Support;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// Review Focus 3, at the registry: the list is the one place that decides who sees what. An admin command is never in the list for an agent who is not an Admin, whoever registered it.
/// </summary>
public sealed class CommandRegistryTests : AdminComponentTest
{
    private CommandRegistry Registry => Services.GetRequiredService<CommandRegistry>();

    private static PaletteCommand Probe(string id = "probe", bool adminOnly = false, Func<Task>? run = null) =>
        new(id, "Probe " + id, "Test", run ?? (() => Task.CompletedTask), AdminOnly: adminOnly);

    [Fact]
    public void A_plain_agent_is_offered_the_queue_views_and_my_settings_and_no_admin_page()
    {
        var labels = Registry.Available(isAdmin: false).Select(c => c.Label).ToList();

        labels.ShouldBe(
        [
            "Queue: Unassigned", "Queue: Mine", "Queue: Open", "Queue: Pending", "Queue: All", "Queue: Spam",
            "My settings",
        ]);
        Registry.Available(isAdmin: false).ShouldAllBe(c => !c.AdminOnly);
    }

    [Fact]
    public void An_admin_is_offered_the_five_admin_pages_as_well()
    {
        var admin = Registry.Available(isAdmin: true).Where(c => c.AdminOnly).Select(c => c.Label).ToList();

        admin.ShouldBe(["Products", "Agents", "Tags", "Audit", "Failed emails"]);
        Registry.Available(isAdmin: true).Count.ShouldBe(Registry.Available(isAdmin: false).Count + 5);
    }

    [Fact]
    public async Task Every_built_in_command_goes_where_its_label_says()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        var paths = new Dictionary<string, string>();

        foreach (var command in Registry.Available(isAdmin: true))
        {
            await command.RunAsync();
            paths[command.Id] = new Uri(navigation.Uri).AbsolutePath;
        }

        paths["go-queue-unassigned"].ShouldBe("/queue/unassigned");
        paths["go-queue-mine"].ShouldBe("/queue/mine");
        paths["go-queue-spam"].ShouldBe("/queue/spam");
        paths["go-my-settings"].ShouldBe("/account/notifications");
        paths["go-products"].ShouldBe("/settings/products");
        paths["go-agents"].ShouldBe("/settings/agents");
        paths["go-tags"].ShouldBe("/settings/tags");
        paths["go-audit"].ShouldBe("/settings/audit");
        paths["go-failed-emails"].ShouldBe("/ops/dead-letters");
    }

    [Fact]
    public void A_registered_command_is_offered_until_its_registration_is_disposed()
    {
        var registration = Registry.Register([Probe()]);

        Registry.Available(false).ShouldContain(c => c.Id == "probe");

        registration.Dispose();
        registration.Dispose();

        Registry.Available(false).ShouldNotContain(c => c.Id == "probe");
    }

    [Fact]
    public void An_admin_only_command_that_a_screen_registered_is_hidden_from_an_agent_too()
    {
        Registry.Register([Probe("plain"), Probe("secret", adminOnly: true)]);

        Registry.Available(isAdmin: false).Where(c => c.Group == "Test").Select(c => c.Id).ShouldBe(["plain"]);
        Registry.Available(isAdmin: true).Where(c => c.Group == "Test").Select(c => c.Id).ShouldBe(["plain", "secret"]);
    }

    [Fact]
    public void Two_screens_registering_do_not_remove_each_others_commands()
    {
        var first = Registry.Register([Probe("one")]);
        Registry.Register([Probe("two")]);

        first.Dispose();

        Registry.Available(false).Where(c => c.Group == "Test").Select(c => c.Id).ShouldBe(["two"]);
    }

    [Theory]
    [InlineData("", 3)]
    [InlineData(null, 3)]
    [InlineData("   ", 3)]
    [InlineData("mine", 1)]
    [InlineData("MINE", 1)]
    [InlineData("queue", 2)]
    [InlineData("ticket reply", 1)]
    [InlineData("reply ticket", 1)]
    [InlineData("go to", 1)]
    [InlineData("nothing like this", 0)]
    public void Filtering_keeps_the_commands_that_contain_every_word_in_their_label_or_group(string? query, int expected)
    {
        IReadOnlyList<PaletteCommand> commands =
        [
            new("a", "Queue: Mine", "Go to", () => Task.CompletedTask),
            new("b", "Queue: Open", "Navigation", () => Task.CompletedTask),
            new("c", "Reply to requester", "Ticket", () => Task.CompletedTask),
        ];

        CommandRegistry.Filter(commands, query).Count.ShouldBe(expected);
    }
}
```

`tests/TechStrap.Admin.Tests/Components/RailLinkTests.cs`

```csharp
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Components.Layout;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The rail says which page is current with <c>aria-current="page"</c>, for a screen reader and for the rail's own style.</summary>
public sealed class RailLinkTests : AdminComponentTest
{
    private static readonly CancellationToken Ct = Xunit.TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("queue", "/queue", true)]
    [InlineData("queue/mine", "/queue", true)]
    [InlineData("queue/mine?search=reset&page=2", "/queue", true)]
    [InlineData("QUEUE/Mine", "/queue", true)]
    [InlineData("queue#top", "/queue", true)]
    [InlineData("settings/products/abc", "/settings/products", true)]
    [InlineData("queued", "/queue", false)]
    [InlineData("queue-archive/old", "/queue", false)]
    [InlineData("settings/agents", "/settings/products", false)]
    [InlineData("tickets/ORB-42", "/queue", false)]
    [InlineData("", "/queue", false)]
    [InlineData("account/notifications", "/account/notifications", true)]
    public void The_address_is_current_when_it_is_the_link_or_below_it_on_whole_segments(string relativePath, string href, bool expected) =>
        RailLink.IsCurrentAddress(relativePath, href).ShouldBe(expected);

    [Fact]
    public void The_link_in_the_current_section_is_marked_and_the_others_are_not()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/settings/products/abc?tab=keys");

        var cut = Render<RailLink>(p => p.Add(l => l.Href, "/settings/products").AddChildContent("Products"));
        var other = Render<RailLink>(p => p.Add(l => l.Href, "/settings/agents").AddChildContent("Agents"));

        cut.Find("a.ts-rail-link").GetAttribute("aria-current").ShouldBe("page");
        cut.Find("a.ts-rail-link").GetAttribute("href").ShouldBe("/settings/products");
        other.Find("a.ts-rail-link").HasAttribute("aria-current").ShouldBeFalse();
    }

    [Fact]
    public async Task The_mark_moves_when_the_agent_navigates()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/queue");
        var cut = Render<RailLink>(p => p.Add(l => l.Href, "/settings/tags").AddChildContent("Tags"));
        cut.Find("a").HasAttribute("aria-current").ShouldBeFalse();

        await cut.InvokeAsync(() => navigation.NavigateTo("/settings/tags"));

        cut.WaitForAssertion(() => cut.Find("a").GetAttribute("aria-current").ShouldBe("page"));

        await cut.InvokeAsync(() => navigation.NavigateTo("/queue/mine"));

        cut.WaitForAssertion(() => cut.Find("a").HasAttribute("aria-current").ShouldBeFalse());
    }

    [Fact]
    public async Task In_the_rail_exactly_one_link_is_current_and_it_is_the_one_for_the_page()
    {
        this.AddAgentShell(AgentRoles.Admin);
        await Services.GetRequiredService<AgentSession>().EnsureLoadedAsync(Ct);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/settings/audit");

        var cut = Render<NavMenu>(p => p.SignedIn());

        var current = cut.FindAll("a.ts-rail-link[aria-current=page]");
        current.Count.ShouldBe(1);
        current[0].GetAttribute("href").ShouldBe("/settings/audit");
    }
}
```

`tests/TechStrap.Admin.Tests/Components/ShellComponentTests.cs` (2 edits)

Replace

```csharp
        var cut = Render<StatusBar>();

        cut.FindAll(".ts-statusbar-hints li").Select(li => li.TextContent.Trim()).ShouldBe(
            ["j k move", "Enter open", "r reply", "n note", "e assign", "/ search", "? help"]);
        cut.Find("p.ts-statusbar-message").GetAttribute("role").ShouldBe("status");
        cut.Find("p.ts-statusbar-message").TextContent.ShouldBeEmpty();
    }
```

with

```csharp
        var cut = Render<StatusBar>();

        cut.FindAll(".ts-statusbar-hints li").Select(li => li.TextContent.Trim()).ShouldBe(
            ["j k move", "Enter open", "r reply", "n note", "e assign", "/ search", "Ctrl K commands", "? help"]);
        cut.Find("p.ts-statusbar-message").GetAttribute("role").ShouldBe("status");
        cut.Find("p.ts-statusbar-message").TextContent.ShouldBeEmpty();
    }
```

Replace

```csharp
        cut.WaitForAssertion(() => Dialogs.VerifyInvoke("open", 1));

        cut.Find("dialog.ts-dialog h2").TextContent.ShouldBe("Keyboard shortcuts");
        cut.FindAll(".ts-shortcut-table tbody tr").Count.ShouldBe(10);
        cut.FindAll(".ts-shortcut-table kbd").Select(k => k.TextContent).ShouldContain("u");

        cut.Find("dialog button").Click();
```

with

```csharp
        cut.WaitForAssertion(() => Dialogs.VerifyInvoke("open", 1));

        cut.Find("dialog.ts-dialog h2").TextContent.ShouldBe("Keyboard shortcuts");
        cut.FindAll(".ts-shortcut-table tbody tr").Count.ShouldBe(11);
        cut.FindAll(".ts-shortcut-table kbd").Select(k => k.TextContent).ShouldContain("u");
        cut.FindAll(".ts-shortcut-table kbd").Select(k => k.TextContent).ShouldContain("Ctrl+K / Cmd+K");

        cut.Find("dialog button").Click();
```

`tests/TechStrap.Admin.Tests/Components/ShortcutServiceTests.cs` (2 edits)

Replace

```csharp
        ShortcutService.Map(Key(key), singleKeyEnabled: false).ShouldBeNull();
        ShortcutService.Map(Key("Escape"), singleKeyEnabled: false).ShouldBe(ShortcutAction.Escape);
        ShortcutService.Map(Key("Enter", ctrl: true, typing: true, scope: ShortcutService.ComposerScope), singleKeyEnabled: false).ShouldBe(ShortcutAction.Send);
    }

    [Fact]
```

with

```csharp
        ShortcutService.Map(Key(key), singleKeyEnabled: false).ShouldBeNull();
        ShortcutService.Map(Key("Escape"), singleKeyEnabled: false).ShouldBe(ShortcutAction.Escape);
        ShortcutService.Map(Key("Enter", ctrl: true, typing: true, scope: ShortcutService.ComposerScope), singleKeyEnabled: false).ShouldBe(ShortcutAction.Send);
    }

    [Theory]
    [InlineData("k", false, true)]
    [InlineData("K", false, true)]
    [InlineData("k", true, false)]
    [InlineData("K", true, false)]
    public void Ctrl_K_and_Cmd_K_open_the_palette_from_anywhere_whatever_the_single_key_switch_says(string key, bool meta, bool ctrl)
    {
        var press = new KeyPress(key, ctrl, meta, Alt: false, Typing: true, OnBody: false, Scope: null);

        ShortcutService.Map(press, singleKeyEnabled: true).ShouldBe(ShortcutAction.Palette);
        ShortcutService.Map(press, singleKeyEnabled: false).ShouldBe(ShortcutAction.Palette);
    }

    [Fact]
    public void A_bare_k_is_still_move_up_and_Ctrl_Alt_K_and_other_chords_are_nothing()
    {
        ShortcutService.Map(Key("k"), singleKeyEnabled: true).ShouldBe(ShortcutAction.MoveUp);
        ShortcutService.Map(Key("k", ctrl: true, alt: true), singleKeyEnabled: true).ShouldBeNull();
        ShortcutService.Map(Key("j", ctrl: true), singleKeyEnabled: true).ShouldBeNull();
        ShortcutService.Map(Key("Enter", ctrl: true, scope: ShortcutService.ComposerScope), singleKeyEnabled: true).ShouldBe(ShortcutAction.Send);
    }

    [Fact]
    public async Task Raising_an_action_tells_every_subscriber_in_order_without_a_key_press()
    {
        var seen = new List<string>();
        ShortcutService.Pressed += action =>
        {
            seen.Add("first " + action);
            return Task.CompletedTask;
        };
        ShortcutService.Pressed += action =>
        {
            seen.Add("second " + action);
            return Task.CompletedTask;
        };

        await ShortcutService.RaiseAsync(ShortcutAction.AssignToMe);

        seen.ShouldBe(["first AssignToMe", "second AssignToMe"]);
        await Should.NotThrowAsync(() => new ShortcutService(Substitute.For<IJSRuntime>()).RaiseAsync(ShortcutAction.Reply));
    }

    [Fact]
```

Replace

```csharp
    {
        var listed = ShortcutCatalog.All.SelectMany(entry => entry.Keys.Split(" / ")).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var key in new[] { "j", "k", "Enter", "/", "r", "n", "e", "u", "?", "Esc", "Ctrl+Enter" })
        {
            listed.ShouldContain(key);
        }
```

with

```csharp
    {
        var listed = ShortcutCatalog.All.SelectMany(entry => entry.Keys.Split(" / ")).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var key in new[] { "j", "k", "Enter", "/", "r", "n", "e", "u", "?", "Esc", "Ctrl+Enter", "Ctrl+K", "Cmd+K" })
        {
            listed.ShouldContain(key);
        }
```

`tests/TechStrap.Admin.Tests/Components/TicketActionsMenuTests.cs`

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NSubstitute;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// The ticket's "More actions" menu follows the WAI-ARIA menu button pattern: roles, a focus move on open, and keys handled by <c>menu.js</c>, which tells the component when to open or close.
/// The key handling itself is pinned by <c>menu.test.mjs</c>; here is the markup and the hand-over between the script and the component.
/// </summary>
public sealed class TicketActionsMenuTests : AdminComponentTest
{
    public TicketActionsMenuTests()
    {
        Services.AddSingleton(Substitute.For<ITicketsClient>());
        Services.AddSingleton(Substitute.For<IRequestersClient>());
        Services.AddSingleton(_ => AgentSessions.SignedIn(admin: true));
    }

    private IRenderedComponent<TicketActions> RenderActions() =>
        Render<TicketActions>(p => p.Add(c => c.Ticket, TestData.Model()));

    [Fact]
    public void The_button_names_the_menu_and_the_menu_is_labelled_by_the_button()
    {
        var cut = RenderActions();

        var button = cut.Find(".ts-actions > button");
        var menu = cut.Find("ul.ts-menu");
        button.GetAttribute("aria-haspopup").ShouldBe("menu");
        button.GetAttribute("aria-controls").ShouldBe(menu.Id);
        menu.GetAttribute("role").ShouldBe("menu");
        menu.GetAttribute("aria-labelledby").ShouldBe(button.Id);
        button.Id.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Every_entry_is_a_menu_item_that_the_script_focuses_and_the_tab_key_skips()
    {
        var cut = RenderActions();

        cut.FindAll("ul.ts-menu > li").ShouldAllBe(li => li.GetAttribute("role") == "none");
        var items = cut.FindAll("ul.ts-menu button");
        items.Count.ShouldBe(3);
        items.ShouldAllBe(button => button.GetAttribute("role") == "menuitem" && button.GetAttribute("tabindex") == "-1");
    }

    [Fact]
    public async Task The_keyboard_script_is_attached_once_to_the_wrapper()
    {
        var cut = RenderActions();
        await cut.InvokeAsync(() => Task.CompletedTask);

        Menu.VerifyInvoke("attach", 1);

        cut.Find(".ts-actions > button").Click();
        cut.Find(".ts-actions > button").Click();

        Menu.VerifyInvoke("attach", 1);
    }

    [Fact]
    public void Opening_with_a_click_moves_focus_to_the_first_item_and_closing_does_not()
    {
        var cut = RenderActions();

        cut.Find(".ts-actions > button").Click();

        cut.WaitForAssertion(() => Menu.VerifyInvoke("focusFirst", 1));
        cut.Find("ul.ts-menu").HasAttribute("hidden").ShouldBeFalse();
        cut.Find(".ts-actions > button").GetAttribute("aria-expanded").ShouldBe("true");

        cut.Find(".ts-actions > button").Click();

        cut.Find("ul.ts-menu").HasAttribute("hidden").ShouldBeTrue();
        Menu.VerifyInvoke("focusFirst", 1);
    }

    [Fact]
    public async Task ArrowDown_on_the_closed_button_opens_the_menu_and_focuses_the_first_item()
    {
        var cut = RenderActions();

        await cut.InvokeAsync(() => cut.Instance.OpenMenu());

        cut.Find("ul.ts-menu").HasAttribute("hidden").ShouldBeFalse();
        cut.WaitForAssertion(() => Menu.VerifyInvoke("focusFirst", 1));
    }

    [Fact]
    public async Task Escape_and_Tab_close_the_menu()
    {
        var cut = RenderActions();
        cut.Find(".ts-actions > button").Click();

        await cut.InvokeAsync(() => cut.Instance.CloseMenu());

        cut.Find("ul.ts-menu").HasAttribute("hidden").ShouldBeTrue();
        cut.Find(".ts-actions > button").GetAttribute("aria-expanded").ShouldBe("false");
    }

    [Fact]
    public async Task A_script_that_fails_never_breaks_the_menu_for_the_mouse()
    {
        Menu.SetupVoid("attach", _ => true).SetException(new JSException("no script"));
        var cut = RenderActions();
        await cut.InvokeAsync(() => Task.CompletedTask);

        cut.Find(".ts-actions > button").Click();

        cut.Find("ul.ts-menu").HasAttribute("hidden").ShouldBeFalse();
    }
}
```

`tests/TechStrap.Admin.Tests/Components/TicketPaletteCommandsTests.cs`

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// The ticket page's commands in the palette: they are there only while a ticket that allows them is on screen, they follow the ticket's state, and each one raises the same
/// shortcut as its key, so the composer, the sidebar and the actions menu run their own code. None of them is an admin command.
/// </summary>
public sealed class TicketPaletteCommandsTests : AdminComponentTest
{
    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();

    public TicketPaletteCommandsTests()
    {
        var products = Substitute.For<IProductsClient>();
        var agents = Substitute.For<IAgentsClient>();
        var tags = Substitute.For<ITagsClient>();
        products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.Product()]));
        agents.ListAllAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<AgentListItemDto>>([TestData.Agent("Sam Ortiz", TestData.SamAgentId)]));
        tags.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<TagDto>>([TestData.Tag()]));
        Services.AddSingleton(_tickets);
        Services.AddSingleton(products);
        Services.AddSingleton(agents);
        Services.AddSingleton(tags);
        Services.AddTicketFeatures();
        Services.AddSingleton(AgentSessions.SignedIn());
        Services.AddSingleton(Substitute.For<IRequestersClient>());
    }

    private CommandRegistry Registry => Services.GetRequiredService<CommandRegistry>();

    private void Show(TicketDetailDto detail) => _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(detail));

    private IRenderedComponent<TicketDetailPage> RenderTicket() => Render<TicketDetailPage>(p => p.Add(c => c.Number, "ORB-42"));

    private IReadOnlyList<string> TicketCommands() => Registry.Available(isAdmin: false).Where(c => c.Group == PaletteCopy.TicketGroup).Select(c => c.Id).ToList();

    [Fact]
    public void An_open_unassigned_ticket_offers_reply_note_and_assign_to_me_and_no_admin_command()
    {
        Show(TestData.Detail());

        RenderTicket();

        TicketCommands().ShouldBe(["ticket-reply", "ticket-note", "ticket-assign-me"]);
        Registry.Available(isAdmin: false).Where(c => c.Group == PaletteCopy.TicketGroup).ShouldAllBe(c => !c.AdminOnly);
    }

    [Fact]
    public void A_ticket_already_assigned_to_the_agent_does_not_offer_assign_to_me()
    {
        Show(TestData.Detail(assigneeId: AgentSessions.SamId, assigneeName: "Sam Ortiz"));

        RenderTicket();

        TicketCommands().ShouldBe(["ticket-reply", "ticket-note"]);
    }

    [Fact]
    public void A_spam_ticket_offers_not_spam_and_a_ticket_that_is_not_spam_does_not()
    {
        Show(TestData.Detail(isSpam: true));

        RenderTicket();

        TicketCommands().ShouldContain("ticket-not-spam");
    }

    [Fact]
    public void A_closed_ticket_offers_nothing_because_it_cannot_be_replied_to_or_changed()
    {
        Show(TestData.Detail(status: TicketStatuses.Closed));

        RenderTicket();

        TicketCommands().ShouldBeEmpty();
    }

    [Fact]
    public async Task Leaving_the_ticket_takes_its_commands_out_of_the_palette()
    {
        Show(TestData.Detail());
        var cut = RenderTicket();
        TicketCommands().ShouldNotBeEmpty();

        await DisposeComponentsAsync();

        TicketCommands().ShouldBeEmpty();
    }

    [Fact]
    public void A_ticket_that_is_not_found_offers_nothing()
    {
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketDetailDto>("ticket-not-found", "No such ticket.", SyntaxCircus.Common.ResultErrorKind.NotFound));

        RenderTicket();

        TicketCommands().ShouldBeEmpty();
    }

    [Fact]
    public async Task Each_command_raises_the_shortcut_that_does_the_same_thing_once()
    {
        Show(TestData.Detail(isSpam: true));
        _tickets.AssignAsync(Arg.Any<Guid>(), Arg.Any<AssignTicketRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.State(assigneeId: AgentSessions.SamId, rowVersion: 8)));
        _tickets.SetSpamAsync(Arg.Any<Guid>(), Arg.Any<MarkTicketSpamRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.State(rowVersion: 8)));
        RenderTicket();
        var raised = new List<ShortcutAction>();
        ShortcutService.Pressed += action =>
        {
            raised.Add(action);
            return Task.CompletedTask;
        };

        foreach (var command in Registry.Available(isAdmin: false).Where(c => c.Group == PaletteCopy.TicketGroup))
        {
            await command.RunAsync();
        }

        raised.ShouldBe([ShortcutAction.Reply, ShortcutAction.Note, ShortcutAction.AssignToMe, ShortcutAction.NotSpam]);
    }

    [Fact]
    public async Task Assign_to_me_from_the_palette_assigns_the_ticket_to_the_agent_with_the_current_row_version()
    {
        Show(TestData.Detail());
        _tickets.AssignAsync(Arg.Any<Guid>(), Arg.Any<AssignTicketRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.State(assigneeId: AgentSessions.SamId, rowVersion: 8)));
        var cut = RenderTicket();

        await cut.InvokeAsync(() => Registry.Available(isAdmin: false).Single(c => c.Id == "ticket-assign-me").RunAsync());

        await _tickets.Received(1).AssignAsync(TestData.TicketId, Arg.Is<AssignTicketRequest>(r => r.AssigneeId == AgentSessions.SamId && r.RowVersion == 7u), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_list_follows_the_ticket_after_a_write_changes_it()
    {
        // The first read finds the ticket unassigned; the reload that follows the write finds it assigned to the agent.
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(
            TestData.Ok(TestData.Detail()),
            TestData.Ok(TestData.Detail(assigneeId: AgentSessions.SamId, assigneeName: "Sam Ortiz", rowVersion: 8)));
        _tickets.AssignAsync(Arg.Any<Guid>(), Arg.Any<AssignTicketRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.State(assigneeId: AgentSessions.SamId, rowVersion: 8)));
        var cut = RenderTicket();
        TicketCommands().ShouldContain("ticket-assign-me");

        await cut.InvokeAsync(() => Registry.Available(isAdmin: false).Single(c => c.Id == "ticket-assign-me").RunAsync());

        cut.WaitForAssertion(() => TicketCommands().ShouldNotContain("ticket-assign-me"));
    }
}
```

`tests/TechStrap.Admin.Tests/HeadingHostTests.cs`

```csharp
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Mvc.Testing;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

/// <summary>Every screen has one <c>h1</c> (UX-BRIEF-admin, accessibility). The two pages that are a brand window and nothing else, the 404 and the sign-in landing, used to have none.</summary>
public sealed class HeadingHostTests
{
    private static async Task<AngleSharp.Dom.IDocument> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return await new HtmlParser().ParseDocumentAsync(html, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_404_page_has_exactly_one_h1_and_it_is_the_window_heading()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        // The 404 page is a static page: no circuit and no API call, so there is no Authorization header to check (AssertEveryCallBore fails on zero calls), as in NotFoundHostTests.
        var page = await GetAsync(client, "/no-such-page");

        page.QuerySelectorAll("h1").Select(h => h.TextContent).ShouldBe(["This page fell out of its strap."]);
        page.QuerySelectorAll(".ts-window-body h2").ShouldBeEmpty();
    }

    [Fact]
    public async Task The_sign_in_landing_page_has_exactly_one_h1_and_it_is_the_window_heading()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var page = await GetAsync(client, "/signin?returnUrl=%2F");

        page.QuerySelectorAll("h1").Count.ShouldBe(1);
        page.QuerySelector("h1")!.TextContent.ShouldNotBeNullOrWhiteSpace();
        page.QuerySelectorAll("h2").ShouldBeEmpty();
    }
}
```

`tests/TechStrap.Admin.Tests/PaletteStyleTests.cs`

```csharp
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>The palette's selected line is set apart by a fill, a bar and weight, and by an outline when forced colours drop the fill. Colour is never the only cue.</summary>
public sealed class PaletteStyleTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    [Fact]
    public void The_selected_line_has_a_fill_a_bar_and_a_heavier_weight()
    {
        var selected = Css.Declarations(".ts-palette-option[aria-selected=true]");

        selected["background"].ShouldBe("var(--sel)");
        selected["border-left-color"].ShouldBe("var(--margin)");
        selected["font-weight"].ShouldBe("700");
    }

    [Fact]
    public void In_forced_colours_the_selected_line_gets_an_outline_in_the_system_highlight_colour()
    {
        var forced = Css.Declarations(".ts-palette-option[aria-selected=true]");

        forced["outline"].ShouldBe("2px solid Highlight");
    }

    [Fact]
    public void The_palette_fits_a_phone_and_scrolls_its_list_not_the_page()
    {
        Css.Declarations(".ts-palette")["width"].ShouldBe("min(36rem,100vw - 32px)");
        Css.Declarations(".ts-palette-list")["overflow-y"].ShouldBe("auto");
    }
}
```

`tests/TechStrap.Admin.Tests/ScriptHostTests.cs`

Replace

```csharp
    [InlineData("/js/preferences.js")]
    [InlineData("/js/clipboard.js")]
    [InlineData("/js/tz.js")]
    public async Task Module_scripts_are_served_as_javascript_without_signing_in(string path)
    {
        await using var factory = new AdminFactory();
```

with

```csharp
    [InlineData("/js/preferences.js")]
    [InlineData("/js/clipboard.js")]
    [InlineData("/js/tz.js")]
    [InlineData("/js/palette.js")]
    [InlineData("/js/menu.js")]
    public async Task Module_scripts_are_served_as_javascript_without_signing_in(string path)
    {
        await using var factory = new AdminFactory();
```

`tests/TechStrap.Admin.Tests/Support/AdminComponentTest.cs` (2 edits)

Replace

```csharp
        // MainLayout also asks the browser for its time zone on the first render; by default it answers UTC. A test sets it up again to try another zone.
        Tz = JSInterop.SetupModule("./js/tz.js");
        Tz.Setup<string?>("zone", _ => true).SetResult("UTC");
    }

    protected FakeTimeProvider Time { get; }
```

with

```csharp
        // MainLayout also asks the browser for its time zone on the first render; by default it answers UTC. A test sets it up again to try another zone.
        Tz = JSInterop.SetupModule("./js/tz.js");
        Tz.Setup<string?>("zone", _ => true).SetResult("UTC");

        // The command palette (opened by MainLayout on Ctrl+K) and the ticket actions menu each import a small module the first time they are used.
        Palette = JSInterop.SetupModule("./js/palette.js");
        Palette.SetupVoid("attach", _ => true).SetVoidResult();
        Palette.SetupVoid("detach", _ => true).SetVoidResult();
        Palette.SetupVoid("reveal", _ => true).SetVoidResult();
        Menu = JSInterop.SetupModule("./js/menu.js");
        Menu.SetupVoid("attach", _ => true).SetVoidResult();
        Menu.SetupVoid("focusFirst", _ => true).SetVoidResult();
    }

    protected FakeTimeProvider Time { get; }
```

Replace

```csharp
    /// <summary>The <c>tz.js</c> module double: <c>zone</c> answers "UTC"; set it up again to answer another zone or to fail.</summary>
    protected BunitJSModuleInterop Tz { get; }

    protected ShortcutService ShortcutService => Services.GetRequiredService<ShortcutService>();

    protected StatusMessageService StatusMessages => Services.GetRequiredService<StatusMessageService>();
```

with

```csharp
    /// <summary>The <c>tz.js</c> module double: <c>zone</c> answers "UTC"; set it up again to answer another zone or to fail.</summary>
    protected BunitJSModuleInterop Tz { get; }

    /// <summary>The <c>palette.js</c> module double: <c>attach</c>, <c>detach</c> and <c>reveal</c> succeed.</summary>
    protected BunitJSModuleInterop Palette { get; }

    /// <summary>The <c>menu.js</c> module double: <c>attach</c> and <c>focusFirst</c> succeed.</summary>
    protected BunitJSModuleInterop Menu { get; }

    protected ShortcutService ShortcutService => Services.GetRequiredService<ShortcutService>();

    protected StatusMessageService StatusMessages => Services.GetRequiredService<StatusMessageService>();
```

`tests/TechStrap.Admin.Tests/js/menu.test.mjs`

```javascript
// Runs with `node --test` (no browser): decideKey in menu.js takes plain objects.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { decideKey } from '../../../src/TechStrap.Admin/wwwroot/js/menu.js';

const event = (key, extra = {}) => ({ key, isComposing: false, altKey: false, ctrlKey: false, metaKey: false, shiftKey: false, ...extra });
const closed = (extra = {}) => ({ open: false, onButton: true, count: 3, current: -1, ...extra });
const open = (extra = {}) => ({ open: true, onButton: false, count: 3, current: 0, ...extra });

describe('a closed menu', () => {
    it('opens on ArrowDown from the button and stops the page from scrolling', () => {
        assert.deepEqual(decideKey(event('ArrowDown'), closed()), { preventDefault: true, action: 'open' });
    });

    it('ignores every other key, and ArrowDown when focus is not on the button', () => {
        for (const key of ['ArrowUp', 'Escape', 'Tab', 'Home', 'End', 'a']) {
            assert.equal(decideKey(event(key), closed()), null, key);
        }

        assert.equal(decideKey(event('ArrowDown'), closed({ onButton: false })), null);
    });
});

describe('an open menu', () => {
    it('moves to the next and previous item and wraps round at both ends', () => {
        assert.deepEqual(decideKey(event('ArrowDown'), open({ current: 0 })).action, { focus: 1 });
        assert.deepEqual(decideKey(event('ArrowDown'), open({ current: 2 })).action, { focus: 0 });
        assert.deepEqual(decideKey(event('ArrowUp'), open({ current: 2 })).action, { focus: 1 });
        assert.deepEqual(decideKey(event('ArrowUp'), open({ current: 0 })).action, { focus: 2 });
    });

    it('starts from the first item going down and the last going up when nothing in the menu has focus yet', () => {
        assert.deepEqual(decideKey(event('ArrowDown'), open({ current: -1 })).action, { focus: 0 });
        assert.deepEqual(decideKey(event('ArrowUp'), open({ current: -1 })).action, { focus: 2 });
    });

    it('jumps to the first and last item with Home and End', () => {
        assert.deepEqual(decideKey(event('Home'), open({ current: 2 })).action, { focus: 0 });
        assert.deepEqual(decideKey(event('End'), open({ current: 0 })).action, { focus: 2 });
    });

    it('takes every navigation key from the browser', () => {
        for (const key of ['ArrowDown', 'ArrowUp', 'Home', 'End']) {
            assert.equal(decideKey(event(key), open()).preventDefault, true, key);
        }
    });

    it('closes on Escape and returns focus to the button, and the key is taken so the page does not also go back to the queue', () => {
        assert.deepEqual(decideKey(event('Escape'), open()), { preventDefault: true, action: 'close-focus-button' });
    });

    it('closes on Tab and Shift+Tab but lets focus move on', () => {
        assert.deepEqual(decideKey(event('Tab'), open()), { preventDefault: false, action: 'close' });
        assert.deepEqual(decideKey(event('Tab', { shiftKey: true }), open()), { preventDefault: false, action: 'close' });
    });

    it('does nothing for an empty menu or an unrelated key', () => {
        for (const key of ['ArrowDown', 'ArrowUp', 'Home', 'End']) {
            assert.equal(decideKey(event(key), open({ count: 0, current: -1 })), null, key);
        }

        assert.equal(decideKey(event('a'), open()), null);
        assert.equal(decideKey(event('Enter'), open()), null);
    });
});

describe('chords and composition', () => {
    it('are never handled', () => {
        for (const extra of [{ ctrlKey: true }, { metaKey: true }, { altKey: true }, { isComposing: true }]) {
            assert.equal(decideKey(event('ArrowDown', extra), open()), null, JSON.stringify(extra));
            assert.equal(decideKey(event('Escape', extra), open()), null, JSON.stringify(extra));
            assert.equal(decideKey(event('ArrowDown', extra), closed()), null, JSON.stringify(extra));
        }
    });
});
```

`tests/TechStrap.Admin.Tests/js/palette.test.mjs`

```javascript
// Runs with `node --test` (no browser): decideKey in palette.js takes a plain event object.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { attach, decideKey, detach, reveal } from '../../../src/TechStrap.Admin/wwwroot/js/palette.js';

const event = (key, extra = {}) => ({ key, isComposing: false, altKey: false, ctrlKey: false, metaKey: false, shiftKey: false, ...extra });

describe('decideKey', () => {
    it('takes the keys that move the selection and run the command', () => {
        for (const key of ['ArrowDown', 'ArrowUp', 'Home', 'End', 'Enter']) {
            assert.equal(decideKey(event(key)), key, key);
        }
    });

    it('leaves typing, Escape and Tab to the browser', () => {
        for (const key of ['a', 'Z', ' ', 'Backspace', 'ArrowLeft', 'ArrowRight', 'Escape', 'Tab', 'Delete']) {
            assert.equal(decideKey(event(key)), null, key);
        }
    });

    it('leaves every chord alone, so Ctrl+Home, Shift+Arrow selection and Alt+Arrow keep their browser meaning', () => {
        for (const extra of [{ ctrlKey: true }, { metaKey: true }, { altKey: true }, { shiftKey: true }]) {
            assert.equal(decideKey(event('ArrowDown', extra)), null, JSON.stringify(extra));
            assert.equal(decideKey(event('Enter', extra)), null, JSON.stringify(extra));
        }
    });

    it('leaves a key during IME composition alone, because Enter there confirms the composition', () => {
        assert.equal(decideKey(event('Enter', { isComposing: true })), null);
    });
});

describe('attach, detach and reveal', () => {
    const fakeInput = () => {
        const listeners = new Map();
        return {
            listeners,
            addEventListener: (name, fn) => listeners.set(name, fn),
            removeEventListener: (name) => listeners.delete(name),
        };
    };

    it('reports a selection key to .NET once and stops the browser from handling it', () => {
        const input = fakeInput();
        const told = [];
        attach(input, { invokeMethodAsync: (method, key) => { told.push([method, key]); return Promise.resolve(); } });
        let prevented = 0;

        input.listeners.get('keydown')({ ...event('ArrowDown'), preventDefault: () => { prevented++; } });
        input.listeners.get('keydown')({ ...event('a'), preventDefault: () => { prevented++; } });

        assert.deepEqual(told, [['NavigateKey', 'ArrowDown']]);
        assert.equal(prevented, 1);
    });

    it('attaches once per input and detaches cleanly', () => {
        const input = fakeInput();
        const handle = { invokeMethodAsync: () => Promise.resolve() };

        attach(input, handle);
        const first = input.listeners.get('keydown');
        attach(input, handle);
        assert.equal(input.listeners.get('keydown'), first);

        detach(input);
        assert.equal(input.listeners.has('keydown'), false);
        assert.doesNotThrow(() => detach(input));
        assert.doesNotThrow(() => detach(null));
        assert.doesNotThrow(() => attach(null, handle));
    });

    it('never throws when the circuit is gone', () => {
        const input = fakeInput();
        attach(input, { invokeMethodAsync: () => { throw new Error('disconnected'); } });

        assert.doesNotThrow(() => input.listeners.get('keydown')({ ...event('Enter'), preventDefault: () => { } }));

        const rejected = fakeInput();
        attach(rejected, { invokeMethodAsync: () => Promise.reject(new Error('gone')) });
        assert.doesNotThrow(() => rejected.listeners.get('keydown')({ ...event('Enter'), preventDefault: () => { } }));
    });

    it('reveals the selected option, and does nothing without a list or a selection', () => {
        const revealed = [];
        const list = { querySelector: () => ({ scrollIntoView: (options) => revealed.push(options) }) };

        reveal(list);
        reveal(null);
        reveal({ querySelector: () => null });

        assert.deepEqual(revealed, [{ block: 'nearest' }]);
    });
});
```

`tests/TechStrap.Admin.Tests/js/shortcuts.test.mjs` (3 edits)

Replace

```javascript
// Runs with `node --test` (no browser, no jsdom): the key filter in shortcuts.js is pure and takes plain objects.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { decide, isRelevantKey, isTyping } from '../../../src/TechStrap.Admin/wwwroot/js/shortcuts.js';

const element = (tagName, extra = {}) => ({ tagName, type: '', isContentEditable: false, getAttribute: () => null, ...extra });
const event = (key, extra = {}) => ({
```

with

```javascript
// Runs with `node --test` (no browser, no jsdom): the key filter in shortcuts.js is pure and takes plain objects.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { decide, isPaletteChord, isRelevantKey, isTyping } from '../../../src/TechStrap.Admin/wwwroot/js/shortcuts.js';

const element = (tagName, extra = {}) => ({ tagName, type: '', isContentEditable: false, getAttribute: () => null, ...extra });
const event = (key, extra = {}) => ({
```

Replace

```javascript
        for (const key of ['a', 'Tab', 'Shift', 'F5', ' ', 'x']) {
            assert.equal(isRelevantKey(key), false, key);
        }
    });
});
```

with

```javascript
        for (const key of ['a', 'Tab', 'Shift', 'F5', ' ', 'x']) {
            assert.equal(isRelevantKey(key), false, key);
        }
    });
});

describe('isPaletteChord', () => {
    it('accepts Ctrl+K and Cmd+K in either case', () => {
        assert.equal(isPaletteChord(event('k', { ctrlKey: true })), true);
        assert.equal(isPaletteChord(event('K', { ctrlKey: true })), true);
        assert.equal(isPaletteChord(event('k', { metaKey: true })), true);
    });

    it('rejects a bare k, other chords, and Alt or Shift combinations', () => {
        assert.equal(isPaletteChord(event('k')), false);
        assert.equal(isPaletteChord(event('j', { ctrlKey: true })), false);
        assert.equal(isPaletteChord(event('k', { ctrlKey: true, altKey: true })), false);
        assert.equal(isPaletteChord(event('k', { ctrlKey: true, shiftKey: true })), false);
        assert.equal(isPaletteChord(event(undefined, { ctrlKey: true })), false);
    });
});
```

Replace

```javascript
        assert.equal(decide(event('j', { altKey: true }), env()), null);
    });

    it('marks onBody false when a control has focus', () => {
        const result = decide(event('Enter'), env({ active: element('BUTTON') }));
```

with

```javascript
        assert.equal(decide(event('j', { altKey: true }), env()), null);
    });

    it('reports Ctrl+K and Cmd+K from anywhere, typing included, and takes the key from the browser', () => {
        const typing = element('INPUT', { type: 'text' });

        for (const active of [null, typing, element('BUTTON')]) {
            const ctrl = decide(event('k', { ctrlKey: true }), env({ active }));
            const meta = decide(event('K', { metaKey: true }), env({ active }));

            assert.equal(ctrl.preventDefault, true);
            assert.equal(ctrl.payload.key, 'k');
            assert.equal(ctrl.payload.ctrl, true);
            assert.equal(meta.payload.meta, true);
        }
    });

    it('does not open the palette over another modal dialog, but a press inside the palette closes it', () => {
        assert.equal(decide(event('k', { ctrlKey: true }), env({ dialogOpen: true })), null);
        assert.equal(decide(event('k', { ctrlKey: true }), env({ dialogOpen: true, paletteOpen: false })), null);

        const inside = decide(event('k', { ctrlKey: true }), env({ dialogOpen: true, paletteOpen: true, active: element('INPUT', { type: 'text' }) }));
        assert.equal(inside.payload.key, 'k');
        assert.equal(inside.preventDefault, true);
    });

    it('ignores Ctrl+K held down, composed, or already handled, and Ctrl+Alt+K and Ctrl+Shift+K', () => {
        assert.equal(decide(event('k', { ctrlKey: true, repeat: true }), env()), null);
        assert.equal(decide(event('k', { ctrlKey: true, isComposing: true }), env()), null);
        assert.equal(decide(event('k', { ctrlKey: true, defaultPrevented: true }), env()), null);
        assert.equal(decide(event('k', { ctrlKey: true, altKey: true }), env()), null);
        assert.equal(decide(event('k', { ctrlKey: true, shiftKey: true }), env()), null);
    });

    it('marks onBody false when a control has focus', () => {
        const result = decide(event('Enter'), env({ active: element('BUTTON') }));
```

- [ ] **Step 2: Run them and watch them fail**

```bash
node --test tests/TechStrap.Admin.Tests/js/shortcuts.test.mjs tests/TechStrap.Admin.Tests/js/palette.test.mjs tests/TechStrap.Admin.Tests/js/menu.test.mjs
dotnet test --project tests/TechStrap.Admin.Tests -c Release
```

Expected: node fails (`isPaletteChord` is not exported; `palette.js` and `menu.js` do not exist), and the .NET build fails (`CommandRegistry`, `CommandPalette`, `RailLink`, `ShortcutAction.Palette`, `BrandWindow.HeadingLevel` and the rest do not exist).

- [ ] **Step 3: Implement**

The key layer first (`shortcuts.js`, `ShortcutService`), then the registry, the palette and its two scripts, the layout and ticket wiring, the menu, the rail and the heading level. New files are given whole; the others as edits.

`src/TechStrap.Admin/Components/Layout/CommandPalette.razor`

```razor
<dialog @ref="_dialog" class="ts-dialog ts-palette" data-palette aria-label="@PaletteCopy.Title"
        @oncancel="CloseAsync" @oncancel:preventDefault="true">
    <input @ref="_input" id="@InputId" type="text" class="form-control ts-palette-input" role="combobox" aria-expanded="true" aria-autocomplete="list"
           aria-controls="@ListId" aria-activedescendant="@ActiveOptionId" aria-label="@PaletteCopy.InputLabel" autocomplete="off" spellcheck="false"
           value="@_query" @oninput="OnQuery" />
    <ul @ref="_list" id="@ListId" class="ts-palette-list" role="listbox" aria-label="@PaletteCopy.ListLabel">
        @for (var i = 0; i < Matches.Count; i++)
        {
            var index = i;
            var command = Matches[i];
            <li @key="command.Id" id="@OptionId(index)" class="ts-palette-option" role="option" aria-selected="@(index == SelectedIndex ? "true" : "false")"
                @onclick="() => RunAsync(command)">
                <span class="ts-palette-label">@command.Label</span>
                <span class="ts-palette-meta">
                    <span class="ts-palette-group">@command.Group</span>
                    @if (command.Keys is not null)
                    {
                        <Kbd>@command.Keys</Kbd>
                    }
                </span>
            </li>
        }
    </ul>
    @if (Matches.Count == 0)
    {
        <p class="ts-palette-empty">@PaletteCopy.Empty</p>
    }
    <p class="visually-hidden" role="status">@PaletteCopy.Count(Matches.Count)</p>
</dialog>
```

`src/TechStrap.Admin/Components/Layout/CommandPalette.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Components.Layout;

/// <summary>
/// The command palette (UX-BRIEF-admin): a modal combobox on the native <c>dialog</c> element, opened with Ctrl+K or Cmd+K. Typing filters the commands; ArrowDown, ArrowUp, Home and End
/// move the selection (<c>palette.js</c> takes those keys from the browser); Enter or a click runs the selected command; Esc closes it and the browser returns focus to where it was.
/// Three rules decide what runs. The list is <see cref="CommandRegistry.Available"/> for this session, so an agent who is not an Admin never sees an admin command, and the same check
/// runs again when a command is chosen, because the session can change while the palette is open. The palette closes first and the command runs after the dialog has closed in the browser,
/// so a command that moves focus finds the page and not the inert background. And a command runs once: a second Enter or click while one is pending or running does nothing.
/// The owner controls <see cref="Open"/>. Nothing here may end the circuit: a script failure leaves the palette unopened and a command that throws is logged by type and reported in the status bar.
/// </summary>
public sealed partial class CommandPalette : IAsyncDisposable
{
    private const string DialogModule = "./js/dialog.js";
    private const string PaletteModule = "./js/palette.js";
    private static int _nextId;

    private readonly int _id = Interlocked.Increment(ref _nextId);
    private ElementReference _dialog;
    private ElementReference _input;
    private ElementReference _list;
    private IJSObjectReference? _dialogScript;
    private IJSObjectReference? _paletteScript;
    private DotNetObjectReference<CommandPalette>? _self;
    private PaletteCommand? _pending;
    private bool _running;
    private bool _shown;
    private bool _openSeen;
    private bool _reveal;
    private int _index;
    private string _query = string.Empty;

    [Inject]
    private IJSRuntime Js { get; set; } = default!;

    [Inject]
    private CommandRegistry Registry { get; set; } = default!;

    [Inject]
    private AgentSession Session { get; set; } = default!;

    [Inject]
    private StatusMessageService StatusMessages { get; set; } = default!;

    [Inject]
    private ILogger<CommandPalette> Logger { get; set; } = default!;

    [Parameter]
    public bool Open { get; set; }

    /// <summary>Raised when the palette should close: Esc, a stray close by the browser, or a command was chosen.</summary>
    [Parameter]
    public EventCallback OnClose { get; set; }

    private string InputId => $"ts-palette-input-{_id}";

    private string ListId => $"ts-palette-list-{_id}";

    private string OptionId(int index) => $"ts-palette-option-{_id}-{index}";

    private string? ActiveOptionId => Matches.Count == 0 ? null : OptionId(SelectedIndex);

    /// <summary>The selected line, kept inside the list even when the list got shorter since it was chosen.</summary>
    private int SelectedIndex => Math.Clamp(_index, 0, Math.Max(Matches.Count - 1, 0));

    /// <summary>What the agent may see for what they typed. Nothing is listed while the palette is closed.</summary>
    private IReadOnlyList<PaletteCommand> Matches => Open ? CommandRegistry.Filter(Registry.Available(Session.IsAdmin), _query) : [];

    protected override void OnParametersSet()
    {
        if (Open && !_openSeen)
        {
            // A new cycle starts empty and on the first line, with nothing left over from a command that was pending when it closed.
            _query = string.Empty;
            _index = 0;
            _pending = null;
        }

        _openSeen = Open;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await SyncDialogAsync();

        if (Open)
        {
            if (_reveal)
            {
                _reveal = false;
                await TryAsync(async () => await _paletteScript!.InvokeVoidAsync("reveal", _list));
            }

            return;
        }

        await RunPendingAsync();
    }

    private async Task SyncDialogAsync()
    {
        if (Open == _shown)
        {
            return;
        }

        try
        {
            _dialogScript ??= await Js.InvokeAsync<IJSObjectReference>("import", DialogModule);
            if (Open)
            {
                _paletteScript ??= await Js.InvokeAsync<IJSObjectReference>("import", PaletteModule);
                _self ??= DotNetObjectReference.Create(this);
                await _paletteScript.InvokeVoidAsync("attach", _input, _self);
                await _dialogScript.InvokeVoidAsync("open", _dialog, _input, _self);
            }
            else
            {
                await _dialogScript.InvokeVoidAsync("close", _dialog);
            }

            // Only after the script succeeded: a failed open must not leave us believing the dialog is showing.
            _shown = Open;
        }
        catch (Exception ex) when (IsScriptFailure(ex))
        {
            // No script, no circuit: the palette stays shut. Log the type, never a message (a message could carry what the agent typed).
            Logger.LogWarning("The command palette could not {Step} the dialog ({ExceptionType}).", Open ? "open" : "close", ex.GetType().Name);
            _shown = false;
            if (Open)
            {
                await CloseAsync();
            }
        }
    }

    /// <summary>The moment after the dialog closed: the chosen command runs, once.</summary>
    private async Task RunPendingAsync()
    {
        if (_pending is not { } command || _running)
        {
            return;
        }

        _pending = null;
        _running = true;
        try
        {
            await command.RunAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // OnAfterRenderAsync must never throw: an exception here would end the circuit. Log the type only, and tell the agent.
            Logger.LogWarning("A command palette command {CommandId} failed ({ExceptionType}).", command.Id, ex.GetType().Name);
            StatusMessages.Show(PaletteCopy.CommandFailed);
        }
        finally
        {
            _running = false;
        }
    }

    private void OnQuery(ChangeEventArgs e)
    {
        _query = e.Value as string ?? string.Empty;
        _index = 0;
        _reveal = true;
    }

    /// <summary>Called by <c>palette.js</c> for ArrowDown, ArrowUp, Home, End and Enter in the input.</summary>
    [JSInvokable]
    public Task NavigateKey(string key) => InvokeAsync(async () =>
    {
        var matches = Matches;
        if (key == "Enter")
        {
            if (matches.Count > 0)
            {
                await RunAsync(matches[SelectedIndex]);
            }

            return;
        }

        if (matches.Count == 0)
        {
            return;
        }

        var current = SelectedIndex;
        _index = key switch
        {
            "ArrowDown" => (current + 1) % matches.Count,
            "ArrowUp" => (current - 1 + matches.Count) % matches.Count,
            "Home" => 0,
            "End" => matches.Count - 1,
            _ => _index,
        };
        _reveal = true;
        StateHasChanged();
    });

    /// <summary>Called by <c>dialog.js</c> when the browser closed the dialog behind our back (a repeated Esc): that is a close.</summary>
    [JSInvokable]
    public Task NativeClosed() => InvokeAsync(async () =>
    {
        if (Open)
        {
            await CloseAsync();
            if (Open)
            {
                _shown = false;
                StateHasChanged();
            }
        }
    });

    /// <summary>Called by <c>dialog.js</c> for an Esc it stopped. The palette is never held open, so this never happens, but the handle must answer.</summary>
    [JSInvokable]
    public Task EscapePressed() => InvokeAsync(CloseAsync);

    private Task CloseAsync() => OnClose.InvokeAsync();

    /// <summary>
    /// Chooses a command: closes the palette now and lets <see cref="RunPendingAsync"/> run it once the dialog is gone. Does nothing while another choice is pending or running,
    /// and nothing for an admin command when the session is not an admin's.
    /// </summary>
    private async Task RunAsync(PaletteCommand command)
    {
        if (_pending is not null || _running || (command.AdminOnly && !Session.IsAdmin))
        {
            return;
        }

        _pending = command;
        await CloseAsync();
    }

    private static bool IsScriptFailure(Exception ex) =>
        ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException;

    private static async Task TryAsync(Func<Task> script)
    {
        try
        {
            await script();
        }
        catch (Exception ex) when (IsScriptFailure(ex))
        {
            // A selection that is not scrolled into view is not worth an error.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _self?.Dispose();
        try
        {
            if (_paletteScript is not null)
            {
                await _paletteScript.InvokeVoidAsync("detach", _input);
                await _paletteScript.DisposeAsync();
            }

            if (_dialogScript is not null)
            {
                if (_shown)
                {
                    await _dialogScript.InvokeVoidAsync("close", _dialog);
                }

                await _dialogScript.DisposeAsync();
            }
        }
        catch (Exception ex) when (ex is JSDisconnectedException or JSException)
        {
            // The circuit is already gone; the browser has dropped the dialog with the page.
        }
    }
}
```

`src/TechStrap.Admin/Components/Layout/MainLayout.razor`

Replace

```razor
    </div>
</div>
<ShortcutHelpDialog Open="_helpOpen" OnClose="CloseHelp" />
```

with

```razor
    </div>
</div>
<ShortcutHelpDialog Open="_helpOpen" OnClose="CloseHelp" />
<CommandPalette Open="_paletteOpen" OnClose="ClosePalette" />
```

`src/TechStrap.Admin/Components/Layout/MainLayout.razor.cs` (5 edits)

Replace

```csharp
using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Components.Layout;
```

with

```csharp
using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Components.Layout;
```

Replace

```csharp
    private const string QueuePath = "queue";

    private bool _helpOpen;

    [Inject]
    private ShortcutService Shortcuts { get; set; } = default!;
```

with

```csharp
    private const string QueuePath = "queue";

    private bool _helpOpen;
    private bool _paletteOpen;

    [Inject]
    private ShortcutService Shortcuts { get; set; } = default!;
```

Replace

```csharp

    [Inject]
    private LocalTimeService LocalTime { get; set; } = default!;

    /// <summary>
    /// The page has <c>&lt;base href="/"&gt;</c>, so a bare <c>#main</c> would resolve to the home page. The link names the current address with the fragment replaced.
```

with

```csharp

    [Inject]
    private LocalTimeService LocalTime { get; set; } = default!;

    [Inject]
    private AgentSession Session { get; set; } = default!;

    /// <summary>
    /// The page has <c>&lt;base href="/"&gt;</c>, so a bare <c>#main</c> would resolve to the home page. The link names the current address with the fragment replaced.
```

Replace

```csharp
            case ShortcutAction.Help:
                _helpOpen = true;
                return InvokeAsync(StateHasChanged);
            case ShortcutAction.FocusSearch when !IsOnQueue():
                Navigation.NavigateTo("/" + QueuePath);
                break;
```

with

```csharp
            case ShortcutAction.Help:
                _helpOpen = true;
                return InvokeAsync(StateHasChanged);
            case ShortcutAction.Palette:
                // Ctrl+K toggles. It never opens before the API has said who this agent is: until then there is nothing to offer, and an admin command must never be listed on a guess.
                _paletteOpen = !_paletteOpen && Session.State == AgentSessionState.Ready;
                return InvokeAsync(StateHasChanged);
            case ShortcutAction.FocusSearch when !IsOnQueue():
                Navigation.NavigateTo("/" + QueuePath);
                break;
```

Replace

```csharp

    private void CloseHelp() => _helpOpen = false;

    public void Dispose()
    {
        Shortcuts.Pressed -= OnShortcutAsync;
```

with

```csharp

    private void CloseHelp() => _helpOpen = false;

    private void ClosePalette() => _paletteOpen = false;

    public void Dispose()
    {
        Shortcuts.Pressed -= OnShortcutAsync;
```

`src/TechStrap.Admin/Components/Layout/NavMenu.razor` (2 edits)

Replace

```razor
    </a>
    @if (Session.State == AgentSessionState.Ready)
    {
        <NavLink class="ts-rail-link" href="/queue" Match="NavLinkMatch.Prefix">@ShellCopy.QueueLink</NavLink>
        @if (Session.IsAdmin)
        {
            <div class="ts-rail-group" role="group" aria-label="@ShellCopy.AdminLinksLabel">
                <NavLink class="ts-rail-link" href="/settings/products" Match="NavLinkMatch.Prefix">@ShellCopy.ProductsLink</NavLink>
                <NavLink class="ts-rail-link" href="/settings/agents" Match="NavLinkMatch.Prefix">@ShellCopy.AgentsLink</NavLink>
                <NavLink class="ts-rail-link" href="/settings/tags" Match="NavLinkMatch.Prefix">@ShellCopy.TagsLink</NavLink>
                <NavLink class="ts-rail-link" href="/settings/audit" Match="NavLinkMatch.Prefix">@ShellCopy.AuditLink</NavLink>
                <NavLink class="ts-rail-link" href="/ops/dead-letters" Match="NavLinkMatch.Prefix">
                    @ShellCopy.FailedEmailsLink
                    @if (Failed.Count is > 0 and var failed)
                    {
                        <span class="ts-rail-badge" aria-hidden="true">@failed</span>
                        <span class="visually-hidden">@ShellCopy.FailedEmailsCountLabel(failed)</span>
                    }
                </NavLink>
            </div>
        }
        <div class="ts-rail-user">
```

with

```razor
    </a>
    @if (Session.State == AgentSessionState.Ready)
    {
        <RailLink Href="/queue">@ShellCopy.QueueLink</RailLink>
        @if (Session.IsAdmin)
        {
            <div class="ts-rail-group" role="group" aria-label="@ShellCopy.AdminLinksLabel">
                <RailLink Href="/settings/products">@ShellCopy.ProductsLink</RailLink>
                <RailLink Href="/settings/agents">@ShellCopy.AgentsLink</RailLink>
                <RailLink Href="/settings/tags">@ShellCopy.TagsLink</RailLink>
                <RailLink Href="/settings/audit">@ShellCopy.AuditLink</RailLink>
                <RailLink Href="/ops/dead-letters">
                    @ShellCopy.FailedEmailsLink
                    @if (Failed.Count is > 0 and var failed)
                    {
                        <span class="ts-rail-badge" aria-hidden="true">@failed</span>
                        <span class="visually-hidden">@ShellCopy.FailedEmailsCountLabel(failed)</span>
                    }
                </RailLink>
            </div>
        }
        <div class="ts-rail-user">
```

Replace

```razor
            {
                <span class="ts-rail-role">Admin</span>
            }
            <NavLink class="ts-rail-link" href="/account/notifications" Match="NavLinkMatch.Prefix">@ShellCopy.MySettingsLink</NavLink>
            <SignOutForm />
        </div>
    }
```

with

```razor
            {
                <span class="ts-rail-role">Admin</span>
            }
            <RailLink Href="/account/notifications">@ShellCopy.MySettingsLink</RailLink>
            <SignOutForm />
        </div>
    }
```

`src/TechStrap.Admin/Components/Layout/RailLink.razor`

```razor
@implements IDisposable
@inject NavigationManager Navigation

<a class="ts-rail-link" href="@Href" aria-current="@(IsCurrent ? "page" : null)">@ChildContent</a>
```

`src/TechStrap.Admin/Components/Layout/RailLink.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace TechStrap.Admin.Components.Layout;

/// <summary>
/// A link in the rail. It is current when the page is its address or anywhere below it (<c>/settings/products/abc</c> keeps Products current), and says so with
/// <c>aria-current="page"</c>, which is what the screen reader announces and what the rail's style reads. The framework's <c>NavLink</c> only adds a class, which a screen
/// reader never hears. The queue's view tabs and rows already use the same attribute.
/// </summary>
public sealed partial class RailLink : IDisposable
{
    [Parameter, EditorRequired]
    public string Href { get; set; } = string.Empty;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    private bool IsCurrent => IsCurrentAddress(Navigation.ToBaseRelativePath(Navigation.Uri), Href);

    protected override void OnInitialized() => Navigation.LocationChanged += OnLocationChanged;

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => _ = InvokeAsync(StateHasChanged);

    /// <summary>
    /// True when <paramref name="relativePath"/> (the page's address relative to the base, with any query or fragment) is <paramref name="href"/> or below it, compared on whole path
    /// segments and ignoring case: <c>queue/mine</c> is under <c>/queue</c>, <c>queued</c> is not.
    /// </summary>
    public static bool IsCurrentAddress(string relativePath, string href)
    {
        var end = relativePath.IndexOfAny(['?', '#']);
        var path = (end < 0 ? relativePath : relativePath[..end]).Trim('/');
        var target = href.Trim('/');
        return path.Equals(target, StringComparison.OrdinalIgnoreCase)
            || (target.Length > 0 && path.StartsWith(target + "/", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose() => Navigation.LocationChanged -= OnLocationChanged;
}
```

`src/TechStrap.Admin/Components/Pages/NotFound.razor`

In the `BrandWindow` opening tag, add the attribute `HeadingLevel="1"` after `Heading="This page fell out of its strap."` (the tag is on one line; its `Title` holds an em dash, which this plan does not reproduce, so the line is not quoted). The tag becomes:

```razor
<BrandWindow Title="..." Heading="This page fell out of its strap." HeadingLevel="1">
```

where `Title` keeps whatever the file already has.

`src/TechStrap.Admin/Components/Pages/SignInLanding.razor`

Replace

```razor

<body>
    <main class="ts-window-page">
        <BrandWindow Title="@SignInCopy.WindowTitle" Heading="@SignInCopy.Heading">
            <p>@SignInCopy.Body</p>
            @if (Failed)
            {
```

with

```razor

<body>
    <main class="ts-window-page">
        <BrandWindow Title="@SignInCopy.WindowTitle" Heading="@SignInCopy.Heading" HeadingLevel="1">
            <p>@SignInCopy.Body</p>
            @if (Failed)
            {
```

`src/TechStrap.Admin/Components/Ui/BrandWindow.razor` (2 edits)

Replace

```razor
    </div>
    <div class="ts-window-body">
        <img src="brand/mark.svg" alt="" width="96" height="96" />
        <h2>@Heading</h2>
        @ChildContent
    </div>
</article>
```

with

```razor
    </div>
    <div class="ts-window-body">
        <img src="brand/mark.svg" alt="" width="96" height="96" />
        @if (HeadingLevel == 1)
        {
            <h1>@Heading</h1>
        }
        else
        {
            <h2>@Heading</h2>
        }
        @ChildContent
    </div>
</article>
```

Replace

```razor
    [Parameter, EditorRequired]
    public string Heading { get; set; } = string.Empty;

    /// <summary>The copy and the single action: one sentence per line, then one button or one link.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }
```

with

```razor
    [Parameter, EditorRequired]
    public string Heading { get; set; } = string.Empty;

    /// <summary>
    /// 2 (the default) inside a page that has its own <c>h1</c>, such as the "All caught up" window in the queue. 1 when the window is the whole page (404 and the sign-in landing),
    /// so every screen has exactly one <c>h1</c> (UX-BRIEF-admin, accessibility).
    /// </summary>
    [Parameter]
    public int HeadingLevel { get; set; } = 2;

    /// <summary>The copy and the single action: one sentence per line, then one button or one link.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }
```

`src/TechStrap.Admin/Features/Shell/CommandRegistry.cs`

```csharp
using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Features.Queue;

namespace TechStrap.Admin.Features.Shell;

/// <summary>One line in the command palette.</summary>
/// <param name="Id">Stable and unique, for example <c>go-queue-mine</c>; it is the <c>@key</c> of the line and what tests name.</param>
/// <param name="Label">What the agent reads and types against: "Queue: Mine", "Reply to requester".</param>
/// <param name="Group">The kind of command (<see cref="PaletteCopy"/>), shown after the label and matched by the filter too.</param>
/// <param name="RunAsync">What the command does. The palette is closed before it runs, so a command that moves focus finds the page and not the dialog.</param>
/// <param name="Keys">The shortcut that does the same, when there is one, shown beside the label (UX-BRIEF-admin).</param>
/// <param name="AdminOnly">Never shown to, and never run for, an agent who is not an Admin. Hiding is not access control (the API refuses every admin call), but a plain agent never sees a page they cannot use.</param>
public sealed record PaletteCommand(string Id, string Label, string Group, Func<Task> RunAsync, string? Keys = null, bool AdminOnly = false);

/// <summary>
/// Every command the palette can offer (UX-BRIEF-admin, command palette). The navigation commands are built in; a screen that has commands of its own (a ticket) registers them with
/// <see cref="Register"/> while it is on screen and disposes the registration when it leaves. <see cref="Available"/> is the one place that decides who sees what: an admin command is
/// never in the list for an agent who is not an Admin. Scoped: one per circuit.
/// </summary>
public sealed class CommandRegistry(NavigationManager navigation)
{
    private readonly List<Registration> _registrations = [];

    /// <summary>The commands this agent may see, in a stable order: the built-in navigation, then the admin pages, then what screens registered.</summary>
    public IReadOnlyList<PaletteCommand> Available(bool isAdmin)
    {
        List<PaletteCommand> all = [.. BuiltIn(), .. _registrations.SelectMany(r => r.Commands)];
        return [.. all.Where(command => isAdmin || !command.AdminOnly)];
    }

    /// <summary>Adds a screen's commands until the returned registration is disposed. Registering again from the same screen means disposing the earlier registration first.</summary>
    public IDisposable Register(IReadOnlyList<PaletteCommand> commands)
    {
        var registration = new Registration(this, commands);
        _registrations.Add(registration);
        return registration;
    }

    /// <summary>The commands whose label or group contains every word of <paramref name="query"/> (case-insensitive). Blank is everything.</summary>
    public static IReadOnlyList<PaletteCommand> Filter(IReadOnlyList<PaletteCommand> commands, string? query)
    {
        var terms = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.Length == 0
            ? commands
            : [.. commands.Where(command => terms.All(term =>
                command.Label.Contains(term, StringComparison.OrdinalIgnoreCase) || command.Group.Contains(term, StringComparison.OrdinalIgnoreCase)))];
    }

    private IEnumerable<PaletteCommand> BuiltIn()
    {
        foreach (var view in QueueViews.All)
        {
            var path = $"/queue/{QueueViews.Slug(view)}";
            yield return new PaletteCommand($"go-queue-{QueueViews.Slug(view)}", $"{ShellCopy.QueueLink}: {view}", PaletteCopy.GoToGroup, () => Go(path));
        }

        yield return new PaletteCommand("go-my-settings", ShellCopy.MySettingsLink, PaletteCopy.GoToGroup, () => Go("/account/notifications"));
        yield return new PaletteCommand("go-products", ShellCopy.ProductsLink, PaletteCopy.AdminGroup, () => Go("/settings/products"), AdminOnly: true);
        yield return new PaletteCommand("go-agents", ShellCopy.AgentsLink, PaletteCopy.AdminGroup, () => Go("/settings/agents"), AdminOnly: true);
        yield return new PaletteCommand("go-tags", ShellCopy.TagsLink, PaletteCopy.AdminGroup, () => Go("/settings/tags"), AdminOnly: true);
        yield return new PaletteCommand("go-audit", ShellCopy.AuditLink, PaletteCopy.AdminGroup, () => Go("/settings/audit"), AdminOnly: true);
        yield return new PaletteCommand("go-failed-emails", ShellCopy.FailedEmailsLink, PaletteCopy.AdminGroup, () => Go("/ops/dead-letters"), AdminOnly: true);
    }

    private Task Go(string path)
    {
        navigation.NavigateTo(path);
        return Task.CompletedTask;
    }

    private sealed class Registration(CommandRegistry owner, IReadOnlyList<PaletteCommand> commands) : IDisposable
    {
        public IReadOnlyList<PaletteCommand> Commands { get; } = commands;

        public void Dispose() => owner._registrations.Remove(this);
    }
}
```

`src/TechStrap.Admin/Features/Shell/PaletteCopy.cs`

```csharp
namespace TechStrap.Admin.Features.Shell;

/// <summary>The words of the command palette. Plain, sentence case (docs/BRAND.md section 3).</summary>
public static class PaletteCopy
{
    public const string Title = "Command palette";
    public const string InputLabel = "Type a command";
    public const string ListLabel = "Commands";
    public const string Empty = "No command matches.";
    public const string CommandFailed = "Couldn't run that command.";

    public const string GoToGroup = "Go to";
    public const string AdminGroup = "Admin";
    public const string TicketGroup = "Ticket";

    public const string ReplyCommand = "Reply to requester";
    public const string NoteCommand = "Add internal note";
    public const string AssignToMeCommand = "Assign to me";
    public const string NotSpamCommand = "Not spam";

    /// <summary>Read out when the list changes: "3 commands", "1 command".</summary>
    public static string Count(int count) => count == 1 ? "1 command" : $"{count} commands";
}
```

`src/TechStrap.Admin/Features/Shell/ShellServiceCollectionExtensions.cs`

Replace

```csharp
        services.AddScoped<ShortcutService>();
        services.AddScoped<PreferencesService>();
        services.AddScoped<LocalTimeService>();
        services.AddScoped<FailedEmailCounter>();
        return services;
    }
```

with

```csharp
        services.AddScoped<ShortcutService>();
        services.AddScoped<PreferencesService>();
        services.AddScoped<LocalTimeService>();
        services.AddScoped<CommandRegistry>();
        services.AddScoped<FailedEmailCounter>();
        return services;
    }
```

`src/TechStrap.Admin/Features/Shell/ShortcutCatalog.cs` (2 edits)

Replace

```csharp
        new("u", "Spam view, flagged ticket", "Not spam: restore the selected ticket from spam, no dialog"),
        new("Ctrl+Enter", "Reply box", "Send in the current mode"),
        new("Esc", "Anywhere", "Leave a field (your text is kept), close a dialog, or go back to the queue"),
        new("?", "Anywhere", "Show this list"),
    ];
```

with

```csharp
        new("u", "Spam view, flagged ticket", "Not spam: restore the selected ticket from spam, no dialog"),
        new("Ctrl+Enter", "Reply box", "Send in the current mode"),
        new("Esc", "Anywhere", "Leave a field (your text is kept), close a dialog, or go back to the queue"),
        new("Ctrl+K / Cmd+K", "Anywhere", "Open the command palette (it works while you type, and when single-key shortcuts are off)"),
        new("?", "Anywhere", "Show this list"),
    ];
```

Replace

```csharp
        ("n", "note"),
        ("e", "assign"),
        ("/", "search"),
        ("?", "help"),
    ];
}
```

with

```csharp
        ("n", "note"),
        ("e", "assign"),
        ("/", "search"),
        ("Ctrl K", "commands"),
        ("?", "help"),
    ];
}
```

`src/TechStrap.Admin/Features/Shell/ShortcutService.cs` (4 edits)

Replace

```csharp
    Send,
    Escape,
    Help,
}

/// <summary>What <c>wwwroot/js/shortcuts.js</c> reports for one key press. The script decides <see cref="Typing"/> and <see cref="OnBody"/> from the focused element.</summary>
```

with

```csharp
    Send,
    Escape,
    Help,

    /// <summary>Ctrl+K or Cmd+K: open or close the command palette. Not a single-key shortcut, so the My settings switch does not turn it off (WCAG 2.1.4).</summary>
    Palette,

    /// <summary>Has no key. The command palette raises it for "Assign to me" on a ticket.</summary>
    AssignToMe,
}

/// <summary>What <c>wwwroot/js/shortcuts.js</c> reports for one key press. The script decides <see cref="Typing"/> and <see cref="OnBody"/> from the focused element.</summary>
```

Replace

```csharp
/// <summary>
/// The keyboard layer (UX-BRIEF-admin, Density and keyboard shortcuts). The script reports key presses; this class decides whether one is a
/// shortcut and tells whoever subscribed. Pages and components subscribe to <see cref="Pressed"/> while they are on screen and ignore what is not theirs.
/// Single-key shortcuts are off while the user types, and when <see cref="SingleKeyEnabled"/> is false (WCAG 2.1.4). The command palette is deferred to PHASE-07c.
/// </summary>
public sealed class ShortcutService(IJSRuntime js) : IAsyncDisposable
{
```

with

```csharp
/// <summary>
/// The keyboard layer (UX-BRIEF-admin, Density and keyboard shortcuts). The script reports key presses; this class decides whether one is a
/// shortcut and tells whoever subscribed. Pages and components subscribe to <see cref="Pressed"/> while they are on screen and ignore what is not theirs.
/// Single-key shortcuts are off while the user types, and when <see cref="SingleKeyEnabled"/> is false (WCAG 2.1.4). Ctrl+K (Cmd+K) is a chord, not a single key: it opens the
/// command palette from anywhere, typing included, and the switch does not turn it off. The palette raises the same actions through <see cref="RaiseAsync"/>, so a ticket command and its key
/// run the same code.
/// </summary>
public sealed class ShortcutService(IJSRuntime js) : IAsyncDisposable
{
```

Replace

```csharp
    {
        if (press.Ctrl || press.Meta)
        {
            return press.Key == "Enter" && press.Scope == ComposerScope ? ShortcutAction.Send : null;
        }
```

with

```csharp
    {
        if (press.Ctrl || press.Meta)
        {
            if (!press.Alt && string.Equals(press.Key, "k", StringComparison.OrdinalIgnoreCase))
            {
                return ShortcutAction.Palette;
            }

            return press.Key == "Enter" && press.Scope == ComposerScope ? ShortcutAction.Send : null;
        }
```

Replace

```csharp
    public async Task OnKeyAsync(KeyPress press)
    {
        var action = Map(press, SingleKeyEnabled);
        if (action is null || Pressed is null)
        {
            return;
        }

        foreach (var handler in Pressed.GetInvocationList().Cast<Func<ShortcutAction, Task>>())
        {
            await handler(action.Value);
        }
    }
```

with

```csharp
    public async Task OnKeyAsync(KeyPress press)
    {
        var action = Map(press, SingleKeyEnabled);
        if (action is not null)
        {
            await RaiseAsync(action.Value);
        }
    }

    /// <summary>Tells every subscriber that <paramref name="action"/> happened, in subscription order, as if its key had been pressed. The command palette uses this for ticket commands.</summary>
    public async Task RaiseAsync(ShortcutAction action)
    {
        if (Pressed is null)
        {
            return;
        }

        foreach (var handler in Pressed.GetInvocationList().Cast<Func<ShortcutAction, Task>>())
        {
            await handler(action);
        }
    }
```

`src/TechStrap.Admin/Features/Tickets/TicketActions.razor`

Replace

```razor
@if (HasAnyAction)
{
    <div class="ts-actions">
        <button type="button" class="btn btn-outline-secondary" aria-haspopup="menu" aria-expanded="@(_menuOpen ? "true" : "false")" @onclick="ToggleMenu">@ActionsCopy.MoreActions</button>
        <ul class="ts-menu" hidden="@(!_menuOpen)">
            @if (CanMarkSpam)
            {
                <li><button type="button" class="ts-menu-item" @onclick="() => Open(ActionDialog.Spam)">@ActionsCopy.MarkSpam</button></li>
            }
            @if (CanRestore)
            {
                <li><button type="button" class="ts-menu-item" @onclick="RestoreAsync">@ActionsCopy.NotSpam <Kbd>u</Kbd></button></li>
            }
            @if (Session.IsAdmin)
            {
                <li><button type="button" class="ts-menu-item ts-menu-item--danger" @onclick="() => Open(ActionDialog.Delete)">@ActionsCopy.DeleteTicket</button></li>
                <li><button type="button" class="ts-menu-item ts-menu-item--danger" @onclick="() => Open(ActionDialog.Erase)">@ActionsCopy.EraseRequester</button></li>
            }
        </ul>
    </div>
```

with

```razor
@if (HasAnyAction)
{
    <div class="ts-actions" @ref="_root">
        <button type="button" id="@ToggleId" class="btn btn-outline-secondary" aria-haspopup="menu" aria-expanded="@(_menuOpen ? "true" : "false")" aria-controls="@MenuId" @onclick="ToggleMenu">@ActionsCopy.MoreActions</button>
        <ul id="@MenuId" @ref="_menu" class="ts-menu" role="menu" aria-labelledby="@ToggleId" hidden="@(!_menuOpen)">
            @if (CanMarkSpam)
            {
                <li role="none"><button type="button" role="menuitem" tabindex="-1" class="ts-menu-item" @onclick="() => Open(ActionDialog.Spam)">@ActionsCopy.MarkSpam</button></li>
            }
            @if (CanRestore)
            {
                <li role="none"><button type="button" role="menuitem" tabindex="-1" class="ts-menu-item" @onclick="RestoreAsync">@ActionsCopy.NotSpam <Kbd>u</Kbd></button></li>
            }
            @if (Session.IsAdmin)
            {
                <li role="none"><button type="button" role="menuitem" tabindex="-1" class="ts-menu-item ts-menu-item--danger" @onclick="() => Open(ActionDialog.Delete)">@ActionsCopy.DeleteTicket</button></li>
                <li role="none"><button type="button" role="menuitem" tabindex="-1" class="ts-menu-item ts-menu-item--danger" @onclick="() => Open(ActionDialog.Erase)">@ActionsCopy.EraseRequester</button></li>
            }
        </ul>
    </div>
```

`src/TechStrap.Admin/Features/Tickets/TicketActions.razor.cs` (5 edits)

Replace

```csharp
using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
```

with

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
```

Replace

```csharp
/// the ticket. Delete and erase are Admin-only, irreversible, and need the ticket number or the requester's email typed: for an Agent they are not hidden but not rendered at all (no menu
/// entry, no dialog, nothing to trigger), and the API enforces the same rule. A failure leaves the dialog open and everything unchanged; only success navigates. A write is never cancelled
/// when the component goes away, and a failure that leaves the outcome unknown says so and offers a reload instead of a retry.
/// </summary>
public sealed partial class TicketActions : IDisposable
{
    private const string QueuePath = "/queue";

    private ActionDialog _dialog;
    private bool _menuOpen;
```

with

```csharp
/// the ticket. Delete and erase are Admin-only, irreversible, and need the ticket number or the requester's email typed: for an Agent they are not hidden but not rendered at all (no menu
/// entry, no dialog, nothing to trigger), and the API enforces the same rule. A failure leaves the dialog open and everything unchanged; only success navigates. A write is never cancelled
/// when the component goes away, and a failure that leaves the outcome unknown says so and offers a reload instead of a retry.
/// The menu follows the WAI-ARIA menu button pattern: the list has <c>role="menu"</c> and its buttons <c>role="menuitem"</c>; opening it puts focus on the first item; ArrowDown, ArrowUp, Home and
/// End move between items; Escape closes it and returns focus to the button; Tab closes it. The keys are handled in <c>menu.js</c>, which tells this component when to open or close.
/// </summary>
public sealed partial class TicketActions : IDisposable, IAsyncDisposable
{
    private const string QueuePath = "/queue";
    private const string MenuModule = "./js/menu.js";
    private static int _nextId;

    private readonly int _id = Interlocked.Increment(ref _nextId);
    private ElementReference _root;
    private ElementReference _menu;
    private IJSObjectReference? _menuScript;
    private DotNetObjectReference<TicketActions>? _self;
    private bool _attached;
    private bool _focusFirst;

    private ActionDialog _dialog;
    private bool _menuOpen;
```

Replace

```csharp
    private bool _uncertain;
    private bool _notSpamUncertain;
    private string? _error;

    [Inject]
    private ITicketsClient Tickets { get; set; } = default!;
```

with

```csharp
    private bool _uncertain;
    private bool _notSpamUncertain;
    private string? _error;

    [Inject]
    private IJSRuntime Js { get; set; } = default!;

    [Inject]
    private ILogger<TicketActions> Logger { get; set; } = default!;

    [Inject]
    private ITicketsClient Tickets { get; set; } = default!;
```

Replace

```csharp

    private bool HasAnyAction => CanMarkSpam || CanRestore || Session.IsAdmin;

    protected override void OnInitialized() => Shortcuts.Pressed += OnShortcutAsync;

    private void ToggleMenu() => _menuOpen = !_menuOpen;

    private void Open(ActionDialog dialog)
    {
```

with

```csharp

    private bool HasAnyAction => CanMarkSpam || CanRestore || Session.IsAdmin;

    private string ToggleId => $"ts-actions-toggle-{_id}";

    private string MenuId => $"ts-actions-menu-{_id}";

    protected override void OnInitialized() => Shortcuts.Pressed += OnShortcutAsync;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed || !HasAnyAction)
        {
            return;
        }

        try
        {
            if (!_attached)
            {
                _menuScript ??= await Js.InvokeAsync<IJSObjectReference>("import", MenuModule);
                _self ??= DotNetObjectReference.Create(this);
                await _menuScript.InvokeVoidAsync("attach", _root, _self);
                _attached = true;
            }

            if (_focusFirst && _menuOpen)
            {
                _focusFirst = false;
                await _menuScript!.InvokeVoidAsync("focusFirst", _menu);
            }
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException)
        {
            // OnAfterRenderAsync must never throw: that would end the circuit. The menu still works with the mouse; log the type only.
            Logger.LogWarning("The actions menu keyboard script failed ({ExceptionType}).", ex.GetType().Name);
        }
    }

    private void ToggleMenu()
    {
        _menuOpen = !_menuOpen;
        _focusFirst = _menuOpen;
    }

    /// <summary>Called by <c>menu.js</c> for ArrowDown on the closed menu button.</summary>
    [JSInvokable]
    public Task OpenMenu() => InvokeAsync(() =>
    {
        _menuOpen = true;
        _focusFirst = true;
        StateHasChanged();
    });

    /// <summary>Called by <c>menu.js</c> for Escape and Tab in the open menu.</summary>
    [JSInvokable]
    public Task CloseMenu() => InvokeAsync(() =>
    {
        _menuOpen = false;
        StateHasChanged();
    });

    private void Open(ActionDialog dialog)
    {
```

Replace

```csharp
    {
        _disposed = true;
        Shortcuts.Pressed -= OnShortcutAsync;
    }
}
```

with

```csharp
    {
        _disposed = true;
        Shortcuts.Pressed -= OnShortcutAsync;
        _self?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        if (_menuScript is null)
        {
            return;
        }

        try
        {
            await _menuScript.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The circuit is already gone.
        }
    }
}
```

`src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor.cs` (9 edits)

Replace

```csharp
using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;
using TechStrap.Contracts.Tickets;
```

with

```csharp
using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;
using TechStrap.Contracts.Tickets;
```

Replace

```csharp
    private ConflictState _conflict;
    private bool _reloading;
    private string? _latestChange;

    [Inject]
    private TicketDetailPresenter Presenter { get; set; } = default!;
```

with

```csharp
    private ConflictState _conflict;
    private bool _reloading;
    private string? _latestChange;
    private IDisposable? _palette;

    [Inject]
    private TicketDetailPresenter Presenter { get; set; } = default!;
```

Replace

```csharp

    [Inject]
    private ShortcutService Shortcuts { get; set; } = default!;

    /// <summary>The ticket number from the route, for example <c>ORB-42</c>.</summary>
    [Parameter]
```

with

```csharp

    [Inject]
    private ShortcutService Shortcuts { get; set; } = default!;

    [Inject]
    private CommandRegistry Commands { get; set; } = default!;

    [Inject]
    private AgentSession Session { get; set; } = default!;

    /// <summary>The ticket number from the route, for example <c>ORB-42</c>.</summary>
    [Parameter]
```

Replace

```csharp
        _loading = true;
        _gone = null;
        _model = null;
        _error = string.Empty;
        await LoadCoreAsync(silent: false);
    }
```

with

```csharp
        _loading = true;
        _gone = null;
        _model = null;
        SyncPalette();
        _error = string.Empty;
        await LoadCoreAsync(silent: false);
    }
```

Replace

```csharp
        if (result.IsSuccess)
        {
            _model = result.Value;
            _error = string.Empty;
            return true;
        }
```

with

```csharp
        if (result.IsSuccess)
        {
            _model = result.Value;
            SyncPalette();
            _error = string.Empty;
            return true;
        }
```

Replace

```csharp
        if (error.Kind == ResultErrorKind.NotFound)
        {
            _model = null;
            _gone = silent
                ? new GoneMessage(TicketCopy.GoneHeading, TicketCopy.GoneBody)
                : new GoneMessage(TicketCopy.NotFoundHeading, TicketCopy.NotFoundBody);
```

with

```csharp
        if (error.Kind == ResultErrorKind.NotFound)
        {
            _model = null;
            SyncPalette();
            _gone = silent
                ? new GoneMessage(TicketCopy.GoneHeading, TicketCopy.GoneBody)
                : new GoneMessage(TicketCopy.NotFoundHeading, TicketCopy.NotFoundBody);
```

Replace

```csharp
        if (_model is not null)
        {
            _model = _model.WithState(state);
            StateHasChanged();
        }
    }
```

with

```csharp
        if (_model is not null)
        {
            _model = _model.WithState(state);
            SyncPalette();
            StateHasChanged();
        }
    }
```

Replace

```csharp

    private void DismissConflict() => _conflict = ConflictState.None;

    private Task OnShortcutAsync(ShortcutAction action)
    {
        if (action == ShortcutAction.Escape)
```

with

```csharp

    private void DismissConflict() => _conflict = ConflictState.None;

    /// <summary>
    /// Offers this ticket's commands in the command palette while the ticket is on screen: reply and note on an open ticket, "Assign to me" when it is not already assigned to the agent,
    /// and "Not spam" on a flagged one. Each raises the shortcut that does the same thing, so the owner of the action (the composer, the sidebar, the actions menu) runs its own code.
    /// Called whenever the model changes, so the list never offers what the ticket no longer allows.
    /// </summary>
    private void SyncPalette()
    {
        _palette?.Dispose();
        _palette = null;
        if (_disposed || _model is not { IsClosed: false } ticket)
        {
            return;
        }

        List<PaletteCommand> commands =
        [
            new("ticket-reply", PaletteCopy.ReplyCommand, PaletteCopy.TicketGroup, () => Shortcuts.RaiseAsync(ShortcutAction.Reply), Keys: "r"),
            new("ticket-note", PaletteCopy.NoteCommand, PaletteCopy.TicketGroup, () => Shortcuts.RaiseAsync(ShortcutAction.Note), Keys: "n"),
        ];
        if (Session.Agent is { } me && ticket.AssigneeId != me.Id)
        {
            commands.Add(new("ticket-assign-me", PaletteCopy.AssignToMeCommand, PaletteCopy.TicketGroup, () => Shortcuts.RaiseAsync(ShortcutAction.AssignToMe)));
        }

        if (ticket.IsSpam)
        {
            commands.Add(new("ticket-not-spam", PaletteCopy.NotSpamCommand, PaletteCopy.TicketGroup, () => Shortcuts.RaiseAsync(ShortcutAction.NotSpam), Keys: "u"));
        }

        _palette = Commands.Register(commands);
    }

    private Task OnShortcutAsync(ShortcutAction action)
    {
        if (action == ShortcutAction.Escape)
```

Replace

```csharp
    public void Dispose()
    {
        _disposed = true;
        Shortcuts.Pressed -= OnShortcutAsync;
        _lifetime.Cancel();
        _load?.Dispose();
```

with

```csharp
    public void Dispose()
    {
        _disposed = true;
        _palette?.Dispose();
        Shortcuts.Pressed -= OnShortcutAsync;
        _lifetime.Cancel();
        _load?.Dispose();
```

`src/TechStrap.Admin/Features/Tickets/TicketSidebar.razor.cs`

Replace

```csharp
        {
            await _assignee.FocusAsync();
        }
    }

    public void Dispose()
```

with

```csharp
        {
            await _assignee.FocusAsync();
        }
        else if (action == ShortcutAction.AssignToMe && CanAssignToMe && !_busy)
        {
            await InvokeAsync(AssignToMeAsync);
        }
    }

    public void Dispose()
```

`src/TechStrap.Admin/Styles/_brand-window.scss`

Replace

```scss
    height: 96px;
  }

  h2 {
    margin: 4px 0 0;
    font: 600 1.1875rem/1.2 var(--ts-font-mono);
```

with

```scss
    height: 96px;
  }

  h1,
  h2 {
    margin: 4px 0 0;
    font: 600 1.1875rem/1.2 var(--ts-font-mono);
```

`src/TechStrap.Admin/Styles/_palette.scss`

```scss
// The command palette (PHASE-07c, UX-BRIEF-admin): a native <dialog> with a combobox input and a listbox. It is working UI like the other dialogs: no window frame, no mascot.

.ts-palette {
  width: min(36rem, calc(100vw - 32px));
  max-height: min(32rem, calc(100vh - 64px));
  margin-top: 12vh;
  padding: 0;
  overflow: hidden;

  &[open] {
    display: flex;
    flex-direction: column;
  }
}

.ts-palette-input {
  flex: none;
  padding: 12px 16px;
  font: 500 .9375rem var(--ts-font-mono);
  border: 0;
  border-bottom: 2px solid var(--ink);
  border-radius: 0;
}

.ts-palette-list {
  flex: 1 1 auto;
  margin: 0;
  padding: 4px 0;
  overflow-y: auto;
  list-style: none;
}

.ts-palette-option {
  display: flex;
  gap: 12px;
  align-items: baseline;
  justify-content: space-between;
  padding: 8px 16px;
  font: 500 .8125rem var(--ts-font-mono);
  cursor: pointer;
  border-left: 3px solid transparent;

  &:hover {
    background: var(--hover);
  }

  // The selected line is set apart by a fill, a bar and weight, never by colour alone (forced-colors drops the fill, so it gets an outline below).
  &[aria-selected="true"] {
    font-weight: 700;
    background: var(--sel);
    border-left-color: var(--margin);
  }
}

.ts-palette-meta {
  display: flex;
  flex: none;
  gap: 8px;
  align-items: baseline;
}

.ts-palette-group {
  font: 400 .6875rem var(--ts-font-mono);
  text-transform: uppercase;
  color: var(--ink-3);
}

.ts-palette-empty {
  margin: 0;
  padding: 12px 16px;
  font: 400 .8125rem var(--ts-font-mono);
}

@media (forced-colors: active) {
  .ts-palette-option[aria-selected="true"] {
    outline: 2px solid Highlight;
    outline-offset: -2px;
  }
}
```

`src/TechStrap.Admin/Styles/app.scss`

Replace

```scss
@import "feedback";
@import "states";
@import "dialog";
@import "statusbar";
@import "queue";
@import "ticket";
```

with

```scss
@import "feedback";
@import "states";
@import "dialog";
@import "palette";
@import "statusbar";
@import "queue";
@import "ticket";
```

`src/TechStrap.Admin/wwwroot/js/menu.js`

```javascript
// The keyboard of a menu button (WAI-ARIA menu button pattern), for the ticket's "More actions" menu. The button has aria-haspopup="menu"; the list has role="menu" and
// its buttons role="menuitem". Which items are shown is .NET's (the menu element is hidden while it is closed); this module only moves focus and reports open and close.
//
//   ArrowDown on the button   opens the menu and focuses the first item (.NET renders it open, then calls focusFirst)
//   ArrowDown / ArrowUp       next / previous item, wrapping round
//   Home / End                first / last item
//   Escape                    closes the menu and returns focus to the button; the key is handled here, so the page's own Escape (back to the queue) does not also fire
//   Tab                       closes the menu and lets focus move on
//
// decideKey is pure (plain objects in, a plain answer out) so tests/TechStrap.Admin.Tests/js/menu.test.mjs can run it under node:test.

const attached = new WeakMap();

/**
 * state: { open (the menu is showing), onButton (focus is on the menu button), count (menu items), current (index of the focused item, -1 for none) }.
 * Returns null (the browser keeps the key), or { preventDefault, action }: action is 'open', 'close', 'close-focus-button' or { focus: index }.
 */
export function decideKey(event, state) {
    if (event.isComposing || event.altKey || event.ctrlKey || event.metaKey) {
        return null;
    }

    if (!state.open) {
        return state.onButton && event.key === 'ArrowDown' ? { preventDefault: true, action: 'open' } : null;
    }

    switch (event.key) {
        case 'Escape':
            return { preventDefault: true, action: 'close-focus-button' };
        case 'Tab':
            return { preventDefault: false, action: 'close' };
        case 'Home':
            return state.count > 0 ? { preventDefault: true, action: { focus: 0 } } : null;
        case 'End':
            return state.count > 0 ? { preventDefault: true, action: { focus: state.count - 1 } } : null;
        case 'ArrowDown':
            return state.count > 0 ? { preventDefault: true, action: { focus: (state.current + 1) % state.count } } : null;
        case 'ArrowUp':
            return state.count > 0 ? { preventDefault: true, action: { focus: state.current <= 0 ? state.count - 1 : state.current - 1 } } : null;
        default:
            return null;
    }
}

function tell(handle, method) {
    try {
        const pending = handle.invokeMethodAsync(method);
        if (pending && typeof pending.catch === 'function') {
            pending.catch(() => { });
        }
    } catch {
        // The circuit is gone; there is nobody to tell.
    }
}

/** Starts the keyboard handling of one menu button wrapper (the element that holds the button and the menu). Safe to call again. */
export function attach(root, handle) {
    if (!root || attached.has(root)) {
        return;
    }

    const listener = (event) => {
        const button = root.querySelector('[aria-haspopup="menu"]');
        const menu = root.querySelector('[role="menu"]');
        if (!button || !menu) {
            return;
        }

        const items = Array.from(menu.querySelectorAll('[role="menuitem"]'));
        const result = decideKey(event, {
            open: !menu.hidden,
            onButton: document.activeElement === button,
            count: items.length,
            current: items.indexOf(document.activeElement),
        });
        if (!result) {
            return;
        }

        if (result.preventDefault) {
            event.preventDefault();
        }

        if (result.action === 'open') {
            tell(handle, 'OpenMenu');
        } else if (result.action === 'close' || result.action === 'close-focus-button') {
            tell(handle, 'CloseMenu');
            if (result.action === 'close-focus-button') {
                button.focus();
            }
        } else if (items[result.action.focus]) {
            items[result.action.focus].focus();
        }
    };

    root.addEventListener('keydown', listener);
    attached.set(root, listener);
}

export function detach(root) {
    const listener = root ? attached.get(root) : null;
    if (listener) {
        root.removeEventListener('keydown', listener);
        attached.delete(root);
    }
}

/** Called after .NET rendered the menu open: puts focus on the first item. */
export function focusFirst(menu) {
    const first = menu ? menu.querySelector('[role="menuitem"]') : null;
    if (first) {
        first.focus();
    }
}
```

`src/TechStrap.Admin/wwwroot/js/palette.js`

```javascript
// The command palette's keyboard plumbing (UX-BRIEF-admin, command palette). The palette is a native <dialog> with a combobox input and a listbox. Typing is the browser's;
// the keys that move the selection are taken here, because only a script can stop the browser from moving the caret on ArrowUp and ArrowDown, and Blazor cannot cancel a key
// per press. The script reports the key to .NET (CommandPalette.NavigateKey), which owns the selection and runs the command.
//
// decideKey is pure so tests/TechStrap.Admin.Tests/js/palette.test.mjs can run it under node:test.

const NAVIGATION_KEYS = new Set(['ArrowDown', 'ArrowUp', 'Home', 'End', 'Enter']);

const attached = new WeakMap();

/** The key to report to .NET, or null when the browser keeps it (typing, Escape, any chord, IME composition). */
export function decideKey(event) {
    if (event.isComposing || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) {
        return null;
    }

    return NAVIGATION_KEYS.has(event.key) ? event.key : null;
}

/** Starts reporting the selection keys of the input to .NET. Safe to call again for the same input. */
export function attach(input, handle) {
    if (!input || attached.has(input)) {
        return;
    }

    const listener = (event) => {
        const key = decideKey(event);
        if (key === null) {
            return;
        }

        event.preventDefault();
        try {
            const pending = handle.invokeMethodAsync('NavigateKey', key);
            if (pending && typeof pending.catch === 'function') {
                pending.catch(() => { });
            }
        } catch {
            // The circuit is gone; there is nobody to tell.
        }
    };

    input.addEventListener('keydown', listener);
    attached.set(input, listener);
}

export function detach(input) {
    const listener = input ? attached.get(input) : null;
    if (listener) {
        input.removeEventListener('keydown', listener);
        attached.delete(input);
    }
}

/** Scrolls the selected option into view inside the list, so ArrowDown past the visible lines does not select something unseen. */
export function reveal(list) {
    const selected = list ? list.querySelector('[aria-selected="true"]') : null;
    if (selected && typeof selected.scrollIntoView === 'function') {
        selected.scrollIntoView({ block: 'nearest' });
    }
}
```

`src/TechStrap.Admin/wwwroot/js/shortcuts.js` (6 edits)

Replace

```javascript
// what they mean. To keep the circuit quiet it reports nothing but the keys the layer can use, and nothing while the user types (except Ctrl/Cmd+Enter,
// which sends from the composer). Escape while typing blurs the field and keeps the text.
//
// The filtering is pure (isTyping, isRelevantKey, decide take plain objects) so tests/TechStrap.Admin.Tests/js/shortcuts.test.mjs can run it under node:test.

const NON_TEXT_INPUTS = new Set(['button', 'checkbox', 'radio', 'submit', 'reset', 'file', 'range', 'color', 'image']);
const EDITABLE_VALUES = new Set(['', 'true', 'plaintext-only']);
```

with

```javascript
// what they mean. To keep the circuit quiet it reports nothing but the keys the layer can use, and nothing while the user types (except Ctrl/Cmd+Enter,
// which sends from the composer). Escape while typing blurs the field and keeps the text.
//
// Ctrl+K (Cmd+K on macOS) opens the command palette from anywhere: while typing, and whatever the My settings switch says (a chord is not a single-key
// shortcut, WCAG 2.1.4). It never opens over another modal dialog, because a confirmation is waiting for an answer; pressed inside the palette it closes it.
//
// The filtering is pure (isTyping, isRelevantKey, isPaletteChord, decide take plain objects) so tests/TechStrap.Admin.Tests/js/shortcuts.test.mjs can run it under node:test.

const NON_TEXT_INPUTS = new Set(['button', 'checkbox', 'radio', 'submit', 'reset', 'file', 'range', 'color', 'image']);
const EDITABLE_VALUES = new Set(['', 'true', 'plaintext-only']);
```

Replace

```javascript
    return RELEVANT.has(key.length === 1 ? key.toLowerCase() : key);
}

/**
 * Decides what one key press means for the page.
 * env: { active (focused element), dialogOpen, scope (nearest data-shortcut-scope), queueOnScreen }.
 * Returns null (ignore), { blur: true } (Escape in a field), or { payload, preventDefault } (report to .NET).
 */
export function decide(event, env) {
```

with

```javascript
    return RELEVANT.has(key.length === 1 ? key.toLowerCase() : key);
}

/** True for Ctrl+K or Cmd+K with no Alt and no Shift (Ctrl+Shift+K is the browser's own developer tools shortcut in Firefox). */
export function isPaletteChord(event) {
    return !!(event.ctrlKey || event.metaKey)
        && !event.altKey
        && !event.shiftKey
        && typeof event.key === 'string'
        && event.key.toLowerCase() === 'k';
}

/**
 * Decides what one key press means for the page.
 * env: { active (focused element), dialogOpen, paletteOpen (the open dialog is the palette), scope (nearest data-shortcut-scope), queueOnScreen }.
 * Returns null (ignore), { blur: true } (Escape in a field), or { payload, preventDefault } (report to .NET).
 */
export function decide(event, env) {
```

Replace

```javascript
        return null;
    }

    // A modal dialog owns the keyboard: Esc and Enter belong to it.
    if (env.dialogOpen) {
        return null;
    }
```

with

```javascript
        return null;
    }

    const palette = isPaletteChord(event);

    // A modal dialog owns the keyboard: Esc and Enter belong to it. The one exception is the palette chord inside the palette itself, which closes it.
    if (env.dialogOpen && !(palette && env.paletteOpen)) {
        return null;
    }
```

Replace

```javascript
    const chord = event.ctrlKey || event.metaKey;
    // Send is Ctrl/Cmd+Enter only: Ctrl+Alt+Enter is a different chord (AltGr on some layouts) and never sends.
    const sendFromComposer = chord && event.key === 'Enter' && !event.altKey;
    if (!sendFromComposer && (typing || chord || event.altKey || !isRelevantKey(key))) {
        return null;
    }
```

with

```javascript
    const chord = event.ctrlKey || event.metaKey;
    // Send is Ctrl/Cmd+Enter only: Ctrl+Alt+Enter is a different chord (AltGr on some layouts) and never sends.
    const sendFromComposer = chord && event.key === 'Enter' && !event.altKey;
    if (!palette && !sendFromComposer && (typing || chord || event.altKey || !isRelevantKey(key))) {
        return null;
    }
```

Replace

```javascript
    const scope = env.scope ?? null;

    // Stop the browser's own use of the key (Firefox quick-find on "/", page scroll on the arrows in the queue, a form submit on Ctrl+Enter).
    const preventDefault = (sendFromComposer && scope === 'composer')
        || key === '/'
        || key === '?'
        || ((key === 'ArrowDown' || key === 'ArrowUp') && onBody && !!env.queueOnScreen);
```

with

```javascript
    const scope = env.scope ?? null;

    // Stop the browser's own use of the key (Firefox quick-find on "/", page scroll on the arrows in the queue, a form submit on Ctrl+Enter).
    const preventDefault = palette
        || (sendFromComposer && scope === 'composer')
        || key === '/'
        || key === '?'
        || ((key === 'ArrowDown' || key === 'ArrowUp') && onBody && !!env.queueOnScreen);
```

Replace

```javascript
        const result = decide(event, {
            active,
            dialogOpen: !!document.querySelector('dialog[open]'),
            scope: scopeElement ? scopeElement.dataset.shortcutScope : null,
            queueOnScreen: !!document.querySelector('[data-shortcut-scope="queue"]'),
        });
```

with

```javascript
        const result = decide(event, {
            active,
            dialogOpen: !!document.querySelector('dialog[open]'),
            paletteOpen: !!document.querySelector('dialog[data-palette][open]'),
            scope: scopeElement ? scopeElement.dataset.shortcutScope : null,
            queueOnScreen: !!document.querySelector('[data-shortcut-scope="queue"]'),
        });
```

- [ ] **Step 4: Run the tests, then prove each pin bites**

```bash
node --test tests/TechStrap.Admin.Tests/js/shortcuts.test.mjs tests/TechStrap.Admin.Tests/js/palette.test.mjs tests/TechStrap.Admin.Tests/js/menu.test.mjs
dotnet test --project tests/TechStrap.Admin.Tests -c Release
pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/AdminScripts.Tests.ps1
```

Expected: all PASS (in the scratch copy `TechStrap.Admin.Tests` went from 1375 to 1454 tests).

Mutations, each applied to the finished code, the tests run, and the change reverted:

| Mutation | Failing tests |
| :-- | :-- |
| `ShortcutService.Map`: Ctrl+K no longer maps to `Palette` | 20 tests: `ShortcutServiceTests.Ctrl_K_and_Cmd_K_open_the_palette_from_anywhere...`, and every `CommandPaletteTests` test that opens the palette |
| `CommandRegistry.Available`: show `AdminOnly` commands to everyone | `CommandPaletteTests.A_plain_agent_is_offered_the_queue_views_and_my_settings_and_never_an_admin_page`, `A_plain_agent_never_sees_an_admin_command_that_a_screen_registered`, `CommandRegistryTests.A_plain_agent_is_offered_the_queue_views_and_my_settings_and_no_admin_page` (8 in all) |
| `CommandPalette.RunAsync`: drop the `command.AdminOnly && !Session.IsAdmin` re-check | `CommandPaletteTests.A_stale_click_on_an_admin_line_does_nothing_once_the_session_is_no_longer_an_admins` |
| `CommandPalette.RunPendingAsync`: do not clear `_pending` before running | `CommandPaletteTests.A_command_fires_exactly_once_even_when_Enter_arrives_twice_and_the_page_renders_again_afterwards` |
| `MainLayout`: open the palette without `Session.State == Ready` | `CommandPaletteTests.It_does_not_open_until_the_api_has_said_who_the_agent_is` |
| `CommandPalette.NavigateKey`: ArrowUp without the wrap | `CommandPaletteTests.The_arrow_keys_move_the_selection_and_wrap_and_the_input_names_the_active_option` |
| `RailLink`: no `aria-current` | `RailLinkTests.The_link_in_the_current_section_is_marked_and_the_others_are_not`, `The_mark_moves_when_the_agent_navigates`, `In_the_rail_exactly_one_link_is_current_and_it_is_the_one_for_the_page` |
| `BrandWindow`: ignore `HeadingLevel` | `BrandWindowTests.The_heading_is_an_h2_by_default_and_an_h1_when_the_window_is_the_whole_page`, `HeadingHostTests` (both pages) |
| `TicketActions.razor`: drop `role="menu"` | `TicketActionsMenuTests.The_button_names_the_menu_and_the_menu_is_labelled_by_the_button` |
| `TicketDetailPage.SyncPalette`: keep the old registration | `TicketPaletteCommandsTests.The_list_follows_the_ticket_after_a_write_changes_it` |
| `TicketDetailPage.Dispose`: do not dispose the registration | `TicketPaletteCommandsTests.Leaving_the_ticket_takes_its_commands_out_of_the_palette` |
| `TicketSidebar`: ignore `ShortcutAction.AssignToMe` | `TicketPaletteCommandsTests.Assign_to_me_from_the_palette_assigns_the_ticket_to_the_agent_with_the_current_row_version`, `The_list_follows_the_ticket_after_a_write_changes_it` |
| `shortcuts.js`: ignore the palette exception inside the palette, or let the chord through over any dialog | `shortcuts.test.mjs` "does not open the palette over another modal dialog, but a press inside the palette closes it" |
| `menu.js`: Escape not taken (`preventDefault: false`) | `menu.test.mjs` "closes on Escape and returns focus to the button, and the key is taken..." |
| `palette.js`: only Enter is taken | `palette.test.mjs` "takes the keys that move the selection and run the command", "reports a selection key to .NET once..." |

- [ ] **Step 5: Build**

```bash
dotnet build TechStrap.slnx -c Release
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add scripts/tests/AdminScripts.Tests.ps1 \
  src/TechStrap.Admin/Components/Layout/CommandPalette.razor \
  src/TechStrap.Admin/Components/Layout/CommandPalette.razor.cs \
  src/TechStrap.Admin/Components/Layout/MainLayout.razor \
  src/TechStrap.Admin/Components/Layout/MainLayout.razor.cs \
  src/TechStrap.Admin/Components/Layout/NavMenu.razor \
  src/TechStrap.Admin/Components/Layout/RailLink.razor \
  src/TechStrap.Admin/Components/Layout/RailLink.razor.cs \
  src/TechStrap.Admin/Components/Pages/NotFound.razor \
  src/TechStrap.Admin/Components/Pages/SignInLanding.razor \
  src/TechStrap.Admin/Components/Ui/BrandWindow.razor \
  src/TechStrap.Admin/Features/Shell/CommandRegistry.cs \
  src/TechStrap.Admin/Features/Shell/PaletteCopy.cs \
  src/TechStrap.Admin/Features/Shell/ShellServiceCollectionExtensions.cs \
  src/TechStrap.Admin/Features/Shell/ShortcutCatalog.cs \
  src/TechStrap.Admin/Features/Shell/ShortcutService.cs \
  src/TechStrap.Admin/Features/Tickets/TicketActions.razor \
  src/TechStrap.Admin/Features/Tickets/TicketActions.razor.cs \
  src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor.cs \
  src/TechStrap.Admin/Features/Tickets/TicketSidebar.razor.cs \
  src/TechStrap.Admin/Styles/_brand-window.scss \
  src/TechStrap.Admin/Styles/_palette.scss \
  src/TechStrap.Admin/Styles/app.scss \
  src/TechStrap.Admin/wwwroot/js/menu.js \
  src/TechStrap.Admin/wwwroot/js/palette.js \
  src/TechStrap.Admin/wwwroot/js/shortcuts.js \
  tests/TechStrap.Admin.Tests/Components/BrandWindowTests.cs \
  tests/TechStrap.Admin.Tests/Components/CommandPaletteTests.cs \
  tests/TechStrap.Admin.Tests/Components/CommandRegistryTests.cs \
  tests/TechStrap.Admin.Tests/Components/RailLinkTests.cs \
  tests/TechStrap.Admin.Tests/Components/ShellComponentTests.cs \
  tests/TechStrap.Admin.Tests/Components/ShortcutServiceTests.cs \
  tests/TechStrap.Admin.Tests/Components/TicketActionsMenuTests.cs \
  tests/TechStrap.Admin.Tests/Components/TicketPaletteCommandsTests.cs \
  tests/TechStrap.Admin.Tests/HeadingHostTests.cs \
  tests/TechStrap.Admin.Tests/PaletteStyleTests.cs \
  tests/TechStrap.Admin.Tests/ScriptHostTests.cs \
  tests/TechStrap.Admin.Tests/Support/AdminComponentTest.cs \
  tests/TechStrap.Admin.Tests/js/menu.test.mjs \
  tests/TechStrap.Admin.Tests/js/palette.test.mjs \
  tests/TechStrap.Admin.Tests/js/shortcuts.test.mjs
git diff --cached --stat
git commit -m "feat(admin): command palette, menu roles and rail current page" -m "Ctrl+K opens a palette of navigation, admin and ticket commands; an admin command is never listed for a plain agent and a command fires once. The actions menu gets menu roles and arrow keys, the rail links say aria-current, and the 404 and sign-in pages get an h1." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```

### Task 8: The responsive and brand pass: a collapsible rail, queue cards, scroll regions, forced colours (P07-T19)

**Review Focus pins:** none of the five is about layout; this task is the rendered half of T19. Its tests read the compiled CSS and the markup (`ResponsiveStyleTests`, `RailToggleTests`, `ScrollRegionSiteTests`), and what they cannot see is the owner's checklist (Task 10).

**Files:**
- Modify: `src/TechStrap.Admin/Components/Layout/NavMenu.razor`
- Modify: `src/TechStrap.Admin/Components/Layout/NavMenu.razor.cs`
- Create: `src/TechStrap.Admin/Components/Ui/ScrollRegion.razor`
- Modify: `src/TechStrap.Admin/Components/Ui/ShellCopy.cs`
- Modify: `src/TechStrap.Admin/Features/Ops/DeadLetters/DeadLettersContent.razor`
- Modify: `src/TechStrap.Admin/Features/Queue/QueueModel.cs`
- Modify: `src/TechStrap.Admin/Features/Queue/TicketQueuePage.razor`
- Modify: `src/TechStrap.Admin/Features/Queue/TicketRow.razor`
- Modify: `src/TechStrap.Admin/Features/Settings/Agents/AgentsContent.razor`
- Modify: `src/TechStrap.Admin/Features/Settings/Audit/AdminEventsContent.razor`
- Modify: `src/TechStrap.Admin/Features/Settings/Products/ApiKeysPanel.razor`
- Modify: `src/TechStrap.Admin/Features/Settings/Products/ProductsContent.razor`
- Modify: `src/TechStrap.Admin/Features/Settings/Tags/TagsContent.razor`
- Modify: `src/TechStrap.Admin/Styles/_queue.scss`
- Create: `src/TechStrap.Admin/Styles/_responsive.scss`
- Modify: `src/TechStrap.Admin/Styles/_shell.scss`
- Modify: `src/TechStrap.Admin/Styles/_ticket.scss`
- Modify: `src/TechStrap.Admin/Styles/app.scss`
- Modify: `tests/Shared/CompiledCss.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/RailToggleTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/Components/TicketQueuePageTests.cs`
- Create: `tests/TechStrap.Admin.Tests/ResponsiveStyleTests.cs`
- Create: `tests/TechStrap.Admin.Tests/ScrollRegionSiteTests.cs`

**Interfaces:**
- Consumes:
  - `NavMenu` and `RailLink` (Task 7), `SignOutForm`, `AgentSession.State == Ready`, `_shell.scss` (the rail, `.ts-rail-link[aria-current="page"]`), `_queue.scss` (the ledger, `.ts-col-*` cells), `_ticket.scss` (`.ts-ticket-layout`, `.ts-side`), `_composer.scss`, `_motion.scss` (the `prefers-reduced-motion` rule, pinned by `StampStyleTests`), Bootstrap's `.table-responsive`, `TicketQueuePage`, `TicketRow`, the six admin tables (products, agents, tags, audit, failed emails, API keys).
  - `CompiledCss` (`tests/Shared`, reads `wwwroot/css/app.css`, which the build compiles from the SCSS and never commits) and the `*StyleTests` pattern.
  - Assumed from Tasks 1 to 5: Task 2 hardens `NavMenu.OnAfterRenderAsync`; this task edits `NavMenu.razor` and adds fields, an `[Inject]` and a `LocationChanged` handler to `NavMenu.razor.cs`, not the render hook. Task 1's rule "no inline `<style>` in `.razor`" and Task 4's `style-src 'self'` both hold: every rule here is in the stylesheet.
- Produces:
  - A Menu button (`ts-rail-toggle`, `aria-expanded`, `aria-controls`) and a `ts-rail-panel` (`data-open`) in `NavMenu`; `ShellCopy.MenuToggle`.
  - `ScrollRegion` (a `role="region"`, named, `tabindex="0"` scroll box) around all seven ledger tables; `QueueCopy.TableLabel`.
  - `ts-ledger--stack` on the queue table and `data-label` on the detail cells of `TicketRow`.
  - `_responsive.scss` (the scroll shadows and the forced-colours rules), changes to `_shell.scss`, `_queue.scss` and `_ticket.scss`.
  - `CompiledCss.InMedia(condition)` and `OutsideMedia()` for tests that care which breakpoint a rule is in.

**Rules:**
1. **Breakpoints** match Bootstrap's `lg` (992 px) and `md` (768 px). The old single 820 px strip is gone.
2. **The rail.** From 992 px the rail is the column it always was: the panel is a flex column inside it and the Menu button is `display: none`. Below 992 px the rail is a bar (brand, then the button on the right), the links are folded away with `display: none` (so they leave the tab order and the accessibility tree until opened) and `data-open="true"` shows them. The button is at least 44 by 44 px, says `aria-expanded`, and names the panel with `aria-controls`. Choosing a page closes the panel (`NavMenu` listens to `LocationChanged`). A visitor with no session sees the brand alone, with no button (the 404 and error pages are static and have no circuit to open anything).
3. **The queue.** From 768 to 992 px it drops Product, Requester and Priority (the order UX-BRIEF-admin proposes), in the queue only: the old rule selected `.ts-ledger th:nth-child(4)` and so also removed the 4th, 5th and 7th header cells of every settings table, leaving their data cells without headers. Below 768 px each ticket is a card: number, status and last activity on the first line, the subject across the full width, then Product, Requester, Priority and Assignee, each with its name beside it (the cell's `data-label`, printed by `::before`). The header row is clipped, not removed, so a screen reader still has the column names; the selected row keeps its bar on the left (the arrow in the first cell is dropped).
4. **The composer stays reachable.** At 1100 px the side panel moved above the conversation, which put the controls between the ticket and the reply box. Below 768 px the controls come after the conversation, so the reply box follows the last message at once.
5. **Tables scroll in their own region.** `ScrollRegion` wraps every ledger table: `table-responsive` (so it scrolls sideways, never the page), `role="region"`, an accessible name (`Tickets`, `Agents`, `Audit events`, ...) and `tabindex="0"`, because a scroll area that only a mouse can use fails WCAG 2.1.1 and axe's "scrollable-region-focusable". A shadow on the edge that has more is the cue (CSS "local" and "scroll" backgrounds). `ScrollRegionSiteTests` fails if a ledger table is ever put anywhere else.
6. **Reduced motion and forced colours.** The `prefers-reduced-motion` rule exists (`_motion.scss`); `ResponsiveStyleTests` now pins it and fails if any stylesheet sets `scroll-behavior`. In forced colours (Windows high contrast) every colour becomes a system colour, so what a fill or an edge colour said alone is said again by shape: the current rail link, tab and queue row get a 2 px `Highlight` outline, the transparent edges of links and tabs become `Canvas` so they do not turn into visible bars, and a focused scroll region gets a ring.
7. **The manual checklist** (1280, 768 and 390 px, a keyboard walk, an axe run, dark and forced-colours emulation, a CSP console check) is written into `docs/development/ADMIN-APP.md` in Task 10, with the rest of the 07c documentation. It needs a signed-in session, so it is the owner's.

- [ ] **Step 1: Write the failing tests**

`CompiledCss` first: the style tests need to know which breakpoint a rule is in, and `Declarations` merges every rule with the same selector whatever its `@media` block.

`tests/Shared/CompiledCss.cs`

Replace

```csharp
    }

    public string Text { get; }

    public static CompiledCss Load(string app)
    {
```

with

```csharp
    }

    public string Text { get; }

    /// <summary>
    /// The rules inside every <c>@media</c> block whose condition is exactly <paramref name="condition"/> as the compiler wrote it, for example <c>(max-width: 767.98px)</c>
    /// (compressed output has no space after <c>@media</c>). Empty when there is no such block, so a test asserting on it fails loudly.
    /// </summary>
    public CompiledCss InMedia(string condition)
    {
        var opening = "@media" + condition + "{";
        var inner = new System.Text.StringBuilder();
        var from = 0;
        while ((from = Text.IndexOf(opening, from, StringComparison.Ordinal)) >= 0)
        {
            var start = from + opening.Length;
            var depth = 1;
            var i = start;
            while (i < Text.Length && depth > 0)
            {
                depth += Text[i] == '{' ? 1 : Text[i] == '}' ? -1 : 0;
                i++;
            }

            inner.Append(Text, start, i - start - 1).Append(' ');
            from = i;
        }

        return new CompiledCss(inner.ToString());
    }

    /// <summary>Everything except the <c>@media</c> blocks: the rules a screen of any size gets. The base rule a media block then overrides.</summary>
    public CompiledCss OutsideMedia()
    {
        var outside = new System.Text.StringBuilder();
        var i = 0;
        while (i < Text.Length)
        {
            if (string.CompareOrdinal(Text, i, "@media", 0, "@media".Length) != 0)
            {
                outside.Append(Text[i++]);
                continue;
            }

            // Skip the whole block: past its opening brace, then to the brace that closes it.
            var depth = 1;
            i = Text.IndexOf('{', i) + 1;
            while (i < Text.Length && depth > 0)
            {
                depth += Text[i] == '{' ? 1 : Text[i] == '}' ? -1 : 0;
                i++;
            }
        }

        return new CompiledCss(outside.ToString());
    }

    public static CompiledCss Load(string app)
    {
```

`tests/TechStrap.Admin.Tests/Components/RailToggleTests.cs`

```csharp
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Components.Layout;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// Below 992px the rail folds behind a Menu button (the CSS decides when; see <c>ResponsiveStyleTests</c>). The button says whether the links are showing with <c>aria-expanded</c>, names
/// the panel it controls, and choosing a link folds the rail away again. A visitor with no session sees the brand and no button.
/// </summary>
public sealed class RailToggleTests : AdminComponentTest
{
    private static readonly CancellationToken Ct = Xunit.TestContext.Current.CancellationToken;

    private async Task<IRenderedComponent<NavMenu>> RenderRailAsync(string role = AgentRoles.Agent)
    {
        this.AddAgentShell(role);
        await Services.GetRequiredService<AgentSession>().EnsureLoadedAsync(Ct);
        return Render<NavMenu>(p => p.SignedIn());
    }

    [Theory]
    [InlineData(AgentRoles.Agent)]
    [InlineData(AgentRoles.Admin)]
    public async Task The_button_starts_closed_and_controls_the_panel_that_holds_every_link(string role)
    {
        var cut = await RenderRailAsync(role);

        var button = cut.Find("button.ts-rail-toggle");
        var panel = cut.Find(".ts-rail-panel");
        button.TextContent.Trim().ShouldBe("Menu");
        button.GetAttribute("type").ShouldBe("button");
        button.GetAttribute("aria-expanded").ShouldBe("false");
        button.GetAttribute("aria-controls").ShouldBe(panel.Id);
        panel.GetAttribute("data-open").ShouldBe("false");
        cut.FindAll("a.ts-rail-link").ShouldAllBe(link => link.Closest(".ts-rail-panel") != null);
        cut.Find("a.ts-brand").Closest(".ts-rail-panel").ShouldBeNull();
    }

    [Fact]
    public async Task Pressing_the_button_opens_the_panel_and_pressing_it_again_closes_it()
    {
        var cut = await RenderRailAsync();

        cut.Find("button.ts-rail-toggle").Click();

        cut.Find("button.ts-rail-toggle").GetAttribute("aria-expanded").ShouldBe("true");
        cut.Find(".ts-rail-panel").GetAttribute("data-open").ShouldBe("true");

        cut.Find("button.ts-rail-toggle").Click();

        cut.Find("button.ts-rail-toggle").GetAttribute("aria-expanded").ShouldBe("false");
        cut.Find(".ts-rail-panel").GetAttribute("data-open").ShouldBe("false");
    }

    [Fact]
    public async Task Choosing_a_page_folds_the_rail_away()
    {
        var cut = await RenderRailAsync();
        cut.Find("button.ts-rail-toggle").Click();

        await cut.InvokeAsync(() => Services.GetRequiredService<NavigationManager>().NavigateTo("/account/notifications"));

        cut.WaitForAssertion(() => cut.Find("button.ts-rail-toggle").GetAttribute("aria-expanded").ShouldBe("false"));
        cut.Find(".ts-rail-panel").GetAttribute("data-open").ShouldBe("false");
    }

    [Fact]
    public async Task A_closed_rail_that_navigates_is_left_alone()
    {
        var cut = await RenderRailAsync();

        await cut.InvokeAsync(() => Services.GetRequiredService<NavigationManager>().NavigateTo("/queue/mine"));

        cut.Find("button.ts-rail-toggle").GetAttribute("aria-expanded").ShouldBe("false");
    }

    [Fact]
    public void Before_the_session_is_known_the_rail_is_the_brand_alone_with_no_button()
    {
        this.AddAgentShell();

        var cut = Render<NavMenu>(p => p.SignedIn());

        cut.FindAll("button.ts-rail-toggle").ShouldBeEmpty();
        cut.FindAll(".ts-rail-panel").ShouldBeEmpty();
        cut.Find("a.ts-brand").ShouldNotBeNull();
    }
}
```

`tests/TechStrap.Admin.Tests/Components/TicketQueuePageTests.cs`

Replace

```csharp
        row.QuerySelector(".ts-priority")!.TextContent.ShouldBe("Urgent");
        row.QuerySelector(".ts-avatar")!.TextContent.ShouldContain("SO");
        row.QuerySelector("time")!.TextContent.ShouldBe("5 min ago");
    }

    [Fact]
```

with

```csharp
        row.QuerySelector(".ts-priority")!.TextContent.ShouldBe("Urgent");
        row.QuerySelector(".ts-avatar")!.TextContent.ShouldContain("SO");
        row.QuerySelector("time")!.TextContent.ShouldBe("5 min ago");
    }

    [Fact]
    public void Every_detail_cell_of_a_row_names_itself_so_the_phone_card_can_show_the_label_beside_it()
    {
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Page([TestData.Summary("ORB-42", assignee: "Sam Ortiz")])));

        var cut = RenderQueue("open");

        var row = cut.Find("tbody tr");
        row.QuerySelector(".ts-col-product")!.GetAttribute("data-label").ShouldBe("Product");
        row.QuerySelector(".ts-col-requester")!.GetAttribute("data-label").ShouldBe("Requester");
        row.QuerySelector(".ts-col-priority")!.GetAttribute("data-label").ShouldBe("Priority");
        row.QuerySelector(".ts-col-assignee")!.GetAttribute("data-label").ShouldBe("Assignee");
        row.QuerySelector(".ts-col-activity")!.GetAttribute("data-label").ShouldBe("Last activity");
        row.QuerySelector(".ts-col-number")!.HasAttribute("data-label").ShouldBeFalse();
        row.QuerySelector(".ts-col-subject")!.HasAttribute("data-label").ShouldBeFalse();
        cut.Find("table.ts-ledger").ClassList.ShouldContain("ts-ledger--stack");
        cut.Find("table.ts-ledger").Closest(".ts-scroll")!.GetAttribute("aria-label").ShouldBe("Tickets");
    }

    [Fact]
```

`tests/TechStrap.Admin.Tests/ResponsiveStyleTests.cs`

```csharp
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>
/// The responsive and accessibility rules (UX-BRIEF-admin, "Responsive and accessibility"): the rail folds away below 992px, the queue stacks below 768px, a wide table scrolls in its own region,
/// the composer follows the conversation on a phone, and forced colours keep what colour alone would say. Reads the compiled CSS, so a rule that is renamed, moved to the wrong breakpoint or
/// deleted fails here. How it looks is the owner's checklist in ADMIN-APP.md.
/// </summary>
public sealed class ResponsiveStyleTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");
    private const string Narrow = "(max-width: 991.98px)";
    private const string Phone = "(max-width: 767.98px)";
    private const string Tablet = "(min-width: 768px)and (max-width: 991.98px)"; // the compressed output has no space before "and"

    [Fact]
    public void The_menu_button_is_hidden_on_a_wide_screen_and_a_44px_target_below_992px()
    {
        Css.OutsideMedia().Declarations(".ts-rail-toggle")["display"].ShouldBe("none");

        var narrow = Css.InMedia(Narrow).Declarations(".ts-rail-toggle");
        narrow["display"].ShouldBe("inline-flex");
        narrow["min-height"].ShouldBe("44px");
        narrow["min-width"].ShouldBe("44px");
    }

    [Fact]
    public void The_links_are_always_shown_on_a_wide_screen_and_folded_away_below_992px_until_the_button_opens_them()
    {
        Css.OutsideMedia().Declarations(".ts-rail-panel")["display"].ShouldBe("flex");

        var narrow = Css.InMedia(Narrow);
        narrow.Declarations(".ts-rail-panel")["display"].ShouldBe("none");
        narrow.Declarations(".ts-rail-panel[data-open=true]")["display"].ShouldBe("flex");
        narrow.Declarations(".ts-shell")["grid-template-columns"].ShouldBe("minmax(0, 1fr)");
    }

    [Fact]
    public void The_old_820px_strip_is_gone_so_the_rail_has_one_breakpoint()
    {
        Css.Text.ShouldNotContain("@media(max-width: 820px){.ts-shell");
        Css.Text.ShouldNotContain(".ts-rail{flex-direction:row;align-items:center;overflow-x:auto");
    }

    [Fact]
    public void A_tablet_drops_product_requester_and_priority_in_the_queue_only()
    {
        var tablet = Css.InMedia(Tablet);

        // One rule for the cells and their headers: Product (4th), Requester (5th) and Priority (7th), in the queue's own table.
        tablet.Declarations(
            ".ts-ledger--stack .ts-col-product,.ts-ledger--stack .ts-col-requester,.ts-ledger--stack .ts-col-priority,"
            + ".ts-ledger--stack th:nth-child(4),.ts-ledger--stack th:nth-child(5),.ts-ledger--stack th:nth-child(7)")["display"].ShouldBe("none");

        // The settings tables share the .ts-ledger class and used to lose their 4th, 5th and 7th header cells at this width: nothing may select them any more.
        Css.Text.ShouldNotContain(".ts-ledger th:nth-child(4)");
        Css.Text.ShouldNotContain(".ts-ledger .ts-col-product");
    }

    [Fact]
    public void A_phone_stacks_each_ticket_into_a_card_with_every_field_back()
    {
        var phone = Css.InMedia(Phone);

        phone.Declarations(".ts-ledger--stack")["display"].ShouldBe("block");
        phone.Declarations(".ts-ledger--stack tbody")["display"].ShouldBe("block");
        phone.Declarations(".ts-ledger--stack tr")["display"].ShouldBe("flex");
        phone.Declarations(".ts-ledger--stack tr")["flex-wrap"].ShouldBe("wrap");
        phone.Declarations(".ts-ledger--stack td")["display"].ShouldBe("block");
        phone.Declarations(".ts-ledger--stack .ts-col-subject")["flex"].ShouldBe("1 0 100%");
        phone.Declarations(".ts-ledger--stack td[data-label]::before")["content"].ShouldBe("attr(data-label) \": \"");

        // Nothing the tablet hid stays hidden on a phone, and the header row is clipped, not removed, so a screen reader still has the column names.
        phone.Declarations(".ts-ledger--stack .ts-col-product").ContainsKey("display").ShouldBeFalse();
        phone.Declarations(".ts-ledger--stack thead")["clip-path"].ShouldBe("inset(50%)");
        phone.Declarations(".ts-ledger--stack thead").ContainsKey("display").ShouldBeFalse();
    }

    [Fact]
    public void A_phone_reads_number_status_and_time_first_then_the_subject_then_the_details()
    {
        var phone = Css.InMedia(Phone);
        string Order(string cell) => phone.Declarations($".ts-ledger--stack .ts-col-{cell}")["order"];

        new[] { "number", "status", "activity", "subject", "product", "requester", "priority", "assignee", "actions" }
            .Select(Order).ShouldBe(["1", "2", "3", "4", "5", "6", "7", "8", "9"]);
    }

    [Fact]
    public void The_selected_row_keeps_a_bar_on_the_left_when_it_is_a_card()
    {
        Css.InMedia(Phone).Declarations(".ts-ledger--stack .ts-row--selected")["box-shadow"].ShouldBe("inset 3px 0 0 var(--margin)");
    }

    [Fact]
    public void On_a_phone_the_composer_follows_the_conversation_and_the_controls_come_after_it()
    {
        Css.InMedia("(max-width: 1100px)").Declarations(".ts-side")["order"].ShouldBe("-1");
        Css.InMedia(Phone).Declarations(".ts-side")["order"].ShouldBe("1");
        Css.InMedia("(max-width: 1100px)").Declarations(".ts-ticket-layout")["grid-template-columns"].ShouldBe("minmax(0, 1fr)");
    }

    [Fact]
    public void A_scroll_region_scrolls_sideways_inside_its_own_box_and_shows_a_shadow_where_there_is_more()
    {
        var scroll = Css.Declarations(".ts-scroll");

        scroll["overflow-x"].ShouldBe("auto");
        scroll["max-width"].ShouldBe("100%");
        scroll["background"].ShouldContain("local");
        scroll["background"].ShouldContain("scroll");
    }

    [Fact]
    public void Forced_colours_keep_the_current_link_tab_and_row_by_an_outline_and_the_focused_scroll_region_by_a_ring()
    {
        var forced = Css.InMedia("(forced-colors: active)");

        forced.Declarations(".ts-rail-link[aria-current=page],.ts-tab[aria-current=page]")["outline"].ShouldBe("2px solid Highlight");
        forced.Declarations(".ts-row--selected")["outline"].ShouldBe("2px solid Highlight");
        forced.Declarations(".ts-scroll:focus-visible")["outline"].ShouldBe("3px solid Highlight");
        forced.Declarations(".ts-rail-link,.ts-tab,.ts-palette-option")["border-color"].ShouldBe("Canvas");
    }

    [Fact]
    public void Reduced_motion_switches_every_animation_and_transition_off_and_nothing_scrolls_smoothly()
    {
        var everything = Css.InMedia("(prefers-reduced-motion: reduce)").Declarations("*,*::before,*::after");
        everything["animation"].ShouldBe("none !important");
        everything["transition"].ShouldBe("none !important");

        var styles = RepositoryRoot.Combine("src", "TechStrap.Admin", "Styles");
        Directory.EnumerateFiles(styles, "*.scss", SearchOption.TopDirectoryOnly)
            .Where(file => File.ReadAllText(file).Contains("scroll-behavior", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }
}
```

`tests/TechStrap.Admin.Tests/ScrollRegionSiteTests.cs`

```csharp
using System.Text.RegularExpressions;
using Bunit;
using TechStrap.Admin.Components.Ui;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>
/// A wide table scrolls inside its own region, and the region is a named, keyboard-reachable box (WCAG 2.1.1; axe: scrollable-region-focusable). The component is the one place that
/// says so, so the site test makes sure no page puts a ledger table anywhere else.
/// </summary>
public sealed partial class ScrollRegionSiteTests : BunitContext
{
    [GeneratedRegex(@"<table class=""table ts-ledger")]
    private static partial Regex LedgerTable();

    [Fact]
    public void The_region_is_a_named_focusable_box_that_holds_what_it_is_given()
    {
        var cut = Render<ScrollRegion>(p => p.Add(r => r.Label, "Tickets").AddChildContent("<table class=\"table\"></table>"));

        var region = cut.Find("div.ts-scroll");
        region.GetAttribute("role").ShouldBe("region");
        region.GetAttribute("aria-label").ShouldBe("Tickets");
        region.GetAttribute("tabindex").ShouldBe("0");
        region.ClassList.ShouldContain("table-responsive");
        region.QuerySelector("table").ShouldNotBeNull();
    }

    [Fact]
    public void Every_ledger_table_in_the_admin_sits_directly_inside_a_scroll_region()
    {
        var admin = RepositoryRoot.Combine("src", "TechStrap.Admin");
        var separator = Path.DirectorySeparatorChar;
        var files = Directory.EnumerateFiles(admin, "*.razor", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{separator}obj{separator}") && !file.Contains($"{separator}bin{separator}"))
            .ToList();

        var tables = 0;
        var unwrapped = new List<string>();
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!LedgerTable().IsMatch(lines[i]))
                {
                    continue;
                }

                tables++;
                if (i == 0 || !lines[i - 1].Contains("<ScrollRegion ", StringComparison.Ordinal))
                {
                    unwrapped.Add($"{Path.GetRelativePath(admin, file).Replace('\\', '/')}:{i + 1}");
                }
            }
        }

        tables.ShouldBe(7);
        unwrapped.ShouldBeEmpty();
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter-class "*ResponsiveStyleTests" --filter-class "*RailToggleTests" --filter-class "*ScrollRegionSiteTests"
```

Expected: the build fails (`ScrollRegion`, `ShellCopy.MenuToggle` are missing, and `NavMenu` has no button); after a stub build, `ResponsiveStyleTests` fails on missing selectors.

- [ ] **Step 3: Implement**

Markup first (`NavMenu`, `ScrollRegion`, the seven tables, `TicketRow`), then the styles. `NavMenu.razor` is given whole because every line of its body moves one level in.

`src/TechStrap.Admin/Components/Layout/NavMenu.razor` (replace the whole file)

```razor
<nav class="ts-rail" aria-label="@ShellCopy.NavigationLabel">
    <a class="ts-brand" href="/" aria-label="TechStrap Admin home">
        <img src="brand/mark.svg" alt="" width="36" height="36" />
        <span>TechStrap<small>Call log / admin</small></span>
    </a>
    @if (Session.State == AgentSessionState.Ready)
    {
        @* Below 992 px the links fold away behind this button (_shell.scss); from 992 px up the button is hidden and the panel is always shown. *@
        <button type="button" class="ts-rail-toggle" aria-expanded="@(_railOpen ? "true" : "false")" aria-controls="@PanelId" @onclick="ToggleRail">@ShellCopy.MenuToggle</button>
        <div id="@PanelId" class="ts-rail-panel" data-open="@(_railOpen ? "true" : "false")">
            <RailLink Href="/queue">@ShellCopy.QueueLink</RailLink>
            @if (Session.IsAdmin)
            {
                <div class="ts-rail-group" role="group" aria-label="@ShellCopy.AdminLinksLabel">
                    <RailLink Href="/settings/products">@ShellCopy.ProductsLink</RailLink>
                    <RailLink Href="/settings/agents">@ShellCopy.AgentsLink</RailLink>
                    <RailLink Href="/settings/tags">@ShellCopy.TagsLink</RailLink>
                    <RailLink Href="/settings/audit">@ShellCopy.AuditLink</RailLink>
                    <RailLink Href="/ops/dead-letters">
                        @ShellCopy.FailedEmailsLink
                        @if (Failed.Count is > 0 and var failed)
                        {
                            <span class="ts-rail-badge" aria-hidden="true">@failed</span>
                            <span class="visually-hidden">@ShellCopy.FailedEmailsCountLabel(failed)</span>
                        }
                    </RailLink>
                </div>
            }
            <div class="ts-rail-user">
                <span class="ts-rail-name">@DisplayName</span>
                @if (Session.IsAdmin)
                {
                    <span class="ts-rail-role">Admin</span>
                }
                <RailLink Href="/account/notifications">@ShellCopy.MySettingsLink</RailLink>
                <SignOutForm />
            </div>
        </div>
    }
</nav>
```

`src/TechStrap.Admin/Components/Layout/NavMenu.razor.cs` (3 edits)

Replace

```csharp
    private readonly CancellationTokenSource _lifetime = new();
    private bool _badgeRequested;
    private bool _disposed;

    [Inject]
    private AgentSession Session { get; set; } = default!;
```

with

```csharp
    private readonly CancellationTokenSource _lifetime = new();
    private bool _badgeRequested;
    private bool _disposed;
    private bool _railOpen;

    [Inject]
    private AgentSession Session { get; set; } = default!;
```

Replace

```csharp
    [Inject]
    private FailedEmailCounter Failed { get; set; } = default!;

    private string DisplayName => Session.Agent is { } agent ? (string.IsNullOrWhiteSpace(agent.Name) ? agent.Email : agent.Name) : string.Empty;

    protected override void OnInitialized()
    {
        Session.Changed += OnChanged;
        Failed.Changed += OnChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
```

with

```csharp
    [Inject]
    private FailedEmailCounter Failed { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private static string PanelId => "ts-rail-panel";

    private void ToggleRail() => _railOpen = !_railOpen;

    private string DisplayName => Session.Agent is { } agent ? (string.IsNullOrWhiteSpace(agent.Name) ? agent.Email : agent.Name) : string.Empty;

    protected override void OnInitialized()
    {
        Session.Changed += OnChanged;
        Failed.Changed += OnChanged;
        Navigation.LocationChanged += OnLocationChanged;
    }

    // Choosing a link folds the rail away again, so the page that was chosen is not left behind the menu on a phone.
    private void OnLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e)
    {
        if (_railOpen)
        {
            _railOpen = false;
            OnChanged();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
```

Replace

```csharp
        _disposed = true;
        Session.Changed -= OnChanged;
        Failed.Changed -= OnChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
```

with

```csharp
        _disposed = true;
        Session.Changed -= OnChanged;
        Failed.Changed -= OnChanged;
        Navigation.LocationChanged -= OnLocationChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
```

`src/TechStrap.Admin/Components/Ui/ScrollRegion.razor`

```razor
@* A wide table scrolls sideways inside this box instead of stretching the page (UX-BRIEF-admin, responsive). The box is a named region that the keyboard can reach, because a scroll
   area that only a mouse can use is a WCAG failure, and it shows a shadow on the edge that has more to see (_responsive.scss). *@
<div class="table-responsive ts-scroll" role="region" tabindex="0" aria-label="@Label">@ChildContent</div>

@code {
    /// <summary>What the region holds: "Tickets", "Agents". A screen reader announces it as the name of the region.</summary>
    [Parameter, EditorRequired]
    public string Label { get; set; } = string.Empty;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}
```

`src/TechStrap.Admin/Components/Ui/ShellCopy.cs`

Replace

```csharp
    public const string AuditLink = "Audit";
    public const string FailedEmailsLink = "Failed emails";
    public const string NavigationLabel = "Admin navigation";
    public const string ShortcutHelpTitle = "Keyboard shortcuts";
    public const string ShortcutHelpOpen = "Shortcuts";
    public const string StatusBarLabel = "Keyboard hints and messages";
```

with

```csharp
    public const string AuditLink = "Audit";
    public const string FailedEmailsLink = "Failed emails";
    public const string NavigationLabel = "Admin navigation";

    /// <summary>The button that opens and closes the rail below 992 px.</summary>
    public const string MenuToggle = "Menu";
    public const string ShortcutHelpTitle = "Keyboard shortcuts";
    public const string ShortcutHelpOpen = "Shortcuts";
    public const string StatusBarLabel = "Keyboard hints and messages";
```

`src/TechStrap.Admin/Features/Ops/DeadLetters/DeadLettersContent.razor` (2 edits)

Replace

```razor
        }
        else
        {
            <div class="table-responsive">
                <table class="table ts-ledger ts-settings-table" aria-busy="@(_loading ? "true" : null)">
                    <thead>
                        <tr>
```

with

```razor
        }
        else
        {
            <ScrollRegion Label="Failed emails">
                <table class="table ts-ledger ts-settings-table" aria-busy="@(_loading ? "true" : null)">
                    <thead>
                        <tr>
```

Replace

```razor
                        }
                    </tbody>
                </table>
            </div>
            <PagerControl Page="_page" PageSize="DeadLettersCopy.PageSize" TotalCount="_total" OnPageChanged="OnPageChanged" />
        }
```

with

```razor
                        }
                    </tbody>
                </table>
            </ScrollRegion>
            <PagerControl Page="_page" PageSize="DeadLettersCopy.PageSize" TotalCount="_total" OnPageChanged="OnPageChanged" />
        }
```

`src/TechStrap.Admin/Features/Queue/QueueModel.cs`

Replace

```csharp
public static class QueueCopy
{
    public const string Heading = "Queue";
    public const string LoadFailed = "Couldn't load tickets.";
    public const string TryAgain = "Try again in a moment.";
    public const string Refresh = "Refresh";
```

with

```csharp
public static class QueueCopy
{
    public const string Heading = "Queue";
    public const string TableLabel = "Tickets";
    public const string LoadFailed = "Couldn't load tickets.";
    public const string TryAgain = "Try again in a moment.";
    public const string Refresh = "Refresh";
```

`src/TechStrap.Admin/Features/Queue/TicketQueuePage.razor` (2 edits)

Replace

```razor

        @if (_rows.Count > 0)
        {
            <div class="table-responsive">
                <table class="table ts-ledger" data-shortcut-scope="queue" aria-busy="@(_loading ? "true" : null)">
                    <thead>
                        <tr>
                            <th scope="col"><span class="visually-hidden">Selection</span></th>
```

with

```razor

        @if (_rows.Count > 0)
        {
            <ScrollRegion Label="@QueueCopy.TableLabel">
                <table class="table ts-ledger ts-ledger--stack" data-shortcut-scope="queue" aria-busy="@(_loading ? "true" : null)">
                    <thead>
                        <tr>
                            <th scope="col"><span class="visually-hidden">Selection</span></th>
```

Replace

```razor
                        }
                    </tbody>
                </table>
            </div>
            <PagerControl Page="_filter.Page" PageSize="QueueDefaults.PageSize" TotalCount="_page!.TotalCount" OnPageChanged="OnPageChangedAsync" />
        }
        else if (_page is not null && _error is null)
```

with

```razor
                        }
                    </tbody>
                </table>
            </ScrollRegion>
            <PagerControl Page="_filter.Page" PageSize="QueueDefaults.PageSize" TotalCount="_page!.TotalCount" OnPageChanged="OnPageChangedAsync" />
        }
        else if (_page is not null && _error is null)
```

`src/TechStrap.Admin/Features/Queue/TicketRow.razor` (2 edits)

Replace

```razor
            <TagChip Name="@tag.Name" Colour="@tag.Colour" />
        }
    </td>
    <td class="ts-col-product"><span class="ts-product">@Row.ProductName</span></td>
    <td class="ts-col-requester">@Row.Requester</td>
    <td class="ts-col-status"><StatusStamp Status="Row.Stamp" /></td>
    <td class="ts-col-priority"><PriorityMark Level="Row.Priority" /></td>
    <td class="ts-col-assignee">
        @if (Row.AssigneeName is not null)
        {
            <span class="ts-avatar" title="@Row.AssigneeName"><span aria-hidden="true">@Row.AssigneeInitials</span><span class="visually-hidden">@Row.AssigneeName</span></span>
```

with

```razor
            <TagChip Name="@tag.Name" Colour="@tag.Colour" />
        }
    </td>
    <td class="ts-col-product" data-label="Product"><span class="ts-product">@Row.ProductName</span></td>
    <td class="ts-col-requester" data-label="Requester">@Row.Requester</td>
    <td class="ts-col-status"><StatusStamp Status="Row.Stamp" /></td>
    <td class="ts-col-priority" data-label="Priority"><PriorityMark Level="Row.Priority" /></td>
    <td class="ts-col-assignee" data-label="Assignee">
        @if (Row.AssigneeName is not null)
        {
            <span class="ts-avatar" title="@Row.AssigneeName"><span aria-hidden="true">@Row.AssigneeInitials</span><span class="visually-hidden">@Row.AssigneeName</span></span>
```

Replace

```razor
            <span class="ts-unassigned"><span aria-hidden="true">&mdash;</span><span class="visually-hidden">Unassigned</span></span>
        }
    </td>
    <td class="ts-col-activity"><RelativeTime When="Row.LastActivity" /></td>
    @if (ShowNotSpam)
    {
        <td class="ts-col-actions">
```

with

```razor
            <span class="ts-unassigned"><span aria-hidden="true">&mdash;</span><span class="visually-hidden">Unassigned</span></span>
        }
    </td>
    <td class="ts-col-activity" data-label="Last activity"><RelativeTime When="Row.LastActivity" /></td>
    @if (ShowNotSpam)
    {
        <td class="ts-col-actions">
```

`src/TechStrap.Admin/Features/Settings/Agents/AgentsContent.razor` (2 edits)

Replace

```razor
        }
        else
        {
            <div class="table-responsive">
                <table class="table ts-ledger ts-settings-table" aria-busy="@(_loading ? "true" : null)">
                    <thead>
                        <tr>
```

with

```razor
        }
        else
        {
            <ScrollRegion Label="Agents">
                <table class="table ts-ledger ts-settings-table" aria-busy="@(_loading ? "true" : null)">
                    <thead>
                        <tr>
```

Replace

```razor
                        }
                    </tbody>
                </table>
            </div>
            <PagerControl Page="_page" PageSize="AgentsCopy.PageSize" TotalCount="_total" OnPageChanged="OnPageChanged" />
        }
```

with

```razor
                        }
                    </tbody>
                </table>
            </ScrollRegion>
            <PagerControl Page="_page" PageSize="AgentsCopy.PageSize" TotalCount="_total" OnPageChanged="OnPageChanged" />
        }
```

`src/TechStrap.Admin/Features/Settings/Audit/AdminEventsContent.razor` (2 edits)

Replace

```razor
        }
        else
        {
            <div class="table-responsive">
                <table class="table ts-ledger ts-settings-table" aria-busy="@(_loading ? "true" : null)">
                    <thead>
                        <tr>
```

with

```razor
        }
        else
        {
            <ScrollRegion Label="Audit events">
                <table class="table ts-ledger ts-settings-table" aria-busy="@(_loading ? "true" : null)">
                    <thead>
                        <tr>
```

Replace

```razor
                        }
                    </tbody>
                </table>
            </div>
            <PagerControl Page="_page" PageSize="AuditCopy.PageSize" TotalCount="_total" OnPageChanged="OnPageChanged" />
        }
    </div>
```

with

```razor
                        }
                    </tbody>
                </table>
            </ScrollRegion>
            <PagerControl Page="_page" PageSize="AuditCopy.PageSize" TotalCount="_total" OnPageChanged="OnPageChanged" />
        }
    </div>
```

`src/TechStrap.Admin/Features/Settings/Products/ApiKeysPanel.razor` (2 edits)

Replace

```razor
    }
    else
    {
        <div class="table-responsive">
            <table class="table ts-ledger ts-settings-table ts-key-table">
                <thead>
                    <tr>
```

with

```razor
    }
    else
    {
        <ScrollRegion Label="API keys">
            <table class="table ts-ledger ts-settings-table ts-key-table">
                <thead>
                    <tr>
```

Replace

```razor
                    }
                </tbody>
            </table>
        </div>
    }

    <ConfirmDialog Open="@(_revoking is not null)" Title="@(_revoking is null ? string.Empty : ApiKeysCopy.RevokeTitle(_revoking.Name))" ConfirmLabel="@ApiKeysCopy.RevokeConfirm" Danger="true"
```

with

```razor
                    }
                </tbody>
            </table>
        </ScrollRegion>
    }

    <ConfirmDialog Open="@(_revoking is not null)" Title="@(_revoking is null ? string.Empty : ApiKeysCopy.RevokeTitle(_revoking.Name))" ConfirmLabel="@ApiKeysCopy.RevokeConfirm" Danger="true"
```

`src/TechStrap.Admin/Features/Settings/Products/ProductsContent.razor` (2 edits)

Replace

```razor
    }
    else
    {
        <div class="table-responsive">
            <table class="table ts-ledger ts-settings-table">
                <thead>
                    <tr>
```

with

```razor
    }
    else
    {
        <ScrollRegion Label="Products">
            <table class="table ts-ledger ts-settings-table">
                <thead>
                    <tr>
```

Replace

```razor
                    }
                </tbody>
            </table>
        </div>
    }
</div>
```

with

```razor
                    }
                </tbody>
            </table>
        </ScrollRegion>
    }
</div>
```

`src/TechStrap.Admin/Features/Settings/Tags/TagsContent.razor` (2 edits)

Replace

```razor
        }
        else
        {
            <div class="table-responsive">
                <table class="table ts-ledger ts-settings-table">
                    <thead>
                        <tr>
```

with

```razor
        }
        else
        {
            <ScrollRegion Label="Tags">
                <table class="table ts-ledger ts-settings-table">
                    <thead>
                        <tr>
```

Replace

```razor
                        }
                    </tbody>
                </table>
            </div>
        }

        <ConfirmDialog Open="@(Deleting is not null)" Title="@(Deleting is null ? string.Empty : TagsCopy.DeleteTitle(Deleting.Name))" ConfirmLabel="@TagsCopy.DeleteConfirm"
```

with

```razor
                        }
                    </tbody>
                </table>
            </ScrollRegion>
        }

        <ConfirmDialog Open="@(Deleting is not null)" Title="@(Deleting is null ? string.Empty : TagsCopy.DeleteTitle(Deleting.Name))" ConfirmLabel="@TagsCopy.DeleteConfirm"
```

`src/TechStrap.Admin/Styles/_queue.scss` (2 edits)

Replace

```scss
  color: var(--ink-2);
}

// Narrow screens drop Product, Requester and Priority first (UX open question 2 proposes this order).
@media (max-width: 820px) {
  .ts-ledger {
    .ts-col-product,
    .ts-col-requester,
    .ts-col-priority,
```

with

```scss
  color: var(--ink-2);
}

// A tablet drops Product, Requester and Priority first (UX open question 2 proposes this order). Only the queue does: the settings tables keep every column and scroll.
@media (min-width: 768px) and (max-width: 991.98px) {
  .ts-ledger--stack {
    .ts-col-product,
    .ts-col-requester,
    .ts-col-priority,
```

Replace

```scss
  }
}

.ts-col-actions {
  white-space: nowrap;
  text-align: right;
```

with

```scss
  }
}

// A phone shows one card per ticket instead of a table: the number, status and last activity on the first line, the subject across the full width, then
// Product, Requester, Priority and Assignee with their names beside them (the data-label of each cell). The header row stays for a screen reader, and the
// selected row keeps its bar on the left (the arrow in the first cell is dropped).
@media (max-width: 767.98px) {
  .ts-ledger--stack {
    display: block;

    thead {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }

    tbody {
      display: block;
    }

    tr {
      display: flex;
      flex-wrap: wrap;
      gap: 2px 12px;
      align-items: baseline;
      padding: 8px 12px 8px 14px;
      border-bottom: 1px solid var(--rule);
    }

    td {
      display: block;
      min-width: 0;
      padding: 0;
      border: 0;
    }

    td[data-label]::before {
      content: attr(data-label) ": ";
      font: 500 .625rem var(--ts-font-mono);
      letter-spacing: .04em;
      text-transform: uppercase;
      color: var(--ink-2);
    }

    .ts-col-mark {
      display: none;
    }

    .ts-col-number {
      order: 1;
    }

    .ts-col-status {
      order: 2;
    }

    .ts-col-activity {
      order: 3;
      margin-left: auto;
    }

    .ts-col-subject {
      flex: 1 0 100%;
      order: 4;
      overflow-wrap: anywhere;
    }

    .ts-col-product {
      order: 5;
    }

    .ts-col-requester {
      order: 6;
    }

    .ts-col-priority {
      order: 7;
    }

    .ts-col-assignee {
      order: 8;
    }

    .ts-col-actions {
      flex-basis: 100%;
      order: 9;
      text-align: left;
    }

    .ts-row--selected {
      box-shadow: inset 3px 0 0 var(--margin);
    }
  }
}

.ts-col-actions {
  white-space: nowrap;
  text-align: right;
```

`src/TechStrap.Admin/Styles/_responsive.scss`

```scss
// Responsive and accessibility rules that cut across screens (PHASE-07c). Imported last, so a rule here wins over the screen it adjusts.

// A wide table scrolls inside its own region (ScrollRegion), never the page. The shadows are the cue that there is more to see: a soft edge appears on the side that has
// more; "local" backgrounds cover it again once the content reaches that side. Keyboard users reach the region with Tab and scroll it with the arrow keys.
.ts-scroll {
  max-width: 100%;
  overflow-x: auto;
  background:
    linear-gradient(to right, var(--paper) 30%, transparent) left center / 24px 100% no-repeat local,
    linear-gradient(to left, var(--paper) 30%, transparent) right center / 24px 100% no-repeat local,
    linear-gradient(to right, var(--shadow), transparent) left center / 10px 100% no-repeat scroll,
    linear-gradient(to left, var(--shadow), transparent) right center / 10px 100% no-repeat scroll;
}

// Forced colours (Windows high contrast) replaces every colour with a system colour, so anything that is said by colour or fill alone is said again by shape.
// The current rail link, tab and row had only a fill and a coloured edge: they get an outline, and the edges that are transparent on purpose stay invisible.
@media (forced-colors: active) {
  .ts-rail-link,
  .ts-tab,
  .ts-palette-option {
    border-color: Canvas;
  }

  .ts-rail-link[aria-current="page"],
  .ts-tab[aria-current="page"] {
    outline: 2px solid Highlight;
    outline-offset: -2px;
  }

  .ts-row--selected {
    outline: 2px solid Highlight;
    outline-offset: -2px;
  }

  .ts-rail-toggle[aria-expanded="true"] {
    outline: 2px solid Highlight;
  }

  .ts-scroll:focus-visible {
    outline: 3px solid Highlight;
  }
}
```

`src/TechStrap.Admin/Styles/_shell.scss` (3 edits)

Replace

```scss
// Admin shell (BRAND.md section 14): a 208px left rail plus the main area. Under 820px the rail becomes a horizontal strip.

.ts-shell {
  display: grid;
```

with

```scss
// Admin shell (BRAND.md section 14): a 208px left rail plus the main area. Below 992px the rail becomes a bar across the top: the brand and a Menu button, with the links folded
// away until the button opens them (aria-expanded). From 992px the button is hidden and the links are always shown.

.ts-shell {
  display: grid;
```

Replace

```scss
  border: 1px solid var(--rule-strong);
}

.ts-main {
  min-width: 0;
}

@media (max-width: 820px) {
  .ts-shell {
    grid-template-columns: minmax(0, 1fr);
    min-height: 0;
  }

  .ts-rail {
    flex-direction: row;
    align-items: center;
    overflow-x: auto;
    padding: 6px 8px;
    border-right: 0;
    border-bottom: 1px solid var(--rule-strong);
  }
```

with

```scss
  border: 1px solid var(--rule-strong);
}

// The links, the admin group and the signed-in user. A plain column inside the rail on a wide screen, so the rail looks as it always did.
.ts-rail-panel {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  gap: 2px;
}

.ts-rail-toggle {
  display: none;
}

.ts-main {
  min-width: 0;
}

@media (max-width: 991.98px) {
  .ts-shell {
    grid-template-columns: minmax(0, 1fr);
    min-height: 0;
  }

  .ts-rail {
    flex-flow: row wrap;
    align-items: center;
    padding: 6px 12px;
    border-right: 0;
    border-bottom: 1px solid var(--rule-strong);
  }
```

Replace

```scss
      width: 28px;
      height: 28px;
    }

    span {
      display: none;
    }
  }

  .ts-rail-link {
    white-space: nowrap;
    border-left: 0;
    border-bottom: 3px solid transparent;
  }
}
```

with

```scss
      width: 28px;
      height: 28px;
    }
  }

  // 44px is the comfortable touch size; WCAG 2.2 AA asks for 24px at least.
  .ts-rail-toggle {
    display: inline-flex;
    gap: 6px;
    align-items: center;
    justify-content: center;
    min-width: 44px;
    min-height: 44px;
    margin-left: auto;
    padding: 0 12px;
    font: 600 .75rem var(--ts-font-mono);
    letter-spacing: .04em;
    text-transform: uppercase;
    color: var(--ink);
    background: var(--sheet);
    border: 2px solid var(--ink);

    &[aria-expanded="true"] {
      background: var(--sel);
    }
  }

  // Folded away until the Menu button opens it. display: none keeps the links out of the tab order and away from a screen reader while they are closed.
  .ts-rail-panel {
    display: none;
    flex-basis: 100%;
    padding-top: 6px;

    &[data-open="true"] {
      display: flex;
    }
  }

  .ts-rail-link {
    align-items: center;
    min-height: 44px;
  }
}
```

`src/TechStrap.Admin/Styles/_ticket.scss`

Replace

```scss
  }
}

.ts-actions-uncertain {
  display: flex;
  flex-wrap: wrap;
```

with

```scss
  }
}

// On a phone the controls come after the conversation, so the reply box follows the last message directly instead of sitting a screenful of controls further down.
@media (max-width: 767.98px) {
  .ts-side {
    order: 1;
  }
}

.ts-actions-uncertain {
  display: flex;
  flex-wrap: wrap;
```

`src/TechStrap.Admin/Styles/app.scss`

Replace

```scss
@import "ops";
@import "styleguide";
@import "motion";
```

with

```scss
@import "ops";
@import "styleguide";
@import "motion";
@import "responsive";
```

- [ ] **Step 4: Run the tests, then prove each pin bites**

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release
```

Expected: all PASS (in the scratch copy 1454 to 1474 tests).

Mutations, each applied to the finished code, the tests run, and the change reverted:

| Mutation | Failing tests |
| :-- | :-- |
| `_shell.scss`: the folded panel stays `display: flex` below 992 px | `ResponsiveStyleTests.The_links_are_always_shown_on_a_wide_screen_and_folded_away_below_992px_until_the_button_opens_them` |
| `NavMenu.razor`: drop `aria-expanded` from the button | `RailToggleTests` (5): the closed state, the toggle, the close on navigation |
| `NavMenu.razor.cs`: do not close the rail on `LocationChanged` | `RailToggleTests.Choosing_a_page_folds_the_rail_away` |
| `ScrollRegion.razor`: drop `tabindex="0"` | `ScrollRegionSiteTests.The_region_is_a_named_focusable_box_that_holds_what_it_is_given` |
| `TagsContent.razor`: something between the region and the table | `ScrollRegionSiteTests.Every_ledger_table_in_the_admin_sits_directly_inside_a_scroll_region` |
| `TicketRow.razor`: drop `data-label="Requester"` | `TicketQueuePageTests.Every_detail_cell_of_a_row_names_itself_so_the_phone_card_can_show_the_label_beside_it` |
| `TicketQueuePage.razor`: no `ts-ledger--stack` on the table | the same test |
| `_queue.scss`: `tr` stays `display: table-row` below 768 px | `ResponsiveStyleTests.A_phone_stacks_each_ticket_into_a_card_with_every_field_back` |
| `_ticket.scss`: the side panel stays first on a phone | `ResponsiveStyleTests.On_a_phone_the_composer_follows_the_conversation_and_the_controls_come_after_it` |

- [ ] **Step 5: Look at it once (not committed)**

The tests cannot tell you the layout looks right. Dump real markup from bUnit and screenshot it against the compiled CSS in headless Chrome. Headless Chrome will not make a window narrower than about 500 px, so the narrow shots are an iframe of the page inside a wider one (media queries answer the iframe's width).

Create `tests/TechStrap.Admin.Tests/Components/HarnessDumpTemp.cs` (delete it afterwards):

```csharp
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Layout;
using TechStrap.Admin.Features.Queue;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

// TEMPORARY, never committed: writes real component markup to a folder so a headless browser can screenshot it against the compiled CSS.
public sealed class HarnessDumpTemp : AdminComponentTest
{
    [Fact]
    public async Task Dump()
    {
        var tickets = Substitute.For<ITicketsClient>();
        tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => TestData.Ok(TestData.Page([
                TestData.Summary("ORB-1", "Cannot log in after password reset on the new device", TicketStatuses.Open, TicketPriorities.Urgent, assignee: "Sam Ortiz", tags: [new TicketTagDto(TestData.BugTagId, "bug", "#DC2626")]),
                TestData.Summary("ORB-2", "Billing question about the annual plan", TicketStatuses.Pending),
                TestData.Summary("ORB-3", "Export fails with error 17", TicketStatuses.New, TicketPriorities.High, assignee: "Ada Admin"),
            ])));
        tickets.GetCountsAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Counts()));
        var products = Substitute.For<IProductsClient>();
        products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.Product()]));
        var tags = Substitute.For<ITagsClient>();
        tags.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<TagDto>>([TestData.Tag()]));
        Services.AddSingleton(tickets);
        Services.AddSingleton(products);
        Services.AddSingleton(tags);
        JSInterop.SetupModule("./js/queue.js").SetupVoid("scrollSelectedIntoView", _ => true).SetVoidResult();
        this.AddAgentShell(AgentRoles.Admin);
        await Services.GetRequiredService<AgentSession>().EnsureLoadedAsync(CancellationToken.None);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/queue/open");
        var rail = Render<NavMenu>(p => p.SignedIn());
        var queue = Render<TicketQueuePage>(p => p.Add(c => c.View, "open"));
        var railOpen = Render<NavMenu>(p => p.SignedIn());
        railOpen.Find("button.ts-rail-toggle").Click();
        var dir = Environment.GetEnvironmentVariable("HARNESS_DIR")!;
        File.WriteAllText(Path.Combine(dir, "rail.html"), rail.Markup);
        File.WriteAllText(Path.Combine(dir, "railopen.html"), railOpen.Markup);
        File.WriteAllText(Path.Combine(dir, "queue.html"), queue.Markup);
    }
}
```

```bash
mkdir -p src/TechStrap.Admin/wwwroot/harness
export HARNESS_DIR="$PWD/src/TechStrap.Admin/wwwroot/harness"
dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter-method "*Dump"
cd src/TechStrap.Admin/wwwroot/harness
python - <<'PY'
head = '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><base href="../"><link rel="stylesheet" href="css/app.css"></head><body>'
queue = open('queue.html', encoding='utf-8').read()
for rail, name in (('rail.html', 'page-closed.html'), ('railopen.html', 'page-open.html')):
    markup = open(rail, encoding='utf-8').read()
    open(name, 'w', encoding='utf-8').write(head + '<div class="ts-shell">' + markup + '<div class="ts-content"><main class="ts-main" id="main">' + queue + '</main></div></div></body></html>')
for width, page in ((390, 'closed'), (390, 'open'), (700, 'closed')):
    open(f'wrap-{width}-{page}.html', 'w').write(f"<!doctype html><body style='margin:0;background:#888'><iframe src='page-{page}.html' width='{width}' height='900' style='border:0;background:#fff'></iframe></body>")
PY
chrome --headless=new --hide-scrollbars --window-size=1280,900 --screenshot=q-1280.png "file://$PWD/page-closed.html"
chrome --headless=new --hide-scrollbars --window-size=800,920 --screenshot=q-390-closed.png "file://$PWD/wrap-390-closed.html"
chrome --headless=new --hide-scrollbars --window-size=800,920 --screenshot=q-390-open.png "file://$PWD/wrap-390-open.html"
chrome --headless=new --hide-scrollbars --window-size=800,920 --screenshot=q-700.png "file://$PWD/wrap-700-closed.html"
cd - && rm -rf src/TechStrap.Admin/wwwroot/harness tests/TechStrap.Admin.Tests/Components/HarnessDumpTemp.cs
```

What to see: at 1280 px the rail is a column with no Menu button and the queue has every column; at 800 px (a tablet) the rail is a bar with a Menu button and the queue has no Product, Requester or Priority; at 390 px each ticket is a card with its labels, the Menu button is a square in the top right, and with the panel open the links stack under the bar. In the scratch copy that is exactly what the four screenshots showed. `git status` must show no `harness` folder and no `HarnessDumpTemp.cs` afterwards.

- [ ] **Step 6: Build**

```bash
dotnet build TechStrap.slnx -c Release
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/TechStrap.Admin/Components/Layout/NavMenu.razor \
  src/TechStrap.Admin/Components/Layout/NavMenu.razor.cs \
  src/TechStrap.Admin/Components/Ui/ScrollRegion.razor \
  src/TechStrap.Admin/Components/Ui/ShellCopy.cs \
  src/TechStrap.Admin/Features/Ops/DeadLetters/DeadLettersContent.razor \
  src/TechStrap.Admin/Features/Queue/QueueModel.cs \
  src/TechStrap.Admin/Features/Queue/TicketQueuePage.razor \
  src/TechStrap.Admin/Features/Queue/TicketRow.razor \
  src/TechStrap.Admin/Features/Settings/Agents/AgentsContent.razor \
  src/TechStrap.Admin/Features/Settings/Audit/AdminEventsContent.razor \
  src/TechStrap.Admin/Features/Settings/Products/ApiKeysPanel.razor \
  src/TechStrap.Admin/Features/Settings/Products/ProductsContent.razor \
  src/TechStrap.Admin/Features/Settings/Tags/TagsContent.razor \
  src/TechStrap.Admin/Styles/_queue.scss \
  src/TechStrap.Admin/Styles/_responsive.scss \
  src/TechStrap.Admin/Styles/_shell.scss \
  src/TechStrap.Admin/Styles/_ticket.scss \
  src/TechStrap.Admin/Styles/app.scss \
  tests/Shared/CompiledCss.cs \
  tests/TechStrap.Admin.Tests/Components/RailToggleTests.cs \
  tests/TechStrap.Admin.Tests/Components/TicketQueuePageTests.cs \
  tests/TechStrap.Admin.Tests/ResponsiveStyleTests.cs \
  tests/TechStrap.Admin.Tests/ScrollRegionSiteTests.cs
git diff --cached --stat
git commit -m "feat(admin): collapsible rail, queue cards and keyboard-reachable scroll regions" -m "Below 992 px the rail folds behind a Menu button; below 768 px the queue is a card per ticket and the composer follows the conversation. Every table scrolls in a named, focusable region, and forced colours keep the current link, tab and row by an outline." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```

### Task 9: Scrub the search from Sentry, document the security schemes in OpenAPI, and close the 07b minors (D-042)

**Review Focus pins:** (4) the text an agent searched for never reaches Sentry (`SensitiveQuerySentryProcessorTests`); the 07b write rules still hold (`UncertainMarksTests`, the paged-page race tests). The OpenAPI schemes are documentation, pinned by `OpenApiSecurityTests`.

**Files:**
- Modify: `src/TechStrap.Admin/Features/Account/NotificationPreferencesPage.razor`
- Modify: `src/TechStrap.Admin/Features/Account/PublicDisplayNameField.razor.cs`
- Modify: `src/TechStrap.Admin/Features/Ops/DeadLetters/DeadLettersContent.razor.cs`
- Modify: `src/TechStrap.Admin/Features/Settings/Agents/AgentsContent.razor.cs`
- Modify: `src/TechStrap.Admin/Features/Settings/Audit/AdminEventsContent.razor.cs`
- Modify: `src/TechStrap.Admin/Features/Settings/Products/ApiKeysPanel.razor.cs`
- Modify: `src/TechStrap.Admin/Features/Settings/Products/ProductEditorViewModel.cs`
- Modify: `src/TechStrap.Admin/Features/Settings/Tags/TagsContent.razor.cs`
- Create: `src/TechStrap.Admin/Features/Shell/UncertainMarks.cs`
- Modify: `src/TechStrap.Api/Program.cs`
- Create: `src/TechStrap.Api/Startup/OpenApiSecurity.cs`
- Modify: `src/TechStrap.Application/Products/UpdateProductRequestHandler.cs`
- Modify: `src/TechStrap.Domain/Products/Product.cs`
- Create: `src/TechStrap.Hosting/Sentry/SensitiveQuerySentryProcessor.cs`
- Modify: `src/TechStrap.Hosting/Sentry/SentryOptionsExtensions.cs`
- Modify: `tests/TechStrap.Admin.Tests/Components/AdminEventsPageTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/Components/AgentsPageTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/Components/DeadLettersPageTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/Components/NotificationPreferencesPageTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/Components/ProductEditorTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/Components/PublicDisplayNameFieldTests.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/UncertainMarksTests.cs`
- Create: `tests/TechStrap.Api.Tests/OpenApiSecurityTests.cs`
- Create: `tests/TechStrap.Api.Tests/SensitiveQuerySentryProcessorTests.cs`
- Modify: `tests/TechStrap.Api.Tests/SentryOptionsExtensionsTests.cs`
- Modify: `tests/TechStrap.Application.Tests/Products/UpdateProductRequestHandlerTests.cs`
- Modify: `tests/TechStrap.Domain.Tests/Products/ProductBrandingLogoTests.cs`

**Interfaces:**
- Consumes:
  - Sentry: `SensitiveHeaderSentryProcessor` and `SentryOptionsExtensions.AddSensitiveHeaderScrubbing()` (called from the `UseSentry` callback of the Api and the Admin today; Task 3 gives the Portal the same call), `SentryEvent.Request`, `SentryTransaction.Request` and `.Spans`, `Breadcrumb`, `SentryOptions.SetBeforeBreadcrumb`; the Sentry SDK is 6.7.0 (`Sentry`, `Sentry.AspNetCore`). `ProcessEnvironmentCollection` (non-parallel tests) for the one test that initialises the SDK, which is process-wide state.
  - OpenAPI: `builder.Services.AddOpenApi()` in the Api `Program.cs`, `Microsoft.AspNetCore.OpenApi` 10.0 over `Microsoft.OpenApi` 2.x (the 2.x scheme and reference types), `AuthorizationPolicies` (`Agent`, `Admin`, `ApiKey`, `Public`), `HeaderNames` (`ApiKey`, `TicketToken`), `OpenApiSurfaceTests` (the document lists exactly the routed controller operations), `ApiFactory`.
  - The 07b pages with held writes: `AgentsContent`, `DeadLettersContent`, `ApiKeysPanel`, `TagsContent` (each has a `_loadId` stale-load guard and a set of rows whose write ended with an unknown outcome), `AdminEventsContent` (the audit page), `PublicDisplayNameField`, `NotificationPreferencesPage` (the `_version` key), `ProductEditorViewModel`, `UpdateProductRequestHandler`, Domain `ProductBranding.Create` and `Guard.OptionalImageUrl`.
  - Assumed from Tasks 1 to 5: Task 3 may move where `AddSensitiveHeaderScrubbing()` is called (a shared host helper). The new processor is registered inside `AddSensitiveHeaderScrubbing()` itself, so wherever a host calls that, it gets both; if Task 3 renames the method, register the new processor in the renamed one. Task 3 also changes Api `Program.cs` (the `HttpClient` default); this task changes only the `AddOpenApi()` line.
- Produces:
  - `SensitiveQuerySentryProcessor` (events, transactions, spans, and the static `ScrubBreadcrumb` for the `BeforeBreadcrumb` hook; `Mask` = `[redacted]`).
  - `OpenApiSecurity.AddTechStrapSecuritySchemes()` (`Bearer`, `ApiKey`, `TicketToken`).
  - `UncertainMarks` (`Contains`, `Add(id, latestLoadId)`, `ReleaseForLoad(loadId)`, `Clear()`).
  - `ProductBranding.CreateForUpdate(current, ...)`; `ProductEditorViewModel.OriginalLogoPath`; `data-revision` on the notification list items.

**Rules:**

*Sentry*
1. **What is masked.** The value of the query parameters `search` (the queue and the ticket list) and `q` (a free-text query), matched by name, case-insensitively, at the start of a query string or after `?` or `&`, up to the next `&`, `#`, space or quote. The value becomes `[redacted]`; the rest of the address stays, so an event still says which page failed. `research`, `faq`, `query` and `/queue/search` are left alone.
2. **Where.** The request query string and URL of every event and transaction; the description and string data of every span of a transaction; the message and data of every breadcrumb, through `BeforeBreadcrumb` (a breadcrumb is immutable, so a masked copy replaces it; one that carries no search is passed on unchanged, the same object).
3. **Registered with the header scrubber**, so every host that scrubs headers scrubs searches too. `BeforeBreadcrumb` has one slot: a host that wants its own must call the registration first and wrap what it needs around it (the doc comment says so).
4. **Proof that it reaches the wire.** One test sends an event and a breadcrumb through the SDK with the host's registration and a capturing transport, and reads the serialised envelope; a control run without the registration must show the secret in at least four places, so the harness cannot pass because it sees nothing.

*OpenAPI*
5. **Three schemes**: `Bearer` (HTTP, `bearer`, JWT) for agents, `ApiKey` (header `X-Api-Key`) for intake, `TicketToken` (header `X-Ticket-Token`) for the customer routes. A document transformer declares them; an operation transformer adds one requirement per operation from its authorization metadata: the Agent or Admin policy is `Bearer`, the ApiKey policy is `ApiKey`, a public operation that reads the `X-Ticket-Token` header is `TicketToken`, any other public operation names none, and `[AllowAnonymous]` wins over everything. It is documentation: the policies on the controllers still decide, and nothing about what the API accepts changes.

*The 07b minors*
6. **Uncertain marks (a).** A write whose answer was lost holds its row until the list has been read again. A read that was already under way when the answer was lost may have been answered before the write landed, so it must not release the hold, or the agent could send the write a second time. Each mark stores the id of the latest read that had started when it was made, and a finishing read releases only marks made before it started. Applied to the four pages that hold a set of rows (agents, failed emails, API-key revokes, tag deletes). Agents and failed emails page, so a second read can be under way (a page change); tags and API keys have no way to start a read while a row is held, so their rule is covered by `UncertainMarksTests` and by the unchanged "released by a later read" tests.
7. **The audit page (c)** draws its events the moment they arrive; the agent list for the actor filter is read in the background and draws when it arrives (the filter has only "everyone" until then). The old code awaited both in `OnParametersSetAsync`, so with a real, slow API the first render waited for the slower read.
8. **The display name (d).** After an uncertain save the stored name is either the committed one or the uncertain one. Retyping either now shows the "may have gone through, reload" message instead of silently doing nothing or sending again. With nothing uncertain, retyping the committed name still does nothing.
9. **The `_version` test (e).** The list items of the notification page are keyed by a revision that a swallowed toggle bumps, so Blazor builds new checkboxes and the browser cannot keep a tick that was never saved. bUnit rebuilds its DOM on every render, so the old reflection test read the private field. The page now shows the revision as `data-revision` (as the ticket sidebar already does) and the test observes that.
10. **A relative logo (f).** A product saved before the logo rule can have a relative logo. Renaming it must not need a logo it never had, and the API must accept an unchanged stored value. `ProductBranding.CreateForUpdate(current, ...)` skips the logo rule when the requested logo (trimmed) equals the stored one, exactly (a different case is a different address). Every other field is always checked, and a different or new logo gets the full rule. `UpdateProductRequestHandler` now loads the product first (so an unknown product with an invalid body is a 404, not a 400) and passes its branding. The Admin editor accepts the loaded value while the field still holds it (`OriginalLogoPath`), so the form no longer shows an error the API would not give; the preview still never loads a relative address.

- [ ] **Step 1: Write the failing tests**

Sentry (its own harness), OpenAPI (the served document), the held marks, the four small page fixes and the logo rule at three levels (Domain, handler, editor).

`tests/TechStrap.Admin.Tests/Components/AdminEventsPageTests.cs`

Replace

```csharp
        cut.WaitForAssertion(() => cut.Find("#ts-audit-actor").QuerySelectorAll("option").Count.ShouldBe(2));
    }

    [Theory]
    [InlineData(1, "1 event")]
    [InlineData(2, "2 events")]
```

with

```csharp
        cut.WaitForAssertion(() => cut.Find("#ts-audit-actor").QuerySelectorAll("option").Count.ShouldBe(2));
    }

    [Fact]
    public void The_events_render_the_moment_they_arrive_while_the_agent_list_is_still_on_its_way()
    {
        var eventsGate = new TaskCompletionSource<Result<PagedResponse<AdminEventDto>>>();
        var agentsGate = new TaskCompletionSource<Result<IReadOnlyList<AgentListItemDto>>>();
        _events.ListAsync(Arg.Any<AdminEventFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(eventsGate.Task);
        _agents.ListAllAsync(Arg.Any<CancellationToken>()).Returns(agentsGate.Task);
        var cut = RenderPage();
        cut.FindAll("tbody tr").ShouldBeEmpty();

        eventsGate.SetResult(TestData.Ok(new PagedResponse<AdminEventDto>([TestData.AdminEvent(AdminEventTypes.TagDeleted, "{\"slug\":\"bug\",\"detachedTicketCount\":2}")], 1, 25, 1)));

        // The agent list has not answered: the rows are drawn without waiting for it, and the filter has only "everyone" until it does.
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
        cut.Find("#ts-audit-actor").QuerySelectorAll("option").Count.ShouldBe(1);
        agentsGate.SetResult(TestData.Ok<IReadOnlyList<AgentListItemDto>>([TestData.AgentRow("Ada Admin", TestData.AdaAgentId, AgentRoles.Admin)]));
        cut.WaitForAssertion(() => cut.Find("#ts-audit-actor").QuerySelectorAll("option").Count.ShouldBe(2));
    }

    [Theory]
    [InlineData(1, "1 event")]
    [InlineData(2, "2 events")]
```

`tests/TechStrap.Admin.Tests/Components/AgentsPageTests.cs`

Replace

```csharp
    }

    [Fact]
    public void An_agent_who_is_already_gone_when_activating_says_so_and_reloads_the_list()
    {
        _agents.SetActiveAsync(Arg.Any<Guid>(), true, Arg.Any<CancellationToken>()).Returns(TestData.Fail<AgentDto>(ApiErrorCodes.AgentNotFound, "No such agent.", ResultErrorKind.NotFound));
```

with

```csharp
    }

    [Fact]
    public void A_read_that_started_before_the_lost_answer_does_not_release_the_hold_and_one_that_started_after_it_does()
    {
        var cut = RenderPage();
        var gate = new TaskCompletionSource<Result<PagedResponse<AgentListItemDto>>>();
        _agents.ListPageAsync(2, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        _agents.SetActiveAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<AgentDto>(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        _navigation.NavigateTo("/settings/agents?page=2");
        cut.WaitForAssertion(() => _agents.Received(1).ListPageAsync(2, 25, Arg.Any<CancellationToken>()));

        // The page-2 read is under way (page 1 is still on screen) when the answer to the write is lost.
        Row(cut, TestData.RaeAgentId).QuerySelector("button.ts-activate")!.Click();
        Writes().Count().ShouldBe(1);

        // That read finishes. It started before the lost answer, so it may not have seen the write, and the hold stays.
        gate.SetResult(TestData.Ok(new PagedResponse<AgentListItemDto>([TestData.AgentRow("Rae Quinn", TestData.RaeAgentId, active: false)], 2, 25, 26)));
        cut.WaitForAssertion(() => cut.FindAll("tr[data-agent]").Count.ShouldBe(1));
        Row(cut, TestData.RaeAgentId).QuerySelector("button.ts-activate")!.Click();

        Writes().Count().ShouldBe(1);
        cut.Find(".ts-conflict[role=alert]").TextContent.ShouldContain("The change to Rae Quinn may have gone through.");

        // A read that starts now is after the write: it releases the hold.
        _agents.ListPageAsync(2, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(new PagedResponse<AgentListItemDto>([TestData.AgentRow("Rae Quinn", TestData.RaeAgentId, active: false)], 2, 25, 26)));
        cut.Find(".ts-conflict button").Click();
        Row(cut, TestData.RaeAgentId).QuerySelector("button.ts-activate")!.Click();

        Writes().Count().ShouldBe(2);
    }

    [Fact]
    public void An_agent_who_is_already_gone_when_activating_says_so_and_reloads_the_list()
    {
        _agents.SetActiveAsync(Arg.Any<Guid>(), true, Arg.Any<CancellationToken>()).Returns(TestData.Fail<AgentDto>(ApiErrorCodes.AgentNotFound, "No such agent.", ResultErrorKind.NotFound));
```

`tests/TechStrap.Admin.Tests/Components/DeadLettersPageTests.cs`

Replace

```csharp
    }

    [Fact]
    public void After_a_lost_discard_answer_asking_to_discard_that_row_again_shows_the_uncertain_copy_and_sends_nothing()
    {
        _letters.DiscardAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
```

with

```csharp
    }

    [Fact]
    public void A_read_that_started_before_the_lost_retry_answer_does_not_release_the_hold()
    {
        var gate = new TaskCompletionSource<Result<PagedResponse<DeadLetterDto>>>();
        _letters.ListAsync(2, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        _letters.RetryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPage();
        _navigation.NavigateTo("/ops/dead-letters?page=2");
        cut.WaitForAssertion(() => _letters.Received(1).ListAsync(2, Arg.Any<int>(), Arg.Any<CancellationToken>()));

        Row(cut, _first).QuerySelector("button.ts-retry")!.Click();
        gate.SetResult(TestData.Ok(new PagedResponse<DeadLetterDto>([TestData.DeadLetter(id: _first)], 2, DeadLettersCopy.PageSize, 26)));
        cut.WaitForAssertion(() => cut.FindAll("tr[data-letter]").Count.ShouldBe(1));
        Row(cut, _first).QuerySelector("button.ts-retry")!.Click();

        Calls(nameof(IDeadLettersClient.RetryAsync)).ShouldBe(1);
        cut.Find(".ts-conflict[role=alert]").TextContent.ShouldContain("The retry may have been queued.");
    }

    [Fact]
    public void After_a_lost_discard_answer_asking_to_discard_that_row_again_shows_the_uncertain_copy_and_sends_nothing()
    {
        _letters.DiscardAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
```

`tests/TechStrap.Admin.Tests/Components/NotificationPreferencesPageTests.cs` (2 edits)

Replace

```csharp
    private static AngleSharp.Dom.IElement Toggle(IRenderedComponent<NotificationPreferencesPage> cut, string product) =>
        cut.FindAll(".ts-toggle-list li").Single(li => li.TextContent.Contains($"New tickets in {product}")).QuerySelector("input")!;

    private static int ListKey(IRenderedComponent<NotificationPreferencesPage> cut) =>
        (int)typeof(NotificationPreferencesPage).GetField("_version", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(cut.Instance)!;

    private static bool IsOn(AngleSharp.Dom.IElement toggle) => toggle.HasAttribute("checked");
```

with

```csharp
    private static AngleSharp.Dom.IElement Toggle(IRenderedComponent<NotificationPreferencesPage> cut, string product) =>
        cut.FindAll(".ts-toggle-list li").Single(li => li.TextContent.Contains($"New tickets in {product}")).QuerySelector("input")!;

    private static string? Revision(IRenderedComponent<NotificationPreferencesPage> cut) =>
        cut.FindAll(".ts-toggle-list li").Select(li => li.GetAttribute("data-revision")).Distinct().Single();

    private static bool IsOn(AngleSharp.Dom.IElement toggle) => toggle.HasAttribute("checked");
```

Replace

```csharp
        _agents.UpdateNotificationPreferencesAsync(Arg.Any<UpdateNotificationPreferencesRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = Render<NotificationPreferencesPage>();
        Toggle(cut, "Orbitly").Change(true);
        var before = ListKey(cut);

        Toggle(cut, "Acme").Change(true);

        // A new key makes Blazor replace the list items. bUnit's DOM keeps no tick of its own, so the key is the observable: without a new one the browser keeps a tick that was never saved.
        ListKey(cut).ShouldBeGreaterThan(before);
        Saves().Count.ShouldBe(1);
        gate.SetResult(TestData.Ok());
        cut.WaitForAssertion(() => cut.FindAll(".ts-toggle-list input").ShouldAllBe(i => !i.HasAttribute("disabled")));
```

with

```csharp
        _agents.UpdateNotificationPreferencesAsync(Arg.Any<UpdateNotificationPreferencesRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = Render<NotificationPreferencesPage>();
        Toggle(cut, "Orbitly").Change(true);
        var before = Revision(cut);

        Toggle(cut, "Acme").Change(true);

        // The list items are keyed by this revision, and the page shows it as data-revision (as the ticket sidebar does). A new one makes Blazor throw the items away and build new ones,
        // so the checkbox the agent just ticked is drawn again from what is saved and the browser cannot keep a tick that was never saved.
        Revision(cut).ShouldNotBe(before);
        Saves().Count.ShouldBe(1);
        gate.SetResult(TestData.Ok());
        cut.WaitForAssertion(() => cut.FindAll(".ts-toggle-list input").ShouldAllBe(i => !i.HasAttribute("disabled")));
```

`tests/TechStrap.Admin.Tests/Components/ProductEditorTests.cs`

Replace

```csharp
    }

    [Fact]
    public void An_https_logo_is_previewed_and_sent_trimmed()
    {
        var cut = RenderEdit();
```

with

```csharp
    }

    [Fact]
    public void A_product_saved_with_a_relative_logo_before_the_rule_can_be_renamed_with_the_logo_left_alone()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(version: 7, logo: "/images/old-logo.png")));
        var cut = RenderEdit();

        Value(cut, "ts-product-logo").ShouldBe("/images/old-logo.png");
        cut.Find("#ts-product-logo").Blur();
        FieldError(cut, "ts-product-logo").ShouldBeNull();
        Type(cut, "ts-product-name", "Orbitly Cloud");
        Save(cut);

        var sent = Updates.ShouldHaveSingleItem();
        sent.Name.ShouldBe("Orbitly Cloud");
        sent.Branding.LogoPath.ShouldBe("/images/old-logo.png");
        cut.FindAll("img").ShouldBeEmpty("a relative address is still never previewed");
    }

    [Fact]
    public void Changing_a_relative_logo_to_another_unsafe_address_is_refused_and_changing_it_back_is_accepted()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(version: 7, logo: "/images/old-logo.png")));
        var cut = RenderEdit();

        Type(cut, "ts-product-logo", "/images/other.png");
        cut.Find("#ts-product-logo").Blur();
        FieldError(cut, "ts-product-logo").ShouldBe("Use a full https:// address for the logo.");
        Save(cut);
        Updates.ShouldBeEmpty();

        Type(cut, "ts-product-logo", "  /images/old-logo.png ");
        cut.Find("#ts-product-logo").Blur();
        FieldError(cut, "ts-product-logo").ShouldBeNull();
        Save(cut);
        Updates.ShouldHaveSingleItem().Branding.LogoPath.ShouldBe("/images/old-logo.png");
    }

    [Fact]
    public void A_new_product_never_gets_the_exception_because_it_has_no_stored_logo()
    {
        var cut = RenderNew();

        Type(cut, "ts-product-logo", "/images/logo.png");
        cut.Find("#ts-product-logo").Blur();

        FieldError(cut, "ts-product-logo").ShouldBe("Use a full https:// address for the logo.");
    }

    [Fact]
    public void An_https_logo_is_previewed_and_sent_trimmed()
    {
        var cut = RenderEdit();
```

`tests/TechStrap.Admin.Tests/Components/PublicDisplayNameFieldTests.cs`

Replace

```csharp
        cut.Find("#ts-public-name").Blur();

        Saves().Count.ShouldBe(1);
    }

    [Fact]
```

with

```csharp
        cut.Find("#ts-public-name").Blur();

        Saves().Count.ShouldBe(1);
    }

    [Fact]
    public void Retyping_the_committed_name_after_an_uncertain_save_says_to_reload_instead_of_doing_nothing()
    {
        _savedName = "Sam";
        _agents.UpdateMyProfileAsync(Arg.Any<UpdateMyProfileRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderField();
        cut.Find("#ts-public-name").Input("Samantha");
        cut.Find("#ts-public-name").Blur();
        cut.Find("[role=alert]").TextContent.ShouldContain("The save may have gone through.");

        // The agent types the old name back to undo it. "Sam" may be what is stored, or "Samantha" may be: silence would hide that, and sending would repeat a write that may have landed.
        cut.Find("#ts-public-name").Input("Sam");
        cut.Find("#ts-public-name").Blur();

        cut.Find("[role=alert]").TextContent.ShouldBe("The save may have gone through. Reload the page to see what is saved before you try again.");
        Saves().Count.ShouldBe(1);
    }

    [Fact]
    public void Retyping_the_committed_name_when_nothing_is_uncertain_still_does_nothing()
    {
        _savedName = "Sam";
        var cut = RenderField();
        cut.Find("#ts-public-name").Input("Samantha");
        cut.Find("#ts-public-name").Input("Sam");

        cut.Find("#ts-public-name").Blur();

        Saves().ShouldBeEmpty();
        cut.FindAll("[role=alert]").ShouldBeEmpty();
    }

    [Fact]
```

`tests/TechStrap.Admin.Tests/Components/UncertainMarksTests.cs`

```csharp
using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// A write with an unknown outcome is held until a read that started after it has finished. A read that was already under way may have been answered before the write landed, so it must not release the hold.
/// </summary>
public sealed class UncertainMarksTests
{
    private static readonly Guid Row = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid Other = Guid.Parse("11111111-0000-0000-0000-000000000002");

    [Fact]
    public void A_row_is_held_from_the_moment_it_is_marked()
    {
        var marks = new UncertainMarks();
        marks.Contains(Row).ShouldBeFalse();

        marks.Add(Row, latestLoadId: 3);

        marks.Contains(Row).ShouldBeTrue();
        marks.Contains(Other).ShouldBeFalse();
    }

    [Theory]
    [InlineData(3, 3, true)]
    [InlineData(3, 2, true)]
    [InlineData(3, 4, false)]
    [InlineData(3, 9, false)]
    public void Only_a_read_that_started_after_the_mark_releases_it(int markedDuringLoad, int finishingLoad, bool stillHeld)
    {
        var marks = new UncertainMarks();
        marks.Add(Row, markedDuringLoad);

        marks.ReleaseForLoad(finishingLoad);

        marks.Contains(Row).ShouldBe(stillHeld);
    }

    [Fact]
    public void A_finishing_read_releases_the_older_marks_and_keeps_the_newer_ones()
    {
        var marks = new UncertainMarks();
        marks.Add(Row, 1);
        marks.Add(Other, 4);

        marks.ReleaseForLoad(3);

        marks.Contains(Row).ShouldBeFalse();
        marks.Contains(Other).ShouldBeTrue();
    }

    [Fact]
    public void Marking_a_row_again_takes_the_newer_load_id()
    {
        var marks = new UncertainMarks();
        marks.Add(Row, 1);
        marks.Add(Row, 5);

        marks.ReleaseForLoad(4);

        marks.Contains(Row).ShouldBeTrue();
    }

    [Fact]
    public void Clear_forgets_every_mark()
    {
        var marks = new UncertainMarks();
        marks.Add(Row, 1);
        marks.Add(Other, 2);

        marks.Clear();

        marks.Contains(Row).ShouldBeFalse();
        marks.Contains(Other).ShouldBeFalse();
    }
}
```

`tests/TechStrap.Api.Tests/OpenApiSecurityTests.cs`

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Api.Security;
using TechStrap.Api.Startup;

namespace TechStrap.Api.Tests;

/// <summary>
/// The OpenAPI document says how a caller proves who they are (PHASE-07c; PHASE-11 generates the SDK from it): a bearer token for agents, an API key for intake, a ticket token for customers, and
/// nothing for a public operation. The document is documentation only, so these tests read the served document, not the policies.
/// </summary>
public sealed partial class OpenApiSecurityTests
{
    [GeneratedRegex(@":[^}]+")]
    private static partial Regex RouteConstraint();

    private static async Task<JsonDocument> GetDocumentAsync(ApiFactory factory)
    {
        using var client = factory.CreateClient();
        return JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken));
    }

    private static string[] SecurityOf(JsonElement document, string path, string method)
    {
        var operation = document.GetProperty("paths").GetProperty(path).GetProperty(method);
        return operation.TryGetProperty("security", out var security)
            ? [.. security.EnumerateArray().SelectMany(requirement => requirement.EnumerateObject().Select(scheme => scheme.Name))]
            : [];
    }

    [Fact]
    public async Task The_document_declares_the_bearer_the_api_key_and_the_ticket_token_schemes()
    {
        await using var factory = new ApiFactory();
        using var document = await GetDocumentAsync(factory);

        var schemes = document.RootElement.GetProperty("components").GetProperty("securitySchemes");

        schemes.EnumerateObject().Select(scheme => scheme.Name).Order().ShouldBe(["ApiKey", "Bearer", "TicketToken"]);
        var bearer = schemes.GetProperty("Bearer");
        bearer.GetProperty("type").GetString().ShouldBe("http");
        bearer.GetProperty("scheme").GetString().ShouldBe("bearer");
        bearer.GetProperty("bearerFormat").GetString().ShouldBe("JWT");
        var apiKey = schemes.GetProperty("ApiKey");
        apiKey.GetProperty("type").GetString().ShouldBe("apiKey");
        apiKey.GetProperty("in").GetString().ShouldBe("header");
        apiKey.GetProperty("name").GetString().ShouldBe("X-Api-Key");
        var ticket = schemes.GetProperty("TicketToken");
        ticket.GetProperty("type").GetString().ShouldBe("apiKey");
        ticket.GetProperty("in").GetString().ShouldBe("header");
        ticket.GetProperty("name").GetString().ShouldBe("X-Ticket-Token");
    }

    [Theory]
    [InlineData("/api/agents/me", "get", "Bearer")]
    [InlineData("/api/tickets", "get", "Bearer")]
    [InlineData("/api/tickets/{id}/replies", "post", "Bearer")]
    [InlineData("/api/products", "post", "Bearer")]
    [InlineData("/api/products/{id}/api-keys", "post", "Bearer")]
    [InlineData("/api/attachments/{id}", "get", "Bearer")]
    [InlineData("/api/intake/tickets", "post", "ApiKey")]
    [InlineData("/api/customer/ticket", "get", "TicketToken")]
    [InlineData("/api/customer/ticket/replies", "post", "TicketToken")]
    [InlineData("/api/customer/attachments/{id}", "get", "TicketToken")]
    public async Task An_operation_names_the_one_scheme_its_policy_needs(string path, string method, string scheme)
    {
        await using var factory = new ApiFactory();
        using var document = await GetDocumentAsync(factory);

        SecurityOf(document.RootElement, path, method).ShouldBe([scheme]);
    }

    [Theory]
    [InlineData("/api/customer/access-link", "post")]
    [InlineData("/api/public/products/{productKey}", "get")]
    [InlineData("/api/public/products/{productKey}/tickets", "post")]
    public async Task A_public_operation_names_no_scheme(string path, string method)
    {
        await using var factory = new ApiFactory();
        using var document = await GetDocumentAsync(factory);

        SecurityOf(document.RootElement, path, method).ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_operation_behind_an_agent_admin_or_api_key_policy_carries_a_requirement()
    {
        await using var factory = new ApiFactory();
        using var document = await GetDocumentAsync(factory);
        var guarded = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            .Where(endpoint => endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(data =>
                data.Policy is AuthorizationPolicies.Agent or AuthorizationPolicies.Admin or AuthorizationPolicies.ApiKey))
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => (Path: "/" + RouteConstraint().Replace(endpoint.RoutePattern.RawText!, string.Empty), Method: method.ToLowerInvariant())))
            .Distinct()
            .ToList();

        guarded.Count.ShouldBeGreaterThanOrEqualTo(30);
        foreach (var (path, method) in guarded)
        {
            SecurityOf(document.RootElement, path, method).ShouldNotBeEmpty($"{method.ToUpperInvariant()} {path} is guarded but names no scheme");
        }
    }

    [Fact]
    public void Anonymous_beats_every_policy_and_a_ticket_token_header_alone_does_not_make_an_agent_route_a_customer_one()
    {
        OpenApiSecurity.SchemeFor([new AllowAnonymousAttribute(), new AuthorizeAttribute(AuthorizationPolicies.Agent)], takesTicketToken: true).ShouldBeNull();
        OpenApiSecurity.SchemeFor([new AuthorizeAttribute(AuthorizationPolicies.Admin)], takesTicketToken: true).ShouldBe(OpenApiSecurity.Bearer);
        OpenApiSecurity.SchemeFor([new AuthorizeAttribute(AuthorizationPolicies.Agent), new AuthorizeAttribute(AuthorizationPolicies.Admin)], takesTicketToken: false).ShouldBe(OpenApiSecurity.Bearer);
        OpenApiSecurity.SchemeFor([new AuthorizeAttribute(AuthorizationPolicies.ApiKey)], takesTicketToken: false).ShouldBe(OpenApiSecurity.ApiKey);
        OpenApiSecurity.SchemeFor([new AuthorizeAttribute(AuthorizationPolicies.Public)], takesTicketToken: true).ShouldBe(OpenApiSecurity.TicketToken);
        OpenApiSecurity.SchemeFor([new AuthorizeAttribute(AuthorizationPolicies.Public)], takesTicketToken: false).ShouldBeNull();
        OpenApiSecurity.SchemeFor([], takesTicketToken: false).ShouldBeNull();
    }
}
```

`tests/TechStrap.Api.Tests/SensitiveQuerySentryProcessorTests.cs`

```csharp
using Sentry;
using Sentry.Extensibility;
using Sentry.Internal;
using Sentry.Protocol.Envelopes;
using TechStrap.Hosting.Sentry;

namespace TechStrap.Api.Tests;

/// <summary>
/// Review Focus 4, Sentry: the text an agent searched for (a requester's email address, a subject line) never reaches Sentry in a request's query string or URL, in a breadcrumb, or in a span. The
/// last test sends an event through the SDK with the host's registration and reads what the transport receives, with a control that proves the harness would see a leak.
/// </summary>
/// <remarks>The last test initialises the SDK, which is process-wide state, so the class runs in the non-parallel <see cref="ProcessEnvironmentCollection"/> with the other tests that must run alone.</remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class SensitiveQuerySentryProcessorTests
{
    private const string Needle = "ada.lovelace%40orbitly.test";

    [Theory]
    [InlineData("search=ada%40x.test", "search=[redacted]")]
    [InlineData("?search=ada%40x.test&page=2", "?search=[redacted]&page=2")]
    [InlineData("?status=Open&search=ada%40x.test&page=2", "?status=Open&search=[redacted]&page=2")]
    [InlineData("?q=reset+password", "?q=[redacted]")]
    [InlineData("?q=a&search=b", "?q=[redacted]&search=[redacted]")]
    [InlineData("?SEARCH=Ada&Q=b", "?SEARCH=[redacted]&Q=[redacted]")]
    [InlineData("?search=", "?search=[redacted]")]
    [InlineData("https://admin.test/queue/mine?status=Open&search=ada@x.test&page=2#top", "https://admin.test/queue/mine?status=Open&search=[redacted]&page=2#top")]
    [InlineData("GET https://api.test/api/tickets?search=a%20b&pageSize=25 failed", "GET https://api.test/api/tickets?search=[redacted]&pageSize=25 failed")]
    public void The_value_of_search_and_q_is_masked_and_the_rest_of_the_address_is_kept(string text, string expected) =>
        SensitiveQuerySentryProcessor.Scrub(text).ShouldBe(expected);

    [Theory]
    [InlineData("?status=Open&page=2")]
    [InlineData("?research=1&faq=2&query=3&squash=4")]
    [InlineData("/queue/search")]
    [InlineData("search")]
    [InlineData("a message that says search=nothing but is not a query")]
    [InlineData("")]
    public void Anything_that_is_not_a_search_parameter_is_left_alone(string text)
    {
        // The last-but-one input does contain "search=" but not as a parameter: it follows a space, so it is not a start of a query string, a "?" or an "&".
        SensitiveQuerySentryProcessor.Scrub(text).ShouldBe(text);
    }

    [Fact]
    public void Nothing_stays_nothing()
    {
        SensitiveQuerySentryProcessor.Scrub(null).ShouldBeNull();
    }

    [Fact]
    public void An_event_loses_the_search_from_its_query_string_and_url_and_keeps_everything_else()
    {
        var sentryEvent = new SentryEvent();
        sentryEvent.Request.QueryString = $"?status=Open&search={Needle}";
        sentryEvent.Request.Url = $"https://admin.test/queue/mine?status=Open&search={Needle}&page=2";
        sentryEvent.Request.Method = "GET";
        sentryEvent.Request.Headers["User-Agent"] = "ua";

        var result = new SensitiveQuerySentryProcessor().Process(sentryEvent);

        result.ShouldNotBeNull();
        result.Request.QueryString.ShouldBe("?status=Open&search=[redacted]");
        result.Request.Url.ShouldBe("https://admin.test/queue/mine?status=Open&search=[redacted]&page=2");
        result.Request.Method.ShouldBe("GET");
        result.Request.Headers["User-Agent"].ShouldBe("ua");
    }

    [Fact]
    public void An_event_with_no_request_is_untouched()
    {
        var result = new SensitiveQuerySentryProcessor().Process(new SentryEvent { Message = "boom" });

        result.ShouldNotBeNull();
        result.Request.QueryString.ShouldBeNull();
        result.Request.Url.ShouldBeNull();
    }

    [Fact]
    public void A_transaction_loses_the_search_from_its_request_and_from_every_span()
    {
        var tracer = new TransactionTracer(DisabledHub.Instance, new TransactionContext("GET /queue/{view}", "http.server", null, null, null, "", null, null, true, TransactionNameSource.Route));
        tracer.Request.QueryString = $"search={Needle}";
        tracer.Request.Url = $"https://admin.test/queue/mine?search={Needle}";
        var span = tracer.StartChild("http.client", $"GET https://api.test/api/tickets?search={Needle}&pageSize=25");
        span.SetData("http.query", $"?search={Needle}&pageSize=25");
        span.SetData("http.response.status_code", 200);
        span.Finish();
        var transaction = new SentryTransaction(tracer);

        var result = new SensitiveQuerySentryProcessor().Process(transaction);

        result.ShouldNotBeNull();
        result.Request.QueryString.ShouldBe("search=[redacted]");
        result.Request.Url.ShouldBe("https://admin.test/queue/mine?search=[redacted]");
        var scrubbed = result.Spans.Single();
        scrubbed.Description.ShouldBe("GET https://api.test/api/tickets?search=[redacted]&pageSize=25");
        scrubbed.Data["http.query"].ShouldBe("?search=[redacted]&pageSize=25");
        scrubbed.Data["http.response.status_code"].ShouldBe(200);
    }

    [Fact]
    public void A_breadcrumb_that_carries_a_search_is_replaced_by_a_masked_copy_and_any_other_is_kept_as_it_is()
    {
        var dirty = new Breadcrumb(
            $"GET /api/tickets?search={Needle}", "http",
            new Dictionary<string, string> { ["url"] = $"https://api.test/api/tickets?search={Needle}&page=1", ["http.query"] = $"?search={Needle}&page=1", ["method"] = "GET" },
            "http", BreadcrumbLevel.Info);
        var clean = new Breadcrumb("GET /api/agents/me", "http", new Dictionary<string, string> { ["url"] = "https://api.test/api/agents/me" }, "http", BreadcrumbLevel.Info);
        var bare = new Breadcrumb("a log line", "default");

        var masked = SensitiveQuerySentryProcessor.ScrubBreadcrumb(dirty, new SentryHint());

        masked.ShouldNotBeNull();
        masked.Message.ShouldBe("GET /api/tickets?search=[redacted]");
        masked.Data!["url"].ShouldBe("https://api.test/api/tickets?search=[redacted]&page=1");
        masked.Data["http.query"].ShouldBe("?search=[redacted]&page=1");
        masked.Data["method"].ShouldBe("GET");
        masked.Type.ShouldBe("http");
        masked.Category.ShouldBe("http");
        masked.Level.ShouldBe(BreadcrumbLevel.Info);
        SensitiveQuerySentryProcessor.ScrubBreadcrumb(clean, new SentryHint()).ShouldBeSameAs(clean);
        SensitiveQuerySentryProcessor.ScrubBreadcrumb(bare, new SentryHint()).ShouldBeSameAs(bare);
    }

    [Fact]
    public void The_host_registration_masks_the_search_in_an_event_and_a_breadcrumb_that_reach_the_transport_and_without_it_the_harness_sees_the_leak()
    {
        var withoutScrubbing = Send(register: false);
        var withScrubbing = Send(register: true);

        // The control: a registration that scrubs nothing delivers the needle in all three places, so this harness would catch a leak.
        withoutScrubbing.Split(Needle).Length.ShouldBeGreaterThanOrEqualTo(4);
        withScrubbing.ShouldNotContain(Needle);
        withScrubbing.ShouldContain("search=[redacted]");
        withScrubbing.ShouldContain("status=Open");
    }

    private static string Send(bool register)
    {
        var transport = new CapturingTransport();
        var options = new SentryOptions
        {
            Dsn = "https://key@sentry.example.test/1",
            Transport = transport,
            AutoSessionTracking = false,
            CacheDirectoryPath = null,
        };
        if (register)
        {
            options.AddSensitiveHeaderScrubbing();
        }

        using var sdk = SentrySdk.Init(options);
        SentrySdk.AddBreadcrumb(
            $"GET /api/tickets?search={Needle}", "http", "http", new Dictionary<string, string> { ["url"] = $"https://api.test/api/tickets?search={Needle}" }, BreadcrumbLevel.Info);
        var sentryEvent = new SentryEvent { Message = "boom" };
        sentryEvent.Request.QueryString = $"?status=Open&search={Needle}";
        sentryEvent.Request.Url = $"https://admin.test/queue/mine?status=Open&search={Needle}";
        SentrySdk.CaptureEvent(sentryEvent);
        SentrySdk.FlushAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();

        return transport.Payloads.Single();
    }

    private sealed class CapturingTransport : ITransport
    {
        public List<string> Payloads { get; } = [];

        public async Task SendEnvelopeAsync(Envelope envelope, CancellationToken cancellationToken = default)
        {
            using var stream = new MemoryStream();
            await envelope.SerializeAsync(stream, null, cancellationToken);
            Payloads.Add(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
        }
    }
}
```

`tests/TechStrap.Api.Tests/SentryOptionsExtensionsTests.cs`

Replace

```csharp
    }

    [Fact]
    public void Scrubbing_needs_options()
    {
        Should.Throw<ArgumentNullException>(() => SentryOptionsExtensions.AddSensitiveHeaderScrubbing(null!));
```

with

```csharp
    }

    [Fact]
    public void Scrubbing_also_registers_the_search_processor_for_events_and_for_transactions()
    {
        var options = new SentryOptions();

        options.AddSensitiveHeaderScrubbing();

        options.GetAllEventProcessors().OfType<SensitiveQuerySentryProcessor>().ShouldHaveSingleItem();
        options.GetAllTransactionProcessors().OfType<SensitiveQuerySentryProcessor>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Scrubbing_needs_options()
    {
        Should.Throw<ArgumentNullException>(() => SentryOptionsExtensions.AddSensitiveHeaderScrubbing(null!));
```

`tests/TechStrap.Application.Tests/Products/UpdateProductRequestHandlerTests.cs`

Replace

```csharp
        _products.DidNotReceive().Update(Arg.Any<Product>());
    }

    [Fact]
    public async Task An_unknown_product_is_not_found()
    {
```

with

```csharp
        _products.DidNotReceive().Update(Arg.Any<Product>());
    }

    private void StoreRelativeLogo() =>
        _products.GetByIdAsync(_product.Id, Arg.Any<CancellationToken>()).Returns(Product.Restore(
            _product.Id, "orbitly", "Orbitly", "ORB", ProductBranding.Restore("Orbitly", "/images/old-logo.png", "#1F6FEB", null, null), isActive: true, version: 7));

    [Fact]
    public async Task A_product_with_a_relative_logo_from_before_the_rule_can_be_renamed_with_the_logo_left_as_it_is()
    {
        StoreRelativeLogo();
        var request = new UpdateProductRequest("Orbitly Cloud", new ProductBrandingRequest("Orbitly", "/images/old-logo.png", "#1F6FEB", null, null), true, 7);

        var result = await Handler().HandleAsync(_product.Id, request, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Orbitly Cloud");
        result.Value.Branding.LogoPath.ShouldBe("/images/old-logo.png");
        _products.Received(1).Update(Arg.Any<Product>());
        _events.Received(1).Add(Arg.Is<AdminEvent>(e => e.PayloadJson == "{\"changed\":[\"name\"]}"));
    }

    [Fact]
    public async Task Changing_the_relative_logo_to_another_unsafe_address_is_still_a_field_error()
    {
        StoreRelativeLogo();
        var request = new UpdateProductRequest("Orbitly", new ProductBrandingRequest("Orbitly", "/images/other.png", "#1F6FEB", null, null), true, 7);

        var result = await Handler().HandleAsync(_product.Id, request, TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Target.ShouldBe("logo-path"),
            error => error.Code.ShouldBe("logo-path-invalid"));
        _products.DidNotReceive().Update(Arg.Any<Product>());
    }

    [Fact]
    public async Task A_stored_product_with_a_good_logo_still_refuses_a_relative_one()
    {
        var request = new UpdateProductRequest("Orbitly", new ProductBrandingRequest("Orbitly", "/images/logo.png", "#1F6FEB", null, null), true, 7);

        var result = await Handler().HandleAsync(_product.Id, request, TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("logo-path-invalid");
    }

    [Fact]
    public async Task An_unknown_product_is_not_found()
    {
```

`tests/TechStrap.Domain.Tests/Products/ProductBrandingLogoTests.cs`

Replace

```csharp
        {
            stored.ShouldStartWith("https://");
        }
    }

    [Theory]
```

with

```csharp
        {
            stored.ShouldStartWith("https://");
        }
    }

    private static ProductBranding StoredWith(string? logo) => ProductBranding.Restore("Orbitly", logo, "#1F6FEB", null, null);

    [Theory]
    [InlineData("/images/old-logo.png")]
    [InlineData("//cdn.orbitly.example/logo.png")]
    [InlineData("http://cdn.orbitly.example/logo.png")]
    public void An_update_that_leaves_a_stored_logo_unchanged_accepts_it_even_when_the_rule_would_refuse_it(string stored)
    {
        var result = ProductBranding.CreateForUpdate(StoredWith(stored), "Orbitly Cloud", stored, "#7C3AED", null, null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.LogoPath.ShouldBe(stored);
        result.Value.DisplayName.ShouldBe("Orbitly Cloud");
        result.Value.AccentColour.ShouldBe("#7C3AED");
    }

    [Fact]
    public void The_unchanged_logo_is_compared_after_trimming()
    {
        var result = ProductBranding.CreateForUpdate(StoredWith("/images/old-logo.png"), "Orbitly", "  /images/old-logo.png  ", null, null, null);

        result.Value.LogoPath.ShouldBe("/images/old-logo.png");
    }

    [Theory]
    [InlineData("/images/other.png")]
    [InlineData("/IMAGES/OLD-LOGO.PNG")]
    [InlineData("javascript:alert(1)")]
    public void A_different_logo_is_checked_by_the_full_rule_even_when_the_product_has_a_stored_one(string requested)
    {
        var result = ProductBranding.CreateForUpdate(StoredWith("/images/old-logo.png"), "Orbitly", requested, null, null, null);

        result.Error!.Code.ShouldBe("logo-path-invalid");
    }

    [Fact]
    public void A_product_with_no_stored_logo_gets_no_exception()
    {
        ProductBranding.CreateForUpdate(StoredWith(null), "Orbitly", "/images/logo.png", null, null, null).Error!.Code.ShouldBe("logo-path-invalid");
        ProductBranding.CreateForUpdate(StoredWith(null), "Orbitly", null, null, null, null).Value.LogoPath.ShouldBeNull();
        ProductBranding.CreateForUpdate(StoredWith(null), "Orbitly", "https://cdn.orbitly.example/logo.png", null, null, null).Value.LogoPath.ShouldBe("https://cdn.orbitly.example/logo.png");
    }

    [Fact]
    public void Clearing_a_stored_relative_logo_is_accepted_and_every_other_field_is_still_checked_when_the_logo_is_left_alone()
    {
        ProductBranding.CreateForUpdate(StoredWith("/images/old-logo.png"), "Orbitly", null, null, null, null).Value.LogoPath.ShouldBeNull();
        ProductBranding.CreateForUpdate(StoredWith("/images/old-logo.png"), "Orbitly", "/images/old-logo.png", "purple", null, null).Error!.Code.ShouldBe("accent-colour-invalid");
        ProductBranding.CreateForUpdate(StoredWith("/images/old-logo.png"), "  ", "/images/old-logo.png", null, null, null).Error!.Code.ShouldBe("display-name-required");
    }

    [Theory]
```

- [ ] **Step 2: Run them and watch them fail**

```bash
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-class "*SensitiveQuerySentryProcessorTests" --filter-class "*OpenApiSecurityTests" --filter-class "*SentryOptionsExtensionsTests"
dotnet test --project tests/TechStrap.Domain.Tests -c Release --filter-class "*ProductBrandingLogoTests"
dotnet test --project tests/TechStrap.Application.Tests -c Release --filter-class "*UpdateProductRequestHandlerTests"
dotnet test --project tests/TechStrap.Admin.Tests -c Release
```

Expected: each project fails to build (`SensitiveQuerySentryProcessor`, `OpenApiSecurity`, `ProductBranding.CreateForUpdate`, `UncertainMarks` and `ProductEditorViewModel.OriginalLogoPath` do not exist).

- [ ] **Step 3: Implement**

The Sentry processor and its registration, the OpenAPI transformers and the one line in `Program.cs`, `UncertainMarks` and the four pages, then the audit, display-name, notification and logo changes.

`src/TechStrap.Admin/Features/Account/NotificationPreferencesPage.razor`

Replace

```razor
            <ul class="ts-toggle-list">
                @foreach (var pref in _prefs)
                {
                    <li @key="(pref.ProductId, _version)" class="form-check">
                        <input id="@($"ts-alert-{pref.ProductId}")" class="form-check-input" type="checkbox" checked="@pref.NotifyNewTicket" disabled="@(_saving || _saveUncertain)"
                               @onchange="e => ToggleAsync(pref, e.Value is true)" />
                        <label class="form-check-label" for="@($"ts-alert-{pref.ProductId}")">@MySettingsCopy.AlertLabel(pref.ProductName)</label>
```

with

```razor
            <ul class="ts-toggle-list">
                @foreach (var pref in _prefs)
                {
                    <li @key="(pref.ProductId, _version)" data-revision="@_version" class="form-check">
                        <input id="@($"ts-alert-{pref.ProductId}")" class="form-check-input" type="checkbox" checked="@pref.NotifyNewTicket" disabled="@(_saving || _saveUncertain)"
                               @onchange="e => ToggleAsync(pref, e.Value is true)" />
                        <label class="form-check-label" for="@($"ts-alert-{pref.ProductId}")">@MySettingsCopy.AlertLabel(pref.ProductName)</label>
```

`src/TechStrap.Admin/Features/Account/PublicDisplayNameField.razor.cs`

Replace

```csharp
        }

        _error = _model.Check();
        if (_error is not null || (_model.Normalized ?? string.Empty) == _committed)
        {
            return;
        }

        if (_uncertainValue is not null && (_model.Normalized ?? string.Empty) == _uncertainValue)
        {
            _error = MySettingsCopy.NameUncertain;
            return;
        }
```

with

```csharp
        }

        _error = _model.Check();
        var typed = _model.Normalized ?? string.Empty;
        if (_error is not null)
        {
            return;
        }

        // After a save with an unknown outcome the stored name is either the committed one or the uncertain one. Retyping either of them cannot be answered by doing nothing (the field would
        // show a name the API may not hold) or by sending it again (it may already be stored): the agent is told to reload.
        if (_uncertainValue is not null && (typed == _uncertainValue || typed == _committed))
        {
            _error = MySettingsCopy.NameUncertain;
            return;
        }

        if (typed == _committed)
        {
            return;
        }
```

`src/TechStrap.Admin/Features/Ops/DeadLetters/DeadLettersContent.razor.cs` (4 edits)

Replace

```csharp
    private readonly CancellationTokenSource _lifetime = new();

    // Emails whose retry or discard ended with an unknown outcome: held (asking again shows the uncertain copy and sends nothing) until the list has been read again successfully.
    private readonly HashSet<Guid> _uncertainIds = [];
    private IReadOnlyList<DeadLetterRowViewModel> _rows = [];
    private DeadLetterRowViewModel? _discarding;
    private string? _error;
```

with

```csharp
    private readonly CancellationTokenSource _lifetime = new();

    // Emails whose retry or discard ended with an unknown outcome: held (asking again shows the uncertain copy and sends nothing) until the list has been read again successfully.
    private readonly UncertainMarks _uncertainIds = new();
    private IReadOnlyList<DeadLetterRowViewModel> _rows = [];
    private DeadLetterRowViewModel? _discarding;
    private string? _error;
```

Replace

```csharp
            {
                _rows = [.. result.Value.Items.Select(DeadLetterRowViewModel.From)];
                _total = result.Value.TotalCount;
                _uncertainIds.Clear();

                // The list already holds the total, so the badge in the navigation follows it without a second call.
                Failed.Set(_total);
```

with

```csharp
            {
                _rows = [.. result.Value.Items.Select(DeadLetterRowViewModel.From)];
                _total = result.Value.TotalCount;
                _uncertainIds.ReleaseForLoad(loadId);

                // The list already holds the total, so the badge in the navigation follows it without a second call.
                Failed.Set(_total);
```

Replace

```csharp
        {
            _dialogError = DeadLettersCopy.DiscardUncertain;
            _uncertain = true;
            _uncertainIds.Add(id);
        }
        else
        {
```

with

```csharp
        {
            _dialogError = DeadLettersCopy.DiscardUncertain;
            _uncertain = true;
            _uncertainIds.Add(id, _loadId);
        }
        else
        {
```

Replace

```csharp
        {
            _rowError = DeadLettersCopy.RetryUncertain;
            _rowUncertain = true;
            _uncertainIds.Add(id);
        }
        else
        {
```

with

```csharp
        {
            _rowError = DeadLettersCopy.RetryUncertain;
            _rowUncertain = true;
            _uncertainIds.Add(id, _loadId);
        }
        else
        {
```

`src/TechStrap.Admin/Features/Settings/Agents/AgentsContent.razor.cs` (4 edits)

Replace

```csharp
    private readonly CancellationTokenSource _lifetime = new();

    // Agents whose activate or deactivate ended with an unknown outcome: held (asking again shows the uncertain copy and sends nothing) until the list has been read again successfully.
    private readonly HashSet<Guid> _uncertainIds = [];
    private IReadOnlyList<AgentRowViewModel> _rows = [];
    private AgentRowViewModel? _deactivating;
    private string? _error;
```

with

```csharp
    private readonly CancellationTokenSource _lifetime = new();

    // Agents whose activate or deactivate ended with an unknown outcome: held (asking again shows the uncertain copy and sends nothing) until the list has been read again successfully.
    private readonly UncertainMarks _uncertainIds = new();
    private IReadOnlyList<AgentRowViewModel> _rows = [];
    private AgentRowViewModel? _deactivating;
    private string? _error;
```

Replace

```csharp
            {
                _rows = [.. result.Value.Items.Select(AgentRowViewModel.From)];
                _total = result.Value.TotalCount;
                _uncertainIds.Clear();

                // A page past the end (an old address, or the last row of the last page went away): go to the last page that has rows instead of saying there are none.
                var lastPage = Math.Max(1, (_total + AgentsCopy.PageSize - 1) / AgentsCopy.PageSize);
```

with

```csharp
            {
                _rows = [.. result.Value.Items.Select(AgentRowViewModel.From)];
                _total = result.Value.TotalCount;
                _uncertainIds.ReleaseForLoad(loadId);

                // A page past the end (an old address, or the last row of the last page went away): go to the last page that has rows instead of saying there are none.
                var lastPage = Math.Max(1, (_total + AgentsCopy.PageSize - 1) / AgentsCopy.PageSize);
```

Replace

```csharp
            {
                _rowError = AgentsCopy.ActivateUncertain(row.DisplayName);
                _rowUncertain = true;
                _uncertainIds.Add(row.Id);
            }
            else
            {
```

with

```csharp
            {
                _rowError = AgentsCopy.ActivateUncertain(row.DisplayName);
                _rowUncertain = true;
                _uncertainIds.Add(row.Id, _loadId);
            }
            else
            {
```

Replace

```csharp
        {
            _dialogError = AgentsCopy.DeactivateUncertain;
            _uncertain = true;
            _uncertainIds.Add(id);
        }
        else if (error.Code == ApiErrorCodes.LastActiveAdmin)
        {
```

with

```csharp
        {
            _dialogError = AgentsCopy.DeactivateUncertain;
            _uncertain = true;
            _uncertainIds.Add(id, _loadId);
        }
        else if (error.Code == ApiErrorCodes.LastActiveAdmin)
        {
```

`src/TechStrap.Admin/Features/Settings/Audit/AdminEventsContent.razor.cs`

Replace

```csharp
        _actor = Guid.TryParse(Actor, out var actor) ? actor : null;
        _page = int.TryParse(PageNumber, NumberStyles.None, CultureInfo.InvariantCulture, out var page) && page > 1 ? page : 1;

        // Both reads start now and are awaited together: the events never wait for the names of the filter. A failed agent list only means the filter has no names; the stale-load guard in LoadAsync still applies.
        var agents = Task.CompletedTask;
        if (!_agentsLoaded)
        {
            _agentsLoaded = true;
            agents = LoadAgentsAsync();
        }

        var events = Task.CompletedTask;
        var key = (_subject, _actor, _page);
        if (_loadedFor != key)
        {
            _loadedFor = key;
            events = LoadAsync();
        }

        await Task.WhenAll(agents, events);
    }

    // The filter's actor list. If it cannot be read the filter simply has no names to offer; the log itself still works.
    private async Task LoadAgentsAsync()
    {
        var result = await AgentsClient.ListAllAsync(_lifetime.Token);
        if (!_lifetime.IsCancellationRequested && result.IsSuccess)
        {
            _agents = [.. result.Value.OrderBy(a => a.Name ?? a.DisplayLabel, StringComparer.OrdinalIgnoreCase)];
        }
    }
```

with

```csharp
        _actor = Guid.TryParse(Actor, out var actor) ? actor : null;
        _page = int.TryParse(PageNumber, NumberStyles.None, CultureInfo.InvariantCulture, out var page) && page > 1 ? page : 1;

        // Both reads start now, but only the events are awaited: they render the moment they arrive and never wait for the names of the filter, which draw when their own read finishes.
        if (!_agentsLoaded)
        {
            _agentsLoaded = true;
            _ = LoadAgentsAsync();
        }

        var key = (_subject, _actor, _page);
        if (_loadedFor != key)
        {
            _loadedFor = key;
            await LoadAsync();
        }
    }

    // The filter's actor list, read in the background. If it cannot be read the filter simply has no names to offer; the log itself still works.
    private async Task LoadAgentsAsync()
    {
        try
        {
            var result = await AgentsClient.ListAllAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested || !result.IsSuccess)
            {
                return;
            }

            _agents = [.. result.Value.OrderBy(a => a.Name ?? a.DisplayLabel, StringComparer.OrdinalIgnoreCase)];
            await InvokeAsync(StateHasChanged);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The page went away while the names were on their way.
        }
    }
```

`src/TechStrap.Admin/Features/Settings/Products/ApiKeysPanel.razor.cs` (3 edits)

Replace

```csharp
    private readonly CancellationTokenSource _lifetime = new();

    // Keys whose revoke ended with an unknown outcome: held (asking again shows the uncertain copy and sends nothing) until the list has been read again successfully.
    private readonly HashSet<Guid> _uncertainRevokes = [];
    private NewApiKeyDialog? _dialog;
    private IReadOnlyList<ApiKeyRowViewModel> _rows = [];
    private ApiKeyRowViewModel? _revoking;
```

with

```csharp
    private readonly CancellationTokenSource _lifetime = new();

    // Keys whose revoke ended with an unknown outcome: held (asking again shows the uncertain copy and sends nothing) until the list has been read again successfully.
    private readonly UncertainMarks _uncertainRevokes = new();
    private NewApiKeyDialog? _dialog;
    private IReadOnlyList<ApiKeyRowViewModel> _rows = [];
    private ApiKeyRowViewModel? _revoking;
```

Replace

```csharp
            if (result.IsSuccess)
            {
                _rows = [.. result.Value.Select(ApiKeyRowViewModel.From).OrderByDescending(r => r.CreatedAt)];
                _uncertainRevokes.Clear();
                return true;
            }
```

with

```csharp
            if (result.IsSuccess)
            {
                _rows = [.. result.Value.Select(ApiKeyRowViewModel.From).OrderByDescending(r => r.CreatedAt)];
                _uncertainRevokes.ReleaseForLoad(loadId);
                return true;
            }
```

Replace

```csharp
            // The revoke may have been applied before the answer was lost: never a bare "try again".
            _revokeError = ApiKeysCopy.RevokeUncertain;
            _revokeUncertain = true;
            _uncertainRevokes.Add(keyId);
        }
        else
        {
```

with

```csharp
            // The revoke may have been applied before the answer was lost: never a bare "try again".
            _revokeError = ApiKeysCopy.RevokeUncertain;
            _revokeUncertain = true;
            _uncertainRevokes.Add(keyId, _loadId);
        }
        else
        {
```

`src/TechStrap.Admin/Features/Settings/Products/ProductEditorViewModel.cs` (4 edits)

Replace

```csharp

    public string LogoPath { get; set; } = string.Empty;

    public string AccentColour { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;
```

with

```csharp

    public string LogoPath { get; set; } = string.Empty;

    /// <summary>The logo address the product was loaded with. An address saved before the logo rule (a relative path) is accepted again while the field still holds it, as the API accepts it.</summary>
    public string OriginalLogoPath { get; set; } = string.Empty;

    public string AccentColour { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;
```

Replace

```csharp
        NumberPrefix = product.NumberPrefix,
        DisplayName = product.Branding.DisplayName,
        LogoPath = product.Branding.LogoPath ?? string.Empty,
        AccentColour = product.Branding.AccentColour,
        FromAddress = product.Branding.FromAddress ?? string.Empty,
        ReplyTo = product.Branding.ReplyTo ?? string.Empty,
```

with

```csharp
        NumberPrefix = product.NumberPrefix,
        DisplayName = product.Branding.DisplayName,
        LogoPath = product.Branding.LogoPath ?? string.Empty,
        OriginalLogoPath = product.Branding.LogoPath ?? string.Empty,
        AccentColour = product.Branding.AccentColour,
        FromAddress = product.Branding.FromAddress ?? string.Empty,
        ReplyTo = product.Branding.ReplyTo ?? string.Empty,
```

Replace

```csharp
    private ProductBrandingRequest ToBranding() =>
        new(DisplayName.Trim(), Blank(LogoPath), Blank(AccentColour), Blank(FromAddress), Blank(ReplyTo));

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The message for one field, or null when it is fine. Key and ticket number prefix are checked only when the product is being created.</summary>
```

with

```csharp
    private ProductBrandingRequest ToBranding() =>
        new(DisplayName.Trim(), Blank(LogoPath), Blank(AccentColour), Blank(FromAddress), Blank(ReplyTo));

    private bool LogoUnchanged => OriginalLogoPath.Length > 0 && string.Equals(LogoPath.Trim(), OriginalLogoPath, StringComparison.Ordinal);

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The message for one field, or null when it is fine. Key and ticket number prefix are checked only when the product is being created.</summary>
```

Replace

```csharp
        ApiFields.NumberPrefix when creating => NumberPrefixPattern().IsMatch(NumberPrefix.Trim()) ? null : ProductsCopy.NumberPrefixInvalid,
        ApiFields.DisplayName => string.IsNullOrWhiteSpace(DisplayName) ? ProductsCopy.DisplayNameRequired
            : DisplayName.Trim().Length > ProductFields.DisplayNameMaxLength ? ProductsCopy.NameTooLong : null,
        ApiFields.LogoPath => BrandingRules.IsAcceptableLogoUrl(LogoPath) ? null : ProductsCopy.LogoInvalid,
        ApiFields.AccentColour => string.IsNullOrWhiteSpace(AccentColour) || Regex.IsMatch(AccentColour.Trim(), BrandingRules.ColourPattern) ? null : ProductsCopy.AccentInvalid,
        ApiFields.FromAddress => IsEmailOrBlank(FromAddress) ? null : ProductsCopy.EmailInvalid,
        ApiFields.ReplyTo => IsEmailOrBlank(ReplyTo) ? null : ProductsCopy.EmailInvalid,
```

with

```csharp
        ApiFields.NumberPrefix when creating => NumberPrefixPattern().IsMatch(NumberPrefix.Trim()) ? null : ProductsCopy.NumberPrefixInvalid,
        ApiFields.DisplayName => string.IsNullOrWhiteSpace(DisplayName) ? ProductsCopy.DisplayNameRequired
            : DisplayName.Trim().Length > ProductFields.DisplayNameMaxLength ? ProductsCopy.NameTooLong : null,
        ApiFields.LogoPath => BrandingRules.IsAcceptableLogoUrl(LogoPath) || LogoUnchanged ? null : ProductsCopy.LogoInvalid,
        ApiFields.AccentColour => string.IsNullOrWhiteSpace(AccentColour) || Regex.IsMatch(AccentColour.Trim(), BrandingRules.ColourPattern) ? null : ProductsCopy.AccentInvalid,
        ApiFields.FromAddress => IsEmailOrBlank(FromAddress) ? null : ProductsCopy.EmailInvalid,
        ApiFields.ReplyTo => IsEmailOrBlank(ReplyTo) ? null : ProductsCopy.EmailInvalid,
```

`src/TechStrap.Admin/Features/Settings/Tags/TagsContent.razor.cs` (3 edits)

Replace

```csharp
    private readonly Dictionary<string, string> _editErrors = [];

    // Tags whose delete ended with an unknown outcome: held (a second ask shows the uncertain copy and sends nothing) until the list has been read again successfully.
    private readonly HashSet<Guid> _uncertainDeletes = [];
    private IReadOnlyList<TagRowViewModel> _rows = [];
    private Guid _deletingId;
    private Guid _editing;
```

with

```csharp
    private readonly Dictionary<string, string> _editErrors = [];

    // Tags whose delete ended with an unknown outcome: held (a second ask shows the uncertain copy and sends nothing) until the list has been read again successfully.
    private readonly UncertainMarks _uncertainDeletes = new();
    private IReadOnlyList<TagRowViewModel> _rows = [];
    private Guid _deletingId;
    private Guid _editing;
```

Replace

```csharp
            if (result.IsSuccess)
            {
                _rows = Sorted(result.Value.Select(TagRowViewModel.From));
                _uncertainDeletes.Clear();
                return true;
            }
```

with

```csharp
            if (result.IsSuccess)
            {
                _rows = Sorted(result.Value.Select(TagRowViewModel.From));
                _uncertainDeletes.ReleaseForLoad(loadId);
                return true;
            }
```

Replace

```csharp
        {
            _deleteError = TagsCopy.DeleteUncertain;
            _deleteUncertain = true;
            _uncertainDeletes.Add(tagId);
        }
        else
        {
```

with

```csharp
        {
            _deleteError = TagsCopy.DeleteUncertain;
            _deleteUncertain = true;
            _uncertainDeletes.Add(tagId, _loadId);
        }
        else
        {
```

`src/TechStrap.Admin/Features/Shell/UncertainMarks.cs`

```csharp
namespace TechStrap.Admin.Features.Shell;

/// <summary>
/// The writes whose outcome is unknown, held per row until the list has been read again (the rule of every admin page: a lost answer may still have been applied, so asking again sends nothing).
/// A mark is released by a read that started after the mark was made, and only by one. A read that was already under way when the write was sent may have been answered before the write
/// landed, so its rows can still show the old state; if it released the mark, the agent could send the write a second time. Each mark therefore carries the id of the latest read that had started
/// when it was made (<c>_loadId</c> on the page), and a finishing read with a higher id releases it. Not thread-safe, because a page touches it only on the renderer's thread.
/// </summary>
public sealed class UncertainMarks
{
    private readonly Dictionary<Guid, int> _marks = [];

    /// <summary>True while a write on this row has an unknown outcome and no later read has finished.</summary>
    public bool Contains(Guid id) => _marks.ContainsKey(id);

    /// <summary>Holds the row. <paramref name="latestLoadId"/> is the id of the latest read that has started so far (the page's own counter).</summary>
    public void Add(Guid id, int latestLoadId) => _marks[id] = latestLoadId;

    /// <summary>Called when the read with id <paramref name="loadId"/> has been applied: releases every mark made before that read started.</summary>
    public void ReleaseForLoad(int loadId)
    {
        foreach (var id in _marks.Where(mark => mark.Value < loadId).Select(mark => mark.Key).ToList())
        {
            _marks.Remove(id);
        }
    }

    /// <summary>Forgets every mark: the page now shows something else (another product), so the rows the marks were about are gone.</summary>
    public void Clear() => _marks.Clear();
}
```

`src/TechStrap.Api/Program.cs`

Replace

```csharp
builder.Services.AddSecurityHeaders(builder.Configuration);
builder.Services.AddProblemDetailsExceptionHandling();
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddTechStrapPersistence();
builder.Services.AddTechStrapSecurity();
builder.Services.AddTechStrapDevelopmentSeeding();
```

with

```csharp
builder.Services.AddSecurityHeaders(builder.Configuration);
builder.Services.AddProblemDetailsExceptionHandling();
builder.Services.AddControllers();
builder.Services.AddOpenApi(options => options.AddTechStrapSecuritySchemes());
builder.Services.AddTechStrapPersistence();
builder.Services.AddTechStrapSecurity();
builder.Services.AddTechStrapDevelopmentSeeding();
```

`src/TechStrap.Api/Startup/OpenApiSecurity.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using TechStrap.Api.Security;
using TechStrap.Contracts.Http;

namespace TechStrap.Api.Startup;

/// <summary>
/// Documents how a caller proves who they are (PHASE-07c, PHASE-11): the typed clients and the SDK are generated from the OpenAPI document, so it must say which operations take which credential.
/// Three schemes, one per way in (02-ARCHITECTURE section 4): <see cref="Bearer"/> for an agent's OIDC access token (the Agent and Admin policies), <see cref="ApiKey"/> for a product's key on intake,
/// and <see cref="TicketToken"/> for a customer's access token. An operation names the scheme its policy needs; a public operation names none. The document is the same for everyone and is
/// documentation only: the policies on the controllers still decide, and nothing here changes what the API accepts.
/// </summary>
public static class OpenApiSecurity
{
    public const string Bearer = "Bearer";
    public const string ApiKey = "ApiKey";
    public const string TicketToken = "TicketToken";

    /// <summary>Adds the three security schemes to the document and the matching security requirement to every operation that is not public.</summary>
    public static OpenApiOptions AddTechStrapSecuritySchemes(this OpenApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[Bearer] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "An agent's access token from the identity provider (OIDC). Sent as Authorization: Bearer <token>.",
            };
            document.Components.SecuritySchemes[ApiKey] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = HeaderNames.ApiKey,
                Description = "A product API key, for ticket intake from the product's own backend.",
            };
            document.Components.SecuritySchemes[TicketToken] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = HeaderNames.TicketToken,
                Description = "A customer's ticket access token, from the link in the email they were sent.",
            };
            return Task.CompletedTask;
        });
        options.AddOperationTransformer((operation, context, _) =>
        {
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;
            var scheme = SchemeFor(metadata, TakesTicketToken(operation));
            if (scheme is not null)
            {
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(scheme, context.Document)] = [],
                });
            }

            return Task.CompletedTask;
        });
        return options;
    }

    /// <summary>
    /// The scheme an operation needs, from its authorization metadata, or null for a public one. Agent and Admin routes take the bearer token, the ApiKey policy the product key. A route that is
    /// public by policy but reads the customer's <c>X-Ticket-Token</c> header takes the ticket token. <c>[AllowAnonymous]</c> wins over everything.
    /// </summary>
    internal static string? SchemeFor(IEnumerable<object> endpointMetadata, bool takesTicketToken)
    {
        var metadata = endpointMetadata.ToList();
        if (metadata.OfType<IAllowAnonymous>().Any())
        {
            return null;
        }

        var policies = metadata.OfType<IAuthorizeData>().Select(data => data.Policy).ToList();
        if (policies.Contains(AuthorizationPolicies.Admin) || policies.Contains(AuthorizationPolicies.Agent))
        {
            return Bearer;
        }

        if (policies.Contains(AuthorizationPolicies.ApiKey))
        {
            return ApiKey;
        }

        return takesTicketToken ? TicketToken : null;
    }

    private static bool TakesTicketToken(Microsoft.OpenApi.OpenApiOperation operation) =>
        operation.Parameters?.Any(parameter => parameter.In == ParameterLocation.Header
            && string.Equals(parameter.Name, HeaderNames.TicketToken, StringComparison.OrdinalIgnoreCase)) == true;
}
```

`src/TechStrap.Application/Products/UpdateProductRequestHandler.cs`

Replace

```csharp
            return Result<ProductDto>.Failure(actor.Errors[0]);
        }

        var input = request.Branding;
        var branding = ProductBranding.Create(input?.DisplayName, input?.LogoPath, input?.AccentColour, input?.FromAddress, input?.ReplyTo);
        if (branding.IsFailure)
        {
            return Result<ProductDto>.Failure(branding.Error!.ToError());
        }

        var product = await products.GetByIdAsync(productId, cancellationToken);
        if (product is null)
        {
            return Result<ProductDto>.Failure(ProductErrors.NotFound());
        }

        if (product.Version != request.Version)
```

with

```csharp
            return Result<ProductDto>.Failure(actor.Errors[0]);
        }

        var product = await products.GetByIdAsync(productId, cancellationToken);
        if (product is null)
        {
            return Result<ProductDto>.Failure(ProductErrors.NotFound());
        }

        // The stored branding is passed in so a logo address saved before the logo rule, and left as it is, does not block the edit (ProductBranding.CreateForUpdate).
        var input = request.Branding;
        var branding = ProductBranding.CreateForUpdate(product.Branding, input?.DisplayName, input?.LogoPath, input?.AccentColour, input?.FromAddress, input?.ReplyTo);
        if (branding.IsFailure)
        {
            return Result<ProductDto>.Failure(branding.Error!.ToError());
        }

        if (product.Version != request.Version)
```

`src/TechStrap.Domain/Products/Product.cs`

Replace

```csharp
        string? logoPath,
        string? accentColour,
        string? fromAddress,
        string? replyTo)
    {
        var name = Guard.RequiredText(displayName, DomainLimits.NameMaxLength, "display-name");
        var logo = Guard.OptionalImageUrl(logoPath, DomainLimits.UrlMaxLength, "logo-path");
        var accent = Guard.Colour(accentColour ?? DefaultAccentColour, "accent-colour");
        var from = Guard.OptionalEmail(fromAddress, "from-address");
        var reply = Guard.OptionalEmail(replyTo, "reply-to");
```

with

```csharp
        string? logoPath,
        string? accentColour,
        string? fromAddress,
        string? replyTo) =>
        Build(displayName, Guard.OptionalImageUrl(logoPath, DomainLimits.UrlMaxLength, "logo-path"), accentColour, fromAddress, replyTo);

    /// <summary>
    /// Branding for an update of a stored product. The logo address the product already has is accepted as it is when the request carries it unchanged (compared after trimming), because
    /// products saved before the logo rule (a relative path, say) must stay editable: renaming one must not need a logo it never had. A different address, or one for a product that has none,
    /// is checked by the full rule; every other field is always checked.
    /// </summary>
    public static DomainResult<ProductBranding> CreateForUpdate(
        ProductBranding current,
        string? displayName,
        string? logoPath,
        string? accentColour,
        string? fromAddress,
        string? replyTo)
    {
        ArgumentNullException.ThrowIfNull(current);
        var unchanged = string.Equals(logoPath?.Trim(), current.LogoPath, StringComparison.Ordinal);
        var logo = unchanged
            ? DomainResult<string?>.Ok(current.LogoPath)
            : Guard.OptionalImageUrl(logoPath, DomainLimits.UrlMaxLength, "logo-path");
        return Build(displayName, logo, accentColour, fromAddress, replyTo);
    }

    private static DomainResult<ProductBranding> Build(string? displayName, DomainResult<string?> logo, string? accentColour, string? fromAddress, string? replyTo)
    {
        var name = Guard.RequiredText(displayName, DomainLimits.NameMaxLength, "display-name");
        var accent = Guard.Colour(accentColour ?? DefaultAccentColour, "accent-colour");
        var from = Guard.OptionalEmail(fromAddress, "from-address");
        var reply = Guard.OptionalEmail(replyTo, "reply-to");
```

`src/TechStrap.Hosting/Sentry/SensitiveQuerySentryProcessor.cs`

```csharp
using System.Text.RegularExpressions;
using Sentry;
using Sentry.Extensibility;

namespace TechStrap.Hosting.Sentry;

/// <summary>
/// Masks the text an agent searched for in what Sentry records. The queue keeps its search in the address (<c>/queue/mine?search=...</c>) so a view can be bookmarked, and the same text goes to the
/// API as <c>GET /api/tickets?search=...</c>. A search is often a requester's email address or a subject line, so on an unhandled exception it must not reach Sentry in the request's query string
/// or URL, in a breadcrumb, or in the description of a span. The value becomes <c>[redacted]</c> and the rest of the address is left alone, so the event still shows which page failed.
/// The parameters are <c>search</c> (the queue and the ticket list) and <c>q</c> (a free-text query). Registered with the header scrubber by <see cref="SentryOptionsExtensions.AddSensitiveHeaderScrubbing"/>.
/// </summary>
public sealed partial class SensitiveQuerySentryProcessor : ISentryEventProcessor, ISentryTransactionProcessor
{
    /// <summary>What replaces a masked value.</summary>
    public const string Mask = "[redacted]";

    // A parameter at the start of a query string or after "?" or "&", up to the next "&", "#", a space or a quote: "search=a%40b.test", "?search=a&page=2", "http://h/queue?q=x#top".
    [GeneratedRegex(@"(?<=^|[?&])(?<name>search|q)=[^&#\s""']*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveParameter();

    /// <summary>The text with the value of every sensitive query parameter masked. Null stays null.</summary>
    public static string? Scrub(string? text) =>
        string.IsNullOrEmpty(text) ? text : SensitiveParameter().Replace(text, match => $"{match.Groups["name"].Value}={Mask}");

    public SentryEvent? Process(SentryEvent @event)
    {
        ScrubRequest(@event.Request);
        return @event;
    }

    public SentryTransaction? Process(SentryTransaction transaction)
    {
        ScrubRequest(transaction.Request);
        foreach (var span in transaction.Spans)
        {
            span.Description = Scrub(span.Description);
            foreach (var (key, value) in span.Data.ToList())
            {
                if (value is string text && Scrub(text) != text)
                {
                    span.SetData(key, Scrub(text));
                }
            }
        }

        return transaction;
    }

    /// <summary>
    /// The <c>BeforeBreadcrumb</c> hook: a breadcrumb is immutable, so one that carries a search is replaced by a masked copy. The HTTP breadcrumbs of the API client carry the address in
    /// <c>url</c> and the query in <c>http.query</c>; a log breadcrumb carries it in the message. The hook runs when the breadcrumb is added, so the copy's timestamp is the same moment.
    /// </summary>
    public static Breadcrumb? ScrubBreadcrumb(Breadcrumb breadcrumb, SentryHint hint)
    {
        ArgumentNullException.ThrowIfNull(breadcrumb);
        var message = Scrub(breadcrumb.Message);
        var data = breadcrumb.Data?.ToDictionary(pair => pair.Key, pair => Scrub(pair.Value) ?? string.Empty);
        var changed = message != breadcrumb.Message || (data is not null && breadcrumb.Data!.Any(pair => data[pair.Key] != pair.Value));
        return changed
            ? new Breadcrumb(message!, breadcrumb.Type!, data, breadcrumb.Category, breadcrumb.Level)
            : breadcrumb;
    }

    private static void ScrubRequest(SentryRequest request)
    {
        request.QueryString = Scrub(request.QueryString);
        request.Url = Scrub(request.Url);
    }
}
```

`src/TechStrap.Hosting/Sentry/SentryOptionsExtensions.cs` (2 edits)

Replace

```csharp
public static class SentryOptionsExtensions
{
    /// <summary>
    /// Registers <see cref="SensitiveHeaderSentryProcessor"/> for events and transactions, so no host forgets one of the two. Call it from the
    /// <c>UseSentry</c> callback of every host that serves requests (Api, Admin, and Portal from PHASE-09).
    /// </summary>
    public static void AddSensitiveHeaderScrubbing(this SentryOptions options)
    {
```

with

```csharp
public static class SentryOptionsExtensions
{
    /// <summary>
    /// Registers <see cref="SensitiveHeaderSentryProcessor"/> for events and transactions, so no host forgets one of the two, and the same for <see cref="SensitiveQuerySentryProcessor"/>
    /// (the text an agent searched for) together with its breadcrumb hook. Call it from the <c>UseSentry</c> callback of every host that serves requests (Api, Admin, and Portal from PHASE-09).
    /// The breadcrumb hook is the one <c>BeforeBreadcrumb</c> callback the options have, so a host that wants its own must call this first and wrap what it needs around it.
    /// </summary>
    public static void AddSensitiveHeaderScrubbing(this SentryOptions options)
    {
```

Replace

```csharp
        var processor = new SensitiveHeaderSentryProcessor();
        options.AddEventProcessor(processor);
        options.AddTransactionProcessor(processor);
    }
}
```

with

```csharp
        var processor = new SensitiveHeaderSentryProcessor();
        options.AddEventProcessor(processor);
        options.AddTransactionProcessor(processor);

        var query = new SensitiveQuerySentryProcessor();
        options.AddEventProcessor(query);
        options.AddTransactionProcessor(query);
        options.SetBeforeBreadcrumb(SensitiveQuerySentryProcessor.ScrubBreadcrumb);
    }
}
```

- [ ] **Step 4: Run the tests, then prove each pin bites**

```bash
dotnet test --project tests/TechStrap.Api.Tests -c Release
dotnet test --project tests/TechStrap.Domain.Tests -c Release
dotnet test --project tests/TechStrap.Application.Tests -c Release
dotnet test --project tests/TechStrap.Admin.Tests -c Release
```

Expected: all PASS (in the scratch copy: Admin 1474 to 1490, Application 561 to 564, Domain 380 to 389). `OpenApiSurfaceTests` still passes: the document lists the same operations, now with security.

Mutations, each applied to the finished code, the tests run, and the change reverted. Revert by hand or with `git stash`: before this task is committed, `git checkout -- <file>` would restore the old file, not the finished one.

| Mutation | Failing tests |
| :-- | :-- |
| `SensitiveQuerySentryProcessor.ScrubRequest`: leave `QueryString` alone | `A_transaction_loses_the_search_from_its_request_and_from_every_span`, `An_event_loses_the_search_from_its_query_string_and_url_and_keeps_everything_else`, `The_host_registration_masks_the_search_in_an_event_and_a_breadcrumb_that_reach_the_transport...` |
| the same for `Url` | the same three tests |
| spans: do not scrub `Description` | `A_transaction_loses_the_search_from_its_request_and_from_every_span` |
| `AddSensitiveHeaderScrubbing`: do not call `SetBeforeBreadcrumb` | `The_host_registration_masks_the_search_in_an_event_and_a_breadcrumb_that_reach_the_transport...` (the control still sees the leak) |
| `AddSensitiveHeaderScrubbing`: do not add the query processor to transactions | `SentryOptionsExtensionsTests.Scrubbing_also_registers_the_search_processor_for_events_and_for_transactions` |
| the regex matches `search` only (not `q`) | three rows of `The_value_of_search_and_q_is_masked_and_the_rest_of_the_address_is_kept` |
| `OpenApiSecurity`: no operation requirement | 11 tests: `An_operation_names_the_one_scheme_its_policy_needs` (all ten rows) and `Every_operation_behind_an_agent_admin_or_api_key_policy_carries_a_requirement` |
| Admin policy no longer maps to `Bearer` | `Anonymous_beats_every_policy_and_a_ticket_token_header_alone_does_not_make_an_agent_route_a_customer_one`, `Every_operation_behind_...` |
| a ticket-token header no longer maps to `TicketToken` | the three `/api/customer/...` rows and `Anonymous_beats_every_policy_...` |
| a public operation maps to `Bearer` | the three `A_public_operation_names_no_scheme` rows and `Anonymous_beats_every_policy_...` |
| the schemes are declared under another name | `The_document_declares_the_bearer_the_api_key_and_the_ticket_token_schemes`, the `ApiKey` row, `Every_operation_...` |
| `[AllowAnonymous]` no longer wins | `Anonymous_beats_every_policy_and_a_ticket_token_header_alone_does_not_make_an_agent_route_a_customer_one` |
| `AgentsContent`: any finished read clears every mark (`Clear()` instead of `ReleaseForLoad(loadId)`) | `AgentsPageTests.A_read_that_started_before_the_lost_answer_does_not_release_the_hold_and_one_that_started_after_it_does` |
| `DeadLettersContent`: the same | `DeadLettersPageTests.A_read_that_started_before_the_lost_retry_answer_does_not_release_the_hold` |
| `AgentsContent`: mark with load id `0` | the agents race test |
| `UncertainMarks.ReleaseForLoad`: `<=` | `UncertainMarksTests.Only_a_read_that_started_after_the_mark_releases_it` (the equal row) and both race tests |
| `UncertainMarks.ReleaseForLoad`: never release | seven tests, among them `After_a_lost_activate_answer_a_second_click_sends_nothing_until_the_list_has_been_read_again` and the failed-email twin |
| `AdminEventsContent`: `await LoadAgentsAsync()` | `AdminEventsPageTests.The_events_render_the_moment_they_arrive_while_the_agent_list_is_still_on_its_way`, `The_events_are_read_without_waiting_for_the_agent_list` |
| `PublicDisplayNameField`: ignore `typed == _committed` after an uncertain save | `PublicDisplayNameFieldTests.Retyping_the_committed_name_after_an_uncertain_save_says_to_reload_instead_of_doing_nothing` |
| `NotificationPreferencesPage`: no `_version++` on a swallowed toggle | `NotificationPreferencesPageTests.A_toggle_that_is_swallowed_while_a_save_runs_draws_the_checkbox_again_from_what_is_saved` |
| `ProductBranding.CreateForUpdate`: never treat the logo as unchanged | five rows of `ProductBrandingLogoTests` |
| the comparison is case-insensitive | `A_different_logo_is_checked_by_the_full_rule_even_when_the_product_has_a_stored_one` (`/IMAGES/OLD-LOGO.PNG`) |
| the handler passes no stored branding | `UpdateProductRequestHandlerTests.A_product_with_a_relative_logo_from_before_the_rule_can_be_renamed_with_the_logo_left_as_it_is` |
| `ProductEditorViewModel`: drop the `LogoUnchanged` clause of the logo check | `ProductEditorTests.A_product_saved_with_a_relative_logo_before_the_rule_can_be_renamed_with_the_logo_left_alone`, `Changing_a_relative_logo_to_another_unsafe_address_is_refused_and_changing_it_back_is_accepted` |

One of the planned pins turned out not to be a pin: a test that observed the old `_version` through the DOM identity of the checkbox passed with the bump removed, because bUnit rebuilds its DOM on every render. It was replaced by the `data-revision` attribute above, and the mutation in the table (no `_version++`) now fails the test.

- [ ] **Step 5: Build and run the whole suite**

```bash
dotnet build TechStrap.slnx -c Release
dotnet test --solution TechStrap.CI.slnf -c Release
```

Expected: 0 warnings; all tests PASS. One mutated run of the Api tests in the scratch copy also failed `TicketDetailEndpointTests.An_agent_reads_a_ticket_by_number_and_by_id`; it passed in every other run, including the full run above. If you see it, run it again and mention it in the PR.

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Admin/Features/Account/NotificationPreferencesPage.razor \
  src/TechStrap.Admin/Features/Account/PublicDisplayNameField.razor.cs \
  src/TechStrap.Admin/Features/Ops/DeadLetters/DeadLettersContent.razor.cs \
  src/TechStrap.Admin/Features/Settings/Agents/AgentsContent.razor.cs \
  src/TechStrap.Admin/Features/Settings/Audit/AdminEventsContent.razor.cs \
  src/TechStrap.Admin/Features/Settings/Products/ApiKeysPanel.razor.cs \
  src/TechStrap.Admin/Features/Settings/Products/ProductEditorViewModel.cs \
  src/TechStrap.Admin/Features/Settings/Tags/TagsContent.razor.cs \
  src/TechStrap.Admin/Features/Shell/UncertainMarks.cs \
  src/TechStrap.Api/Program.cs \
  src/TechStrap.Api/Startup/OpenApiSecurity.cs \
  src/TechStrap.Application/Products/UpdateProductRequestHandler.cs \
  src/TechStrap.Domain/Products/Product.cs \
  src/TechStrap.Hosting/Sentry/SensitiveQuerySentryProcessor.cs \
  src/TechStrap.Hosting/Sentry/SentryOptionsExtensions.cs \
  tests/TechStrap.Admin.Tests/Components/AdminEventsPageTests.cs \
  tests/TechStrap.Admin.Tests/Components/AgentsPageTests.cs \
  tests/TechStrap.Admin.Tests/Components/DeadLettersPageTests.cs \
  tests/TechStrap.Admin.Tests/Components/NotificationPreferencesPageTests.cs \
  tests/TechStrap.Admin.Tests/Components/ProductEditorTests.cs \
  tests/TechStrap.Admin.Tests/Components/PublicDisplayNameFieldTests.cs \
  tests/TechStrap.Admin.Tests/Components/UncertainMarksTests.cs \
  tests/TechStrap.Api.Tests/OpenApiSecurityTests.cs \
  tests/TechStrap.Api.Tests/SensitiveQuerySentryProcessorTests.cs \
  tests/TechStrap.Api.Tests/SentryOptionsExtensionsTests.cs \
  tests/TechStrap.Application.Tests/Products/UpdateProductRequestHandlerTests.cs \
  tests/TechStrap.Domain.Tests/Products/ProductBrandingLogoTests.cs
git diff --cached --stat
git commit -m "feat: mask searches in Sentry, document the security schemes, close the 07b minors" -m "Sentry masks the search and q query values in requests, spans and breadcrumbs. OpenAPI documents Bearer, ApiKey and TicketToken. A lost write is held until a read that started after it, the audit page does not wait for the agent list, and a product with a relative logo can be renamed." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```

### Task 10: Compose verification and the closing docs (P07-T20, D-042)

**Review Focus pins:** none of the five; this task is the evidence for T20 and the record of the whole phase. The smoke is pinned by its Pester tests, and by one real run whose output goes into the PR.

**Files:**
- Modify: `.github/workflows/ci.yml`
- Modify: `docs/architecture/00-DISCOVERY-INDEX.md`
- Modify: `docs/architecture/04-DECISION-LOG.md`
- Modify: `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`
- Modify: `docs/architecture/PHASE-07-admin-app.md`
- Modify: `docs/development/ADMIN-APP.md`
- Create: `scripts/Test-ComposeSmoke.ps1`
- Modify: `scripts/tests/ComposeFiles.Tests.ps1`
- Create: `scripts/tests/ComposeSmoke.Tests.ps1`

**Interfaces:**
- Consumes:
  - `docker-compose.yml` (the local stack: Postgres 17, Mailpit, Api, Admin, Portal, Worker; Admin and Api health checks, `depends_on` with `service_healthy`), `Dockerfile.admin`, the Admin and Api `/health/ready` endpoints (`MapStandardHealthChecks`; the Admin registers no `ready`-tagged check, so its ready answer is 200 while the host runs), `scripts/Invoke-ScriptTests.ps1` and the Pester suites (`ComposeFiles.Tests.ps1` runs `docker compose config` and is skipped without Docker), `.github/workflows/ci.yml` (CI has Docker: `build-test` uses Testcontainers and `docker-build` builds the four images, but nothing starts the stack).
  - The decision log (`04-DECISION-LOG.md`: the header list, the index table, one section per decision), `ADMIN-APP.md`, `PHASE-07-admin-app.md` (the task boxes, the carry-forward list), `99-IMPLEMENTATION-ROADMAP.md` and `00-DISCOVERY-INDEX.md` (the phase row).
  - From Tasks 1 to 5 (final), as the docs state them: the Admin architecture rules and their failing samples (Task 1); `SessionExpiry`, `AgentSession.ExpiredWhileWorking` and `IsAdmitted`, the `SessionExpiredBanner`, `ReadRetryAfterCap` of 2 s (Task 2); `TechStrap.Hosting.Wiring` (`AddTechStrapHttpClientDefaults` in all four hosts, `AddTechStrapWebHost`, `UseTechStrapWebHost`, `UseTechStrapErrorPages`, `AddTechStrapObservability` for the Portal), the Portal's reference to Contracts and Hosting, `FactoryClientLeakTests`, and the finding that the OTLP exporter going through the factory was not reproduced (Task 3); `TechStrapCsp.ForBlazorApp` and `ForApi`, `<ImportMap />` removed, the websocket under `connect-src 'self'` left as an owner browser check (Task 4); `theme-init.js` loaded by `App.razor` and `SignInLanding.razor` (Task 5). The docs script names these types because they are now fixed.
- Produces:
  - `scripts/Test-ComposeSmoke.ps1`, `scripts/tests/ComposeSmoke.Tests.ps1`, a manually started `compose-smoke` job in `ci.yml`, and Pester checks that the Admin has a health check on `/health/live`, waits for a healthy Api and keeps its key ring on a volume in all three compose files.
  - D-042 in the decision log (with its index row and header line), the 07c sections of `ADMIN-APP.md` (the CSP, the theme script, local time, the palette, the responsive behaviour, the session-expired banner, the owner's manual checklist, the compose smoke, the 07c known gaps), the ticks and evidence in `PHASE-07-admin-app.md` (T19, T20, T05, T06, the two carry-forwards; T02 stays open), and the phase row in the roadmap and the index.

**Rules:**
1. **The smoke** (`docker compose up -d --wait`, the Api and the Admin answer `/health/ready` with 200, the Admin container is healthy, then `down`). It builds the four images one after another and not with `up --build`: compose builds them in parallel and four restores into the one shared NuGet cache mount (`--mount=type=cache,id=techstrap-nuget`) corrupted each other in the first real run ("Could not find file .../markdig/...").
2. **It never touches a stack you run by hand.** Its project name is `techstrap-smoke`; the name `techstrap` is refused. Every published port is a free loopback port chosen by Docker (an override file with `!override`), so 8080 to 8082 and 8025 may be in use. The images are named `techstrap-smoke-*:local`, so building them does not replace the `techstrap-*:local` images of your own stack (the first real run did replace them; that is why the names exist).
3. **It never removes volumes**: `down` without `-v`, and no `docker volume` command. The `techstrap-smoke_*` volumes stay for the next run.
4. **It is opt-in.** CI has Docker, but the smoke builds four images and starts the whole stack, so it is a job that runs only when the workflow is started by hand (`workflow_dispatch`), not on every pull request.
5. **The docs are applied by a script that stops if a text it replaces is not there exactly once**, so a drifted document is never half edited. The new text is ASCII.
6. **Honest ticks.** T19 and T20 are ticked with their evidence. T19's manual parts (the checklist, the keyboard-only run, the axe run) need a signed-in session, so the line says they are the owner's and wait for owner action 7; T02 stays open for the same reason.

- [ ] **Step 1: Write the failing tests**

The Pester tests for the script and the three compose files. `ComposeSmoke.Tests.ps1` runs the script with `-DryRun`, which prints what it would run and runs nothing, so it needs no Docker.

`scripts/tests/ComposeFiles.Tests.ps1`

Replace

```powershell
        $result.Config.services.admin.environment.TECHSTRAP_GROUP_CLAIM_TYPE | Should -Be 'groups'
    }

    It 'local compose gives the Admin placeholder OIDC values so the container starts without an identity provider' {
        $admin = (Get-ComposeConfig -File 'docker-compose.yml').Config.services.admin.environment
        $admin.Auth__Authority | Should -Match '^https://'
```

with

```powershell
        $result.Config.services.admin.environment.TECHSTRAP_GROUP_CLAIM_TYPE | Should -Be 'groups'
    }

    It '<file> starts the Admin only after a healthy Api and gives it a health check on /health/live and a persistent key ring' -ForEach @(
        @{ file = 'docker-compose.yml'; withEnv = $false }
        @{ file = 'docker-compose.uat.yml'; withEnv = $true }
        @{ file = 'docker-compose.production.yml'; withEnv = $true }
    ) {
        $envFile = ''
        if ($withEnv) {
            $envFile = Join-Path $TestDrive 'env-admin-health'
            New-ProductionEnvFile -Path $envFile
        }
        $admin = (Get-ComposeConfig -File $file -EnvFile $envFile).Config.services.admin

        $admin.depends_on.api.condition | Should -Be 'service_healthy'
        ($admin.healthcheck.test -join ' ') | Should -Match '/health/live'
        @($admin.volumes | Where-Object { $_.target -eq '/app/dataprotection-keys' }).Count | Should -Be 1
    }

    It 'local compose gives the Admin placeholder OIDC values so the container starts without an identity provider' {
        $admin = (Get-ComposeConfig -File 'docker-compose.yml').Config.services.admin.environment
        $admin.Auth__Authority | Should -Match '^https://'
```

`scripts/tests/ComposeSmoke.Tests.ps1`

```powershell
BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:SmokeScript = Join-Path $script:RepoRoot 'scripts' 'Test-ComposeSmoke.ps1'
    $script:SmokeText = Get-Content -LiteralPath $script:SmokeScript -Raw
    $script:DryRun = & $script:SmokeScript -DryRun | Out-String
}

Describe 'Test-ComposeSmoke.ps1' {
    It 'parses without errors' {
        $errors = $null
        [System.Management.Automation.Language.Parser]::ParseFile($script:SmokeScript, [ref]$null, [ref]$errors) | Out-Null
        $errors | Should -BeNullOrEmpty
    }

    It 'under -DryRun prints the commands it would run and runs none of them' {
        $script:DryRun | Should -Match 'up -d --wait'
        $script:DryRun | Should -Match 'port api 80'
        $script:DryRun | Should -Match 'port admin 80'
        $script:DryRun | Should -Match 'health/ready'
        $script:DryRun | Should -Match ' down\r?\n'
    }

    It 'uses its own project name, so it can never stop the stack run by hand' {
        $script:DryRun | Should -Match 'compose -p techstrap-smoke '
        $script:DryRun | Should -Not -Match 'compose -p techstrap '
        { & $script:SmokeScript -ProjectName 'techstrap' -DryRun } | Should -Throw '*Refusing*'
    }

    It 'never removes volumes' {
        # The one place it stops the stack passes the single argument 'down': no -v and no --volumes after it.
        $script:SmokeText | Should -Match "Invoke-Compose -Arguments @\('down'\)"
        $script:SmokeText | Should -Not -Match "'down'\s*,"
        $script:SmokeText | Should -Not -Match "'(-v|--volumes)'"
        $script:SmokeText | Should -Not -Match '(?i)docker volume (rm|prune)'
        $script:DryRun | Should -Not -Match '(?i)\sdown\s+(-v|--volumes)'
    }

    It 'publishes every port on a free loopback port and names its own images' {
        $script:DryRun | Should -Match '(?s)api:\s+image: techstrap-smoke-api:local\s+ports: !override\s+- "127\.0\.0\.1::80"'
        $script:DryRun | Should -Match '(?s)admin:\s+image: techstrap-smoke-admin:local\s+ports: !override\s+- "127\.0\.0\.1::80"'
        $script:DryRun | Should -Match '(?s)portal:\s+image: techstrap-smoke-portal:local\s+ports: !override\s+- "127\.0\.0\.1::80"'
        $script:DryRun | Should -Match '(?s)mailpit:\s+ports: !override\s+- "127\.0\.0\.1::8025"'
        $ports = [regex]::Matches($script:DryRun, '(?m)^\s+- "([^"]+)"\s*$') | ForEach-Object { $_.Groups[1].Value }
        @($ports).Count | Should -Be 4
        foreach ($port in $ports) { $port | Should -Match '^127\.0\.0\.1::\d+$' }
    }

    It 'builds the four images one after another, never with up --build (parallel restores corrupt the shared NuGet cache mount)' {
        foreach ($service in 'api', 'worker', 'admin', 'portal') {
            $script:DryRun | Should -Match "build $service"
        }
        $script:DryRun | Should -Not -Match 'up [^\r\n]*--build'
        $quiet = & $script:SmokeScript -DryRun -NoBuild | Out-String
        $quiet | Should -Not -Match ' build '
    }

    It 'leaves the stack running only when asked to' {
        (& $script:SmokeScript -DryRun -KeepRunning | Out-String) | Should -Not -Match ' down'
    }
}

Describe 'the compose smoke in CI' {
    BeforeAll { $script:Workflow = Get-Content -LiteralPath (Join-Path $script:RepoRoot '.github' 'workflows' 'ci.yml') -Raw }

    It 'is a manually started job, so a pull request does not build four images twice' {
        $script:Workflow | Should -Match '(?m)^  workflow_dispatch:'
        $script:Workflow | Should -Match '(?s)compose-smoke:\s+name: Compose smoke \(manual\)\s+if: github\.event_name == ''workflow_dispatch'''
        $script:Workflow | Should -Match 'scripts/Test-ComposeSmoke\.ps1'
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

```bash
pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ComposeSmoke.Tests.ps1
```

Expected: FAIL, the script does not exist (the `BeforeAll` that runs it throws a `CommandNotFoundException`).

- [ ] **Step 3: Implement**

The script and the workflow job.

`.github/workflows/ci.yml` (2 edits)

Replace

```yaml
  pull_request:
  push:
    branches: [main]

concurrency:
  group: ci-${{ github.ref }}
```

with

```yaml
  pull_request:
  push:
    branches: [main]
  workflow_dispatch:

concurrency:
  group: ci-${{ github.ref }}
```

Replace

```yaml
        shell: pwsh
        run: ./Build-TechStrapDocker.ps1 -Platforms linux/amd64 -ImageTag "0.0.0-ci.${{ github.run_number }}" -PushLatest:$false
```

with

```yaml
        shell: pwsh
        run: ./Build-TechStrapDocker.ps1 -Platforms linux/amd64 -ImageTag "0.0.0-ci.${{ github.run_number }}" -PushLatest:$false

  # Started by hand (Actions > CI > Run workflow): it builds four images and starts the whole local stack, so a pull request does not pay for it.
  compose-smoke:
    name: Compose smoke (manual)
    if: github.event_name == 'workflow_dispatch'
    runs-on: ubuntu-latest
    needs: build-test
    steps:
      - uses: actions/checkout@v7
        with:
          fetch-depth: 0

      - name: Start the local stack and check the Api and the Admin
        shell: pwsh
        run: ./scripts/Test-ComposeSmoke.ps1
```

`scripts/Test-ComposeSmoke.ps1`

```powershell
<#
.SYNOPSIS
Starts the local compose stack, checks that the Api and the Admin answer, and stops it again.
.DESCRIPTION
The PHASE-07 T20 check: "docker compose up" gives a healthy Admin and an Api whose /health/ready answers 200. It is opt-in (it builds four images and needs Docker),
so it is not part of the default CI run; run it by hand before merging a change to a Dockerfile, a compose file or the host wiring, or start the "Compose smoke" workflow.

It never touches a stack you already run. It uses its own compose project name (techstrap-smoke, never the default "techstrap"), publishes every host port on a free
port chosen by Docker instead of 8080 to 8082 and 8025, and stops only that project. It never removes volumes: "docker compose down" is run without -v, so the Postgres and
key-ring volumes of the smoke project stay for the next run (they are named techstrap-smoke_*; remove them yourself when you want them gone).

The Admin starts with placeholder OIDC settings (docker-compose.yml), so nothing here signs in; it checks the container's health and its /health/ready endpoint.
.PARAMETER ProjectName
The compose project name. "techstrap" is refused, because that is the name of the stack you may be running.
.PARAMETER NoBuild
Do not rebuild the images; use the ones that exist (techstrap-smoke-*:local, left by an earlier run).
.PARAMETER KeepRunning
Leave the stack running after the checks (for looking at it); stop it later with: docker compose -p techstrap-smoke down
.PARAMETER DryRun
Print the commands and the override file and run nothing.
.EXAMPLE
pwsh ./scripts/Test-ComposeSmoke.ps1
.EXAMPLE
pwsh ./scripts/Test-ComposeSmoke.ps1 -NoBuild
#>
[CmdletBinding()]
param(
    [string] $ComposeFile = (Join-Path $PSScriptRoot '..' 'docker-compose.yml'),
    [string] $ProjectName = 'techstrap-smoke',
    [int] $TimeoutSeconds = 900,
    [switch] $NoBuild,
    [switch] $KeepRunning,
    [switch] $DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($ProjectName -eq 'techstrap') {
    throw "Refusing to use the project name 'techstrap': it is the name of the stack you run by hand, and this script stops its project when it finishes."
}

# Every published port becomes "a free port on loopback", so a stack that already holds 8080 to 8082 or 8025 is not in the way. "!override" replaces the list instead of adding to it.
# The images get their own names too (techstrap-smoke-*:local), so building them never replaces the techstrap-*:local images of the stack you run by hand.
$overrideText = @'
services:
  api:
    image: techstrap-smoke-api:local
    ports: !override
      - "127.0.0.1::80"
  worker:
    image: techstrap-smoke-worker:local
  admin:
    image: techstrap-smoke-admin:local
    ports: !override
      - "127.0.0.1::80"
  portal:
    image: techstrap-smoke-portal:local
    ports: !override
      - "127.0.0.1::80"
  mailpit:
    ports: !override
      - "127.0.0.1::8025"
'@

$overridePath = Join-Path ([System.IO.Path]::GetTempPath()) "techstrap-smoke-$([guid]::NewGuid().ToString('N')).override.yml"
$composeArguments = @('compose', '-p', $ProjectName, '-f', (Resolve-Path -LiteralPath $ComposeFile).Path, '-f', $overridePath)

function Invoke-Compose {
    param([Parameter(Mandatory)][string[]] $Arguments, [switch] $AllowFailure)

    $output = & docker @composeArguments @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -and -not $AllowFailure) {
        throw "docker $($composeArguments -join ' ') $($Arguments -join ' ') failed ($LASTEXITCODE):`n$output"
    }

    return $output
}

function Get-PublishedPort {
    param([Parameter(Mandatory)][string] $Service, [Parameter(Mandatory)][int] $ContainerPort)

    $mapping = (Invoke-Compose -Arguments @('port', $Service, "$ContainerPort")).Trim()
    if ($mapping -notmatch ':(?<port>\d+)\s*$') {
        throw "Could not read the published port of $Service from '$mapping'."
    }

    return [int] $Matches['port']
}

function Assert-Ready {
    param([Parameter(Mandatory)][string] $Name, [Parameter(Mandatory)][int] $Port)

    $uri = "http://127.0.0.1:$Port/health/ready"
    $response = Invoke-WebRequest -Uri $uri -UseBasicParsing -TimeoutSec 30 -SkipHttpErrorCheck
    if ($response.StatusCode -ne 200) {
        throw "$Name answered $($response.StatusCode) at $uri (expected 200)."
    }

    Write-Output "ok   $Name $uri -> 200"
}

# The four images are built one after another, never by "up --build": compose builds them in parallel, and four restores into the one shared NuGet cache mount
# (Dockerfile --mount=type=cache,id=techstrap-nuget) can corrupt each other ("Could not find file .../markdig/...").
$services = @('api', 'worker', 'admin', 'portal')
$upArguments = @('up', '-d', '--wait', '--wait-timeout', "$TimeoutSeconds")

if ($DryRun) {
    Write-Output "# override file ($overridePath)"
    Write-Output $overrideText
    if (-not $NoBuild) {
        foreach ($service in $services) {
            Write-Output "docker $($composeArguments -join ' ') build $service"
        }
    }

    Write-Output "docker $($composeArguments -join ' ') $($upArguments -join ' ')"
    Write-Output "docker $($composeArguments -join ' ') port api 80"
    Write-Output "docker $($composeArguments -join ' ') port admin 80"
    Write-Output "GET /health/ready on the Api and on the Admin, expecting 200"
    Write-Output "docker $($composeArguments -join ' ') ps admin --format json   (Health must be healthy)"
    if (-not $KeepRunning) {
        Write-Output "docker $($composeArguments -join ' ') down"
    }

    return
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue) -or ((& docker compose version 2>&1 | Out-String) -notmatch 'Docker Compose')) {
    throw 'Docker with the compose plugin is required.'
}

Set-Content -LiteralPath $overridePath -Value $overrideText -Encoding utf8
$failed = $true
try {
    Write-Output "Starting project $ProjectName (this builds the images unless -NoBuild is given)..."
    if (-not $NoBuild) {
        foreach ($service in $services) {
            Write-Output "Building $service..."
            Invoke-Compose -Arguments @('build', $service) | Out-Null
        }
    }

    Invoke-Compose -Arguments $upArguments | Out-Null

    $apiPort = Get-PublishedPort -Service 'api' -ContainerPort 80
    $adminPort = Get-PublishedPort -Service 'admin' -ContainerPort 80
    Assert-Ready -Name 'Api' -Port $apiPort
    Assert-Ready -Name 'Admin' -Port $adminPort

    $admin = (Invoke-Compose -Arguments @('ps', 'admin', '--format', 'json') | ConvertFrom-Json)
    if ($admin.Health -ne 'healthy') {
        throw "The Admin container reports health '$($admin.Health)' (expected healthy)."
    }

    Write-Output 'ok   Admin container is healthy'
    $failed = $false
}
finally {
    if ($failed) {
        Write-Output (Invoke-Compose -Arguments @('logs', '--tail', '60', 'api', 'admin') -AllowFailure)
    }

    if (-not $KeepRunning) {
        # Never -v: the volumes are not this script's to delete.
        Invoke-Compose -Arguments @('down') -AllowFailure | Out-Null
    }

    Remove-Item -LiteralPath $overridePath -Force -ErrorAction SilentlyContinue
}

Write-Output 'Compose smoke passed.'
```

- [ ] **Step 4: Run the tests, then prove each pin bites**

```bash
pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ComposeSmoke.Tests.ps1
pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ComposeFiles.Tests.ps1
```

Expected: PASS (8 tests in `ComposeSmoke.Tests.ps1`; `ComposeFiles.Tests.ps1` 27, skipped without Docker).

Mutations, each applied to the finished file, the Pester file run, and the change reverted:

| Mutation | Failing test |
| :-- | :-- |
| `Test-ComposeSmoke.ps1`: `down` with `-v` | `never removes volumes` |
| the project-name guard removed | `uses its own project name, so it can never stop the stack run by hand` |
| the `api` port published as `127.0.0.1:8080:80` | `publishes every port on a free loopback port and names its own images` |
| `--build` added to `up` | `builds the four images one after another, never with up --build...` |
| the `if: github.event_name == 'workflow_dispatch'` line removed from the job | `the compose smoke in CI` |
| `docker-compose.yml`: the Admin health check on `/` instead of `/health/live` | `docker-compose.yml starts the Admin only after a healthy Api and gives it a health check on /health/live and a persistent key ring` |

- [ ] **Step 5: Run the smoke for real**

This is the T20 evidence. It needs Docker, the network (the images restore NuGet packages and the Admin and Portal restore their fonts with libman) and several minutes.

```bash
pwsh -File scripts/Test-ComposeSmoke.ps1
```

Expected output, with your own port numbers:

```text
Starting project techstrap-smoke (this builds the images unless -NoBuild is given)...
Building api...
Building worker...
Building admin...
Building portal...
ok   Api http://127.0.0.1:64504/health/ready -> 200
ok   Admin http://127.0.0.1:64508/health/ready -> 200
ok   Admin container is healthy
Compose smoke passed.
```

Paste that output into the PR description. If it fails, the script prints the last 60 lines of the Api and Admin logs and still stops its own project. Afterwards `docker ps` shows no `techstrap-smoke` container; the four `techstrap-smoke-*:local` images and the `techstrap-smoke_*` volumes are left on purpose.

- [ ] **Step 6: The documents**

Save the script below outside the repository (for example `$TEMP/p07c_docs.py`) and run it from the repository root with `python $TEMP/p07c_docs.py`. It edits `ADMIN-APP.md`, `PHASE-07-admin-app.md`, the roadmap, the index and the decision log, and stops with an `AssertionError` that names the file and the start of the text if any replaced text is not there exactly once.

```python
import os

ROOT = os.getcwd()


def edit(path, old, new, count=1):
    """Exact replace in a repo-relative file; works with LF or CRLF working trees; stops if the text is not there exactly once."""
    p = os.path.join(ROOT, path)
    s = open(p, encoding='utf-8', newline='').read()
    if '\r\n' in s:
        old = old.replace('\r\n', '\n').replace('\n', '\r\n')
        new = new.replace('\r\n', '\n').replace('\n', '\r\n')
    assert s.count(old) == count, (path, old[:60], s.count(old))
    open(p, 'w', encoding='utf-8', newline='').write(s.replace(old, new))


ADMIN_DOC = 'docs/development/ADMIN-APP.md'
PHASE = 'docs/architecture/PHASE-07-admin-app.md'
LOG = 'docs/architecture/04-DECISION-LOG.md'
ROADMAP = 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md'
INDEX = 'docs/architecture/00-DISCOVERY-INDEX.md'

# ---------------------------------------------------------------------------------------------------------------------------- ADMIN-APP.md
edit(ADMIN_DOC, '''**07c**: polish, the command palette, compose and architecture rules, CSP. This page describes the app as it is once 07b is merged; the 07b parts say so.''',
     '''**07c**: the command palette, local time, the CSP and security headers, the responsive and accessibility pass, session resilience, compose verification and the Admin architecture rules. This page describes
the app as it is once 07c is merged; the 07b and 07c parts say so.''')

edit(ADMIN_DOC, '''(never on 500, and there is no circuit breaker); **writes are never retried** (a retried reply would send twice).''',
     '''(never on 500, and there is no circuit breaker); a `Retry-After` header is honoured but never waits longer than 2 seconds (07c); **writes are never retried** (a retried reply would send twice).''')

edit(ADMIN_DOC, '''### Roles

| What | Agent | Admin |''', '''**A 401 in the middle of a session (07c).** Every 401 is reported once, by `ApiConnection` to the scoped `SessionExpiry`, and `AgentSession` moves to "session expired". A 401 on the first load still shows the
full "Your session has expired" page. In the middle of work the session keeps what it knows (`ExpiredWhileWorking`; `IsAdmitted` and so `IsAdmin` stay as they were) and a `SessionExpiredBanner` (`role="alert"`) appears above the page with a "Sign in again" link to
`/signin/start?returnUrl=` for the page you are on; the page stays mounted, so an unsent reply or note is not lost. A 401 during a session reload keeps the agent too. After signing in again you land on the same page; a draft that was only in the circuit is gone with the circuit, so copy it before you follow the link if it matters.

### Roles

| What | Agent | Admin |''')

edit(ADMIN_DOC, '''    Ui/           reusable primitives: LoadingState, ErrorState, EmptyState, ConfirmDialog, PagerControl, TagChip, RelativeTime, StatusStamp, PriorityMark, AdminOnly (the admin page guard), AccentPreview, ...
    Layout/       MainLayout, NavMenu, StatusBar, AgentGate, ShortcutHelpDialog''',
     '''    Ui/           reusable primitives: LoadingState, ErrorState, EmptyState, ConfirmDialog, PagerControl, TagChip, RelativeTime (local time), ScrollRegion (a table's scroll box), StatusStamp, PriorityMark, AdminOnly (the admin page guard), AccentPreview, ...
    Layout/       MainLayout, NavMenu (the collapsible rail), RailLink, StatusBar, AgentGate, ShortcutHelpDialog, CommandPalette''')

edit(ADMIN_DOC, '''    Shell/        StatusMessageService, ShortcutService, ShortcutCatalog''',
     '''    Shell/        StatusMessageService, ShortcutService, ShortcutCatalog, CommandRegistry (the palette's commands), LocalTimeService (the browser's time zone), UncertainMarks (writes held until a later read)''')

edit(ADMIN_DOC, '''ES modules only: dialog.js, shortcuts.js, queue.js, preferences.js (browser preferences and the theme), clipboard.js (copy the new API key) (no inline script anywhere)''',
     '''dialog.js, shortcuts.js, queue.js, preferences.js (browser preferences and the theme), clipboard.js (copy the new API key), tz.js (the browser's time zone), palette.js and menu.js (keys of the command palette and the actions menu)
                  are ES modules; theme-init.js is the one classic script, loaded in the page head. No inline script anywhere''')

edit(ADMIN_DOC, '''The command palette (Ctrl+K) arrives in 07c.

| Key | Where | What |''', '''The command palette (Ctrl+K or Cmd+K) is a chord, not a single key, so it works while you type and when the single-key switch is off (WCAG 2.1.4).

| Key | Where | What |''')

edit(ADMIN_DOC, '''| `?` | Anywhere | Show the list |
''', '''| `Ctrl+K` / `Cmd+K` | Anywhere | Open or close the command palette |
| `?` | Anywhere | Show the list |

### Command palette (07c)

Ctrl+K opens a dialog with a search box and a list of commands. Type to filter (every word must appear in the name or the group), ArrowDown and ArrowUp move the selection (they wrap; Home and End jump), Enter or a click
runs the selected command, Esc closes it and focus returns to where it was. The palette closes first and the command runs after it has closed, so a command that moves focus (Reply) lands on the page and not on the dialog.
A command runs once. Ctrl+K never opens over a confirmation dialog, and does nothing until the API has said who you are.

| Group | Commands | Who sees them |
| --- | --- | --- |
| Go to | Queue: Unassigned, Mine, Open, Pending, All, Spam; My settings | Every agent |
| Admin | Products, Agents, Tags, Audit, Failed emails | Admins only, and checked again when one is chosen |
| Ticket | Reply to requester, Add internal note, Assign to me (when it is not already yours), Not spam (on a flagged ticket) | While that ticket is on screen and open |

A screen adds its own commands with `CommandRegistry.Register(...)` while it is mounted and disposes the registration when it goes; `CommandRegistry.Available(isAdmin)` is the one place that decides who sees what.
The ticket commands raise the same shortcut action as their key, so the composer, the sidebar and the actions menu run their own code.
''')

edit(ADMIN_DOC, '''## Known limits

- **Pages render twice.**''', '''## Local time, theme, security headers and layout (07c)

**Local time.** Times show in the zone of the browser (`Intl.DateTimeFormat().resolvedOptions().timeZone`, read by `wwwroot/js/tz.js` once per circuit). The prerender, and any circuit whose browser gives no usable zone, shows UTC;
when the zone arrives every time draws again. Relative times ("5 min ago") do not depend on the zone; from a week on the visible text is the date in your zone, and the tooltip shows your local time and the UTC time
("2026-10-04 12:55 Europe/London, 2026-10-04 11:55 UTC"). The `datetime` attribute is always UTC. The zone is not stored: there is no API field and no preference for it (D-042). The Admin image installs `tzdata`; an unknown zone falls back to UTC.

**Theme before first paint.** `wwwroot/js/theme-init.js` is a small blocking classic script in the page head, loaded by `App.razor` and by the sign-in landing page. It reads the same storage key as `preferences.js` (`techstrap.admin.theme`) and sets `data-bs-theme` for Light or Dark before the
first paint, so a stored choice no longer flashes the other theme while the circuit connects. Auto removes the attribute. The script never throws; without storage the page is Auto.

**Content Security Policy and headers.** Admin and Portal send one policy from `TechStrap.Hosting` (`TechStrapCsp.ForBlazorApp`, applied by `UseTechStrapWebHost`), plus `X-Content-Type-Options`, `Referrer-Policy` and `Permissions-Policy`. The policy is `default-src 'self'; script-src 'self'; style-src 'self'; style-src-attr 'unsafe-inline'; img-src 'self' https: data:; connect-src 'self'; font-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self' <the identity provider's origin>`
(the origin comes from the configured authority), so the sign-in and sign-out redirects are allowed. `data:` is there because Bootstrap's compiled CSS draws its icons with data: SVG. In Development `img-src` also allows loopback http (a logo on localhost). There is no `upgrade-insecure-requests`, and `connect-src` has no explicit `ws:` or `wss:`: whether `'self'` covers the circuit's websocket is checked in the owner's browser (the checklist below). Scripts stay strict: there is no inline script, and `<ImportMap />` was removed from both apps (an architecture rule flags it coming back). The Api sends `default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'`, and attachment downloads additionally get `sandbox`. The one relaxation is `style-src-attr`, because the product accent and tag colours are admin data set as `style` attributes (`AccentPreview`, `TagChip`).
If a page loses its styles, a font or sign-in after a change, open the browser console: a blocked resource is named there with the directive. Header tests assert the headers on the Admin pages (including `/signin` and `/error`), the Portal and the Api.

**Responsive behaviour.** From 992 px the rail is a column at the left. Below 992 px it becomes a bar with a Menu button (`aria-expanded`) that opens the links, and choosing a link closes it again. Below 768 px each ticket in the queue is a card (number, status
and time, then the subject, then Product, Requester, Priority and Assignee with their names); between 768 and 992 px the queue drops Product, Requester and Priority. On a phone the reply box follows the conversation and the ticket controls come after it. A wide table
scrolls inside its own region, which is a named, focusable box, never the page. `prefers-reduced-motion` switches animation off, and forced colours keep the current link, tab and row by an outline.

**Manual checklist (owner, before merging 07c).** The tests read the compiled CSS and the markup; they cannot see a layout. With the app signed in (this needs the identity provider, owner action 7) and the browser's developer tools:

1. At 1280 px: the rail is a column, the queue is a table with every column, the palette opens on Ctrl+K, nothing overflows sideways.
2. At 768 px: the rail is a bar with a Menu button; the Menu button opens and closes the links; the queue has no Product, Requester or Priority column; a wide settings table scrolls in its own box and shows a shadow on the edge that has more.
3. At 390 px: each ticket is a card; no horizontal page scroll anywhere (queue, a ticket, Products, Agents, the audit log); the reply box is directly under the last message; the Menu button is at least 44 px square.
4. Keyboard only, from the queue: Ctrl+K, type "mine", Enter; j/k to a ticket, Enter; r, type a reply, Ctrl+Enter; set the status to Solved from the sidebar. Focus is visible everywhere and never lost, Esc closes the palette and the actions menu and returns focus.
5. Chrome DevTools, Rendering: emulate `prefers-color-scheme: dark` and `forced-colors: active` and look at the queue, a ticket and the palette; the selected row, the current rail link and the selected palette line stay distinguishable.
6. Run the axe browser extension on the queue, a ticket, Products and the style guide (`/_styleguide`, Development): no critical findings; record the result in the PR.
7. Open the Admin with the browser console visible and sign in: no Content-Security-Policy violation, the circuit connects (this is the check that `connect-src 'self'` covers the websocket; if it does not, say so in the PR so the policy can name `wss:`), the fonts load, the theme does not flash.

**Compose smoke.** `pwsh scripts/Test-ComposeSmoke.ps1` builds the four images one after another, starts the local compose stack under its own project name (`techstrap-smoke`) on free ports, waits for every healthcheck, checks that the Api and the
Admin answer `/health/ready` with 200 and that the Admin container is healthy, and stops the project (never `down -v`). It needs Docker and runs by hand or from the "Compose smoke" workflow; it is not part of every pull request.

## Known limits

- **Pages render twice.**''')

edit(ADMIN_DOC, '''A read is tried up to three times in all (250 ms base backoff with jitter) on transport errors, timeouts, 408, 502, 503 and 504. There is no circuit breaker: one named read client is shared by every agent, and a breaker opened by one failing endpoint would lock everyone out. Under a real outage every call waits out its retries (roughly a second) before it fails.''',
     '''A read is tried up to three times in all (250 ms base backoff with jitter) on transport errors, timeouts, 408, 502, 503 and 504, and a `Retry-After` header is honoured but capped at 2 seconds a try. There is no circuit breaker: one named read client is shared by every agent, and a breaker opened by one failing endpoint would lock everyone out. Under a real outage every call waits out its retries (a few seconds at most) before it fails.''')

edit(ADMIN_DOC, '''| The theme does not change | The choice is kept in this browser: private windows and cleared site data forget it, and Auto follows the device. |
''', '''| The theme does not change | The choice is kept in this browser: private windows and cleared site data forget it, and Auto follows the device. |
| Times show in UTC | The browser gave no usable time zone, or the page has not finished connecting. Reload; the tooltip always shows the UTC time too. |
| Ctrl+K does nothing | A dialog is open (close it first), the page has not finished connecting, the API has not accepted you yet, or `shortcuts.js` was blocked. The browser's own Ctrl+K (address bar) is stopped only while the page has focus. |
| A banner says "Sign in again" above the page | The API rejected your token in the middle of your work. Copy any unsent text, follow the link, and you return to this page. |
| The page loads unstyled, or sign-in does nothing | Check the browser console for a Content-Security-Policy violation: the directive and the blocked address are named. A proxy that injects scripts or styles will be blocked by design. |
''')

edit(ADMIN_DOC, '''set up `./js/dialog.js`, `./js/shortcuts.js` and `./js/preferences.js` (the `AdminComponentTest` base class does;''',
     '''set up `./js/dialog.js`, `./js/shortcuts.js`, `./js/preferences.js`, `./js/tz.js`, `./js/palette.js` and `./js/menu.js` (the `AdminComponentTest` base class does;''')

edit(ADMIN_DOC, '''times are shown in UTC (the agent's zone needs the browser); "Apply my change again"''',
     '''times are shown in UTC (07c shows them in the browser's zone); "Apply my change again"''')
edit(ADMIN_DOC, '''no presence or live updates (PHASE-10); no command palette (07c); the manual sign-in check''',
     '''no presence or live updates (PHASE-10); no command palette (added in 07c); the manual sign-in check''')

edit(ADMIN_DOC, '''`Program.cs` calls `ConfigureHttpClientDefaults(http => http.RemoveAllLoggers())`, so this is a default for every client the factory creates''',
     '''Every host (Api, Worker, Admin and Portal) removes the logging handlers through one shared registration in `TechStrap.Hosting` (`AddTechStrapHttpClientDefaults`, 07c, D-042), so this is a default for every client the factory creates''')

edit(ADMIN_DOC, '''and an OTLP exporter's `x-api-key` the same way.''', '''and the factory's own default leaks header values in its structured state. (07b also said an OTLP exporter's `x-api-key` reached the log through the factory; 07c could not reproduce that, and the OTLP leak tests remain as an end-to-end guard.)''')
edit(ADMIN_DOC, '''(the two API clients, the OTLP exporters' clients and any future one)''', '''(the two API clients and any future one)''')

edit(ADMIN_DOC, '''- Times are shown in UTC, as in 07a.
- The OpenAPI bearer scheme, the CSP and the responsive and accessibility pass remain 07c.
''', '''- Times were shown in UTC, as in 07a, until 07c.
- The OpenAPI security schemes, the CSP and the responsive and accessibility pass were done in 07c.

## Known gaps in 07c

Recorded in D-042 and tracked for later phases:

- Authentik is not set up (owner action 7), so P07-T02 stays open, and the checks that need a signed-in session have not been run against a real provider: the keyboard walk, the axe run, the CSP check of the sign-in and sign-out redirects. The checklist above is the owner's.
- The queue's search text stays in the address (so a view can be bookmarked). It is masked in Sentry (`search` and `q`), but it is in the browser history and in the reverse proxy's access log.
- The time zone comes from the browser only. A second device or browser shows its own zone, and there is no way to choose another one.
- The palette has navigation and the four ticket commands. "Open a ticket by number", "Cycle theme" and the other commands of the UX brief are not built.
- The Compose smoke is by hand (or the manual workflow), not on every pull request, because it builds four images.
''')

# -------------------------------------------------------------------------------------------------------------------------------- PHASE-07
edit(PHASE, '''The manual sign-in against a real provider is still open (Authentik is not set up; docs/development/ADMIN-APP.md)''',
     '''The manual sign-in against a real provider is still open (Authentik is not set up; docs/development/ADMIN-APP.md). **07c:** still open, for the same reason (owner action 7); everything that can be tested without a provider is, and 07c adds the CSP check of the sign-in and sign-out redirects to the owner's checklist''')

edit(PHASE, '''- [ ] **P07-T05** [07a] Implement typed clients''', '''- [x] **P07-T05** [07a] Implement typed clients''')
edit(PHASE, '''`IAdminEventsClient` is in the app (07b)''', '''`IAdminEventsClient` is in the app (07b). **07c evidence:** a 401 on any call reaches `AgentSession` from `ApiConnection`, and the read retry's `Retry-After` is capped at 2 s (the session and resilience tests of 07c)''')
edit(PHASE, '''- [ ] **P07-T06** [07a] Implement `ITicketsClient`''', '''- [x] **P07-T06** [07a] Implement `ITicketsClient`''')
edit(PHASE, '''and `IDeadLettersClient` is in the app (07b)''', '''and `IDeadLettersClient` is in the app (07b). The unchecked box was only the wording about `IAttachmentsClient`, which D-040 removed''')

edit(PHASE, '''- [ ] **P07-T19** [07c] Apply BRAND.md tokens/SCSS''', '''- [x] **P07-T19** [07c] Apply BRAND.md tokens/SCSS''')
edit(PHASE, '''The keyboard walk-through deferred from PHASE-02 happens here.
- [ ] **P07-T20**''', '''The keyboard walk-through deferred from PHASE-02 happens here.
  - **07c evidence:** `ResponsiveStyleTests` (rail fold at 992 px, queue cards at 768 px, scroll regions, composer order, forced colours, reduced motion), `RailToggleTests`, `RailLinkTests` (`aria-current`), `ScrollRegionSiteTests`, `CommandPaletteTests`, `TicketActionsMenuTests`, `HeadingHostTests` (one `h1` on the 404 and the sign-in page), `TicketPaletteCommandsTests`, the node tests `shortcuts`, `palette` and `menu`, and a headless-browser render of the queue and the rail at 1280, 800 and 390 px during development. **Still the owner's:** the checklist in docs/development/ADMIN-APP.md, the keyboard-only run through queue, reply and solve, and the axe run with its result in the PR; all need a signed-in session, so they wait for owner action 7. The `.text-{color}` carry-over: a search of the Admin markup and SCSS finds no use of them (nothing guards it).
- [x] **P07-T20**''')
edit(PHASE, '''- [x] **P07-T20** [07c] Add Admin to compose and verify Dockerfile run; add admin architecture rules (no reference to Application/Infrastructure/EF; no `HttpClient` use in `.razor` files)
  - **Depends on:** P07-T02
  - **Validation:** `docker compose up` -> `/health/ready` 200; Architecture.Tests fail when a forbidden reference or `[Inject] HttpClient` in a component is introduced (verified by a deliberate failing sample).''',
     '''- [x] **P07-T20** [07c] Add Admin to compose and verify Dockerfile run; add admin architecture rules (no reference to Application/Infrastructure/EF; no `HttpClient` use in `.razor` files)
  - **Depends on:** P07-T02
  - **Validation:** `docker compose up` -> `/health/ready` 200; Architecture.Tests fail when a forbidden reference or `[Inject] HttpClient` in a component is introduced (verified by a deliberate failing sample).
  - **07c evidence:** `scripts/Test-ComposeSmoke.ps1` (own project name and free ports, images built one by one, `up -d --wait`, Api and Admin `/health/ready` 200, Admin container healthy, `down` without `-v`; run result in the PR) and its Pester tests; the Admin architecture rules in `tests/TechStrap.Architecture.Tests`, each with a deliberate failing sample that proves it bites (package allowlist, no `HttpClient` in components, the `[ExcludeFromInteractiveRouting]` allowlist, no inline script or style); `Dockerfile.admin` installs `tzdata`.''')

edit(PHASE, '''- [ ] [07c] Carried forward from the PHASE-07b final review: `AccentPreview` styles itself''', '''- [x] [07c] Carried forward from the PHASE-07b final review: `AccentPreview` styles itself''')
edit(PHASE, '''instead of `style-src 'self'` alone.
- [ ] Carried forward from the PHASE-04 final review: Add an OpenAPI bearer security scheme so generated clients know the endpoints need a token (also needed by PHASE-11).''',
     '''instead of `style-src 'self'` alone. (done in PHASE-07c, D-042: `style-src 'self'` plus `style-src-attr 'unsafe-inline'`)
- [x] Carried forward from the PHASE-04 final review: Add an OpenAPI bearer security scheme so generated clients know the endpoints need a token (also needed by PHASE-11). (done in PHASE-07c, D-042: Bearer, ApiKey and TicketToken schemes, and a requirement on every operation that is not public)''')

# ------------------------------------------------------------------------------------------------------------------------ roadmap and index
status_old = '07a merged (PR #9); 07b implemented (pending merge); 07c not started'
status_new = '07 complete (pending merge): 07a merged (PR #9), 07b merged (PR #10), 07c implemented; owner action 7 (Authentik) still open, so P07-T02 stays unticked'
edit(ROADMAP, status_old, status_new)
edit(INDEX, status_old, status_new)
edit(ROADMAP, '''| Admin/Portal security headers with a Blazor-aware CSP; extract the shared Admin/Portal host wiring | PHASE-07 / PHASE-09 |''',
     '''| Admin/Portal security headers with a Blazor-aware CSP; extract the shared Admin/Portal host wiring | PHASE-07 / PHASE-09 (done in PHASE-07c, D-042) |''')

# --------------------------------------------------------------------------------------------------------------------------- decision log
edit(LOG, '''- **Owner decision (2026-10-02, PHASE-02):** D-023 (visual direction)''', '''- **Owner decision (2026-10-04, PHASE-07c planning):** D-042, the owner decisions on the CSP, the theme flash, the time zone source, the shared host wiring and the single PR. Its defaults were proposed in the PHASE-07c plan and approved when the owner approved the plan.
- **Owner decision (2026-10-02, PHASE-02):** D-023 (visual direction)''')
edit(LOG, '''| D-041 | PHASE-07b: roles are read-only in the Admin; the product logo is a validated URL; the tag list shows ticket counts; Admin guard, browser preferences and client decisions | Approved (owner 2026-10-04; technical decisions at PHASE-07b plan review) | 2026-10-04 | PHASE-07, PHASE-08, PHASE-12 |
''', '''| D-041 | PHASE-07b: roles are read-only in the Admin; the product logo is a validated URL; the tag list shows ticket counts; Admin guard, browser preferences and client decisions | Approved (owner 2026-10-04; technical decisions at PHASE-07b plan review) | 2026-10-04 | PHASE-07, PHASE-08, PHASE-12 |
| D-042 | PHASE-07c: CSP with `style-src-attr`, theme init script, browser time zone, shared host wiring, command palette, responsive rail, session-expired banner, Sentry search scrub, OpenAPI security schemes | Approved (owner 2026-10-04; defaults at PHASE-07c plan review) | 2026-10-04 | PHASE-07, PHASE-09, PHASE-11, PHASE-12 |
''')

d042 = '''

---

## D-042: PHASE-07c: the CSP, the theme script, local time, shared host wiring, the command palette and the responsive Admin

- **Status:** Approved (owner 2026-10-04; defaults at PHASE-07c plan review)
- **Date:** 2026-10-04
- **Owner:** Jon Seeley
- **Related artifacts:** D-017, D-024, D-034, D-040, D-041, PHASE-07, UX-BRIEF-admin, `docs/development/ADMIN-APP.md`, `docs/superpowers/plans/2026-10-04-phase-07c-admin-polish.md`

### Context
PHASE-07a and 07b are merged. 07c is the polish pass (T19), the compose and architecture checks (T20) and the items D-040 and D-041 moved to it: the CSP, the shared host wiring, the OpenAPI security scheme, the command palette, local time and the theme flash.
Reading the code for the plan found these facts:
- **Headers.** Only the Api sends security headers, and only the package defaults, whose CSP has no `default-src`, `script-src` or `style-src`. The Admin and the Portal send none. The Admin uses `style` attributes for the product accent and tag colours (`AccentPreview`, `TagChip`), and `<ImportMap />` renders an inline script (it is removed in 07c).
- **The sign-in redirects.** Chrome applies `form-action` to the redirect that follows a form submission, so the sign-in and sign-out forms, which answer with a 302 to the identity provider, need the provider's origin in `form-action`. Firefox does not enforce it, so a Firefox-only check would miss it.
- **Sessions.** Only the first `GET /api/agents/me` could move the app to "session expired". A 401 on any later call showed a generic error string and left the shell looking signed in.
- **Logging.** Only the Admin removed the `HttpClient` factory's logging handlers, which write each request header (`Authorization`, an OTLP `x-api-key`) at Trace. The Api, Worker and Portal use the same factory. The 07b claim that the OTLP exporter goes through it was not reproduced in 07c; what was found is that the factory's own logging default leaks header values in its structured state, and the OTLP leak tests remain as an end-to-end guard with a positive control (a TCP listener that sees the export attempt).
- **Search.** The queue keeps its search text in the address and sends it to the API as `?search=`. Sentry scrubbed request headers only.
- **Time.** Every time is formatted in UTC. The API stores `timestamptz`, the agent profile has no zone, and the browser is the only source.

### Decision
**Owner decisions (2026-10-04)**
- **CSP.** `script-src 'self'` stays strict. `style-src 'self'` plus `style-src-attr 'unsafe-inline'`: the colours are admin data, so classes cannot carry them, and a nonce does not cover attributes.
- **Theme flash.** An external blocking script, `wwwroot/js/theme-init.js`, in the page head, loaded before the stylesheets.
- **Time zone.** From the browser (`Intl`) through a JS module, per circuit. The prerender shows UTC. No API change.
- **Host wiring.** A shared helper in `TechStrap.Hosting` is adopted by the Admin and the Portal, and the Portal gains a reference to Hosting. The `ConfigureHttpClientDefaults(b => b.RemoveAllLoggers())` default goes into all four hosts.
- **One PR.**

**Defaults (proposed in the plan, approved with it)**
- **Queue search stays in the URL** and is masked in Sentry: a `SensitiveQuerySentryProcessor` in `TechStrap.Hosting` replaces the value of `search` and `q` with `[redacted]` in the request query string and URL of events and transactions, in span descriptions and data, and in breadcrumbs (through the `BeforeBreadcrumb` hook). It is registered with the header scrubber, so every host that has one gets it.
- **`Retry-After` is honoured but capped at 2 seconds** on the read client.
- **OpenAPI documents three schemes**: `Bearer` (HTTP bearer, JWT) for the Agent and Admin policies, `ApiKey` (header `X-Api-Key`) for intake, and `TicketToken` (header `X-Ticket-Token`) for the customer routes. A document and an operation transformer add them; public operations name none. It is documentation only.
- **A 401 in the middle of a session** moves `AgentSession` to "session expired" from one place (`ApiConnection`). First load: the full page. Mid-session: a banner with "Sign in again" (a link to `/signin/start` for the current page), and the page stays mounted so an unsent draft survives.

**Technical decisions made in the plan**
- **CSP details.** The directives are `default-src 'self'; script-src 'self'; style-src 'self'; style-src-attr 'unsafe-inline'; img-src 'self' https: data:; connect-src 'self'; font-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self' <the OIDC authority origin>`. `img-src` allows `https:` because product logos are `https` addresses chosen by an admin, loopback http only in Development, and `data:` because Bootstrap's compiled CSS uses data: SVG icons. There is no `upgrade-insecure-requests`. `connect-src` has no explicit `ws:` or `wss:`; that `'self'` covers the circuit's websocket is an owner browser check. `<ImportMap />` is removed from both apps (an architecture rule flags it). One builder in `TechStrap.Hosting` (`TechStrapCsp.ForBlazorApp`) serves the Admin and the Portal; the Api uses `TechStrapCsp.ForApi` (`default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'`), and attachment downloads also get `sandbox`.
- **Shared host wiring** (`TechStrap.Hosting.Wiring`). `AddTechStrapHttpClientDefaults` (the `RemoveAllLoggers` default) applies in all four hosts; `AddTechStrapWebHost`, `UseTechStrapWebHost` and `UseTechStrapErrorPages` apply to the Admin and the Portal; the Portal gets `AddTechStrapObservability`, which calls `AddSensitiveHeaderScrubbing`. The Portal references exactly Contracts and Hosting. `FactoryClientLeakTests` covers all four hosts.
- **Session resilience.** `SessionExpiry` (scoped) receives `Report()` from `ApiConnection` on every 401. `AgentSession` gains `ExpiredWhileWorking`, `IsAdmitted` and `MarkUnavailable()`; `IsAdmin` follows `IsAdmitted`, so it stays true while the session is expired mid-session, and a 401 during a reload keeps the agent. The `SessionExpiredBanner` (`role="alert"`) links to `/signin/start?returnUrl=<current>`. The read retry's cap is `ApiClientRegistration.ReadRetryAfterCap` (2 s).
- **Command palette.** Ctrl+K or Cmd+K, a chord rather than a single key, so the My settings switch does not turn it off (WCAG 2.1.4). It is a native `dialog` with a combobox and a listbox. Commands come from a scoped `CommandRegistry`: built-in navigation (the six queue views, My settings), admin pages (admin only, checked when listed and again when chosen), and commands a screen registers while it is mounted (the ticket page: reply, internal note, assign to me, not spam). Ticket commands raise the same shortcut action as their key. The palette closes before a command runs, runs it once, and never opens over a confirmation dialog or before the session is known.
- **Local time.** `LocalTimeService` (scoped) reads the zone once after the first interactive render and resolves it with `TimeZoneInfo.FindSystemTimeZoneById`; an unknown or missing zone is UTC. `RelativeTime` shows relative text (zone independent), the date in the zone from a week on, a tooltip with the local and the UTC time, and a UTC `datetime` attribute. The Admin image installs `tzdata`.
- **Responsive layout.** The rail folds behind a Menu button below 992 px (`aria-expanded`, closes on navigation). The queue drops three columns from 768 to 992 px and becomes cards below 768 px. Tables scroll in a named, focusable `ScrollRegion`. On a phone the composer follows the conversation. `prefers-reduced-motion` and `forced-colors` rules are tested against the compiled CSS. Breakpoints were chosen to match Bootstrap's `lg` and `md`.
- **Menu accessibility.** The ticket actions menu follows the menu button pattern (roles, arrow keys, Home and End, Escape returns focus, Tab closes). The rail links carry `aria-current="page"`. `BrandWindow` takes a heading level, so the 404 and the sign-in page have an `h1`.
- **07b minors.** An uncertain write is held until a read that started after it finishes (`UncertainMarks`, a load id stored with each mark); the audit page draws its events without waiting for the agent list; retyping the committed display name after an uncertain save says to reload; a product saved with a relative logo before the logo rule can be edited when the logo is left alone (`ProductBranding.CreateForUpdate` skips the logo rule for an unchanged stored value; the Admin editor mirrors it).

### Alternatives Considered
- **A strict `style-src 'self'` with the colours set from script** (CSSOM). Rejected: the prerender and a page without script would have no colours, and it is more code for the same protection of a validated hex value.
- **A per-request nonce or hash for inline script.** Rejected: there is no inline script to cover once `theme-init.js` is external, and a nonce does not cover style attributes.
- **A theme cookie rendered on `<html>`.** Rejected: a second source of truth and a cookie on every request.
- **Taking the time zone from the API or a stored preference.** Rejected: it needs an API change and a migration (the Admin adds no entry points, D-040), and a stored zone goes stale when an agent travels.
- **Taking the queue search out of the address.** Rejected: it loses bookmarks and back and forward for searches and reverses a recorded assumption; the masking in Sentry covers the exposure that mattered.
- **Ignoring `Retry-After`.** Rejected: a polite client waits a little when the API asks it to. Honouring it uncapped could stall a page for the whole client timeout.
- **Tearing the page down on a mid-session 401.** Rejected: it loses unsent replies and notes.
- **Per-page 401 handling.** Rejected: every page would need it, and one would be forgotten.

### Consequences
- **Superseded wording.** The D-040 and D-041 lines that moved the palette, local time, the theme flash, the CSP and the OpenAPI scheme to 07c are now done. ADMIN-APP.md describes them. The PHASE-07 package-table lines about "401 flips the session" and "CSP asserted in a host test" are now true.
- **The CSP relaxes one thing**, `style-src-attr 'unsafe-inline'`, so an injected `style` attribute would run. It cannot run script, and every value the Admin writes into a `style` attribute is a validated hex colour.
- **The Portal depends on Hosting** (the architecture tests allow it) and gets the PII enricher, the Sentry scrubbers and the headers early, before PHASE-09 builds on it.
- **The compose smoke is opt-in**, because it builds four images.
- **Still open:** the manual sign-in against a real identity provider (P07-T02, owner action 7), and with it the keyboard walk, the axe run and the CSP check of the redirects. The queue search is still in the browser history and in the proxy's access log.

### Approval
- **Approved by:** Jon Seeley (owner, PHASE-07c planning)
- **Approved on:** 2026-10-04
'''
# append at the end of the file
import os
path = os.path.join(ROOT, LOG)
text = open(path, encoding='utf-8', newline='').read()
nl = '\r\n' if '\r\n' in text else '\n'
assert text.rstrip().endswith('- **Approved on:** 2026-10-04')
open(path, 'w', encoding='utf-8', newline='').write(text.rstrip() + d042.replace('\n', nl))
```

Then read the result:

```bash
git diff --stat docs
git diff docs/architecture/PHASE-07-admin-app.md
```

Expected: five files changed; in `PHASE-07-admin-app.md` T05, T06, T19 and T20 are `[x]` with a 07c evidence line, **T02 is still `[ ]`** with the 07c note, and the two carry-forwards are `[x]`; the roadmap row and the index row say "07 complete (pending merge)" and name owner action 7. `D-042` appears in the decision log header, its index and as the last section.

- [ ] **Step 7: Final verification**

Run, in order:
- `dotnet build TechStrap.slnx -c Release` (0 warnings)
- `dotnet test --solution TechStrap.CI.slnf -c Release`
- `pwsh -File scripts/Invoke-ScriptTests.ps1`
- the EF pending-model check (no migration is expected in PHASE-07c)
- `git status --short` shows no `harness` folder, no `HarnessDumpTemp.cs` and nothing under `.superpowers/`

Expected: all PASS. In the scratch copy, with Tasks 6 to 10 only: 3654 tests in `TechStrap.CI.slnf` (07b ended at 3460) and 149 script tests (07b: 134). The count of the real branch is higher by whatever Tasks 1 to 5 add.

- [ ] **Step 8: Commit**

```bash
git add .github/workflows/ci.yml \
  docs/architecture/00-DISCOVERY-INDEX.md \
  docs/architecture/04-DECISION-LOG.md \
  docs/architecture/99-IMPLEMENTATION-ROADMAP.md \
  docs/architecture/PHASE-07-admin-app.md \
  docs/development/ADMIN-APP.md \
  scripts/Test-ComposeSmoke.ps1 \
  scripts/tests/ComposeFiles.Tests.ps1 \
  scripts/tests/ComposeSmoke.Tests.ps1
git diff --cached --stat
git commit -m "feat: compose smoke, D-042 and the closing PHASE-07 docs" -m "A manual smoke starts the local stack under its own project name on free ports and checks the Api and the Admin, without removing volumes. D-042 records the phase; ADMIN-APP.md, PHASE-07, the roadmap and the index are brought up to date, with T02 still open on Authentik." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```
