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
    [GeneratedRegex(@"\bAddInteractive\w+|@rendermode\b|\brendermode\s*=|\bRenderMode\s*\.\s*Interactive\w*|\bInteractive(?:Server|WebAssembly|Auto)(?:RenderMode)?\b|\bIComponentRenderMode\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
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
