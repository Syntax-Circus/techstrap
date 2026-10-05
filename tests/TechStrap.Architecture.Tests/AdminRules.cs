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
