using System.Text;
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
    /// The pages that may carry [ExcludeFromInteractiveRouting], by name and by path (without extension) relative to src/TechStrap.Admin. AgentGate
    /// treats such a page as having no circuit and no session, so it renders its content without asking the API who the agent is (no NoAccess check).
    /// A data-bearing page given the attribute would skip that gate, and so would a same-named page in another folder, so the path is pinned.
    /// </summary>
    public static IReadOnlyDictionary<string, string> StaticPagePaths { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Error"] = "Components/Pages/Error",
        ["NotFound"] = "Components/Pages/NotFound",
        ["StyleGuide"] = "Components/Pages/StyleGuide",
    };

    public static IReadOnlySet<string> StaticPages { get; } = new HashSet<string>(StaticPagePaths.Keys, StringComparer.Ordinal);

    [GeneratedRegex(@"\bI?HttpClient(Factory)?\b", RegexOptions.CultureInvariant)]
    private static partial Regex HttpClientUse();

    [GeneratedRegex(@"@attribute[ \t]*\[|^[ \t]*\[", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex AttributeListStart();

    [GeneratedRegex(@"^(?:\s|\[[^\]]*\])*(?:(?:public|internal|sealed|partial|abstract|static)\s+)*(?:class|record|struct)\s+(?<name>\w+)", RegexOptions.CultureInvariant)]
    private static partial Regex ClassDeclaration();

    [GeneratedRegex(@"<!--.*?-->|@\*.*?\*@|/\*.*?\*/", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex Comments();

    /// <summary>
    /// An element that carries code or style in the page: a script without a src attribute, or any style element. The tag name must be exactly script
    /// or style (any case except PascalCase, which Razor reads as a component) followed by whitespace, "/" or ">", so script-x and Style are not hits.
    /// </summary>
    [GeneratedRegex(@"<(?!(?-i:Script|Style)[\s/>])(?:style(?=[\s/>])|script(?=[\s/>])(?![^>]*(?<![\w:.@-])src\s*=))", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
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
            .Where(f => f.Path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) && InlineScriptOrStyle().IsMatch(Comments().Replace(f.Text, " ")))
            .Select(f => $"{f.Path} has an inline <script> or a <style> element. The CSP allows neither; use wwwroot/js modules and Styles/_*.scss.")];

    public static IReadOnlyList<string> StaticPageViolations(IEnumerable<(string Path, string Text)> files)
    {
        var violations = new List<string>();
        var excluded = files
            .Where(f => IsSourceFile(f.Path))
            .Select(f => (Path: f.Path.Replace('\\', '/'), f.Text))
            .GroupBy(f => ComponentKey(f.Path), StringComparer.Ordinal)
            .Select(group => (Key: group.Key, Excluded: group.Any(f => HasExclude(f.Text)), Anonymous: group.Any(f => HasAllowAnonymous(f.Path, f.Text))))
            .Where(component => component.Excluded)
            .OrderBy(component => component.Key, StringComparer.Ordinal)
            .ToList();

        var expectedKeys = StaticPagePaths.Values.Select(relative => $"src/{ReferenceRules.Admin}/{relative}").ToHashSet(StringComparer.Ordinal);
        foreach (var (key, _, anonymous) in excluded)
        {
            if (!expectedKeys.Contains(key))
            {
                violations.Add($"{key} is [ExcludeFromInteractiveRouting] but is not in AdminRules.StaticPages at that path. A static page skips the agent gate.");
            }

            if (!anonymous)
            {
                violations.Add($"{key} is [ExcludeFromInteractiveRouting] but is not [AllowAnonymous]. A static page must hold no agent data.");
            }
        }

        var found = excluded.Select(component => component.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var (name, relative) in StaticPagePaths.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!found.Contains($"src/{ReferenceRules.Admin}/{relative}"))
            {
                violations.Add($"{name} is in AdminRules.StaticPages but no page at {relative} is [ExcludeFromInteractiveRouting]. Remove it from the list or fix the path.");
            }
        }

        return violations;
    }

    /// <summary>Every .razor, .razor.cs and .cs file under src/TechStrap.Admin, except bin, obj and node_modules folders inside the Admin project.</summary>
    public static IEnumerable<(string Path, string Text)> ComponentSources(string repositoryRoot)
    {
        var skipped = new HashSet<string>(["bin", "obj", "node_modules"], StringComparer.OrdinalIgnoreCase);
        var admin = Path.Combine(repositoryRoot, "src", ReferenceRules.Admin);
        return Directory.EnumerateFiles(admin, "*", SearchOption.AllDirectories)
            .Select(f => (Full: f, Relative: Path.GetRelativePath(repositoryRoot, f)))
            .Where(f => IsSourceFile(f.Relative) && !f.Relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[..^1].Any(skipped.Contains))
            .Select(f => (f.Relative, File.ReadAllText(f.Full)));
    }

    private static bool IsComponentFile(string path) =>
        path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase);

    private static bool IsSourceFile(string path) => IsComponentFile(path) || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    private static string ComponentKey(string path) =>
        path.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase) ? path[..^".razor.cs".Length]
        : path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) ? path[..^".razor".Length]
        : path[..^".cs".Length];

    /// <summary>True when any attribute list (an @attribute directive or a line-leading [..] list) names ExcludeFromInteractiveRouting in any spelling.</summary>
    private static bool HasExclude(string text) =>
        AttributeLists(Comments().Replace(text, " ")).Any(list => list.Names.Any(name => Simple(name) == "ExcludeFromInteractiveRouting"));

    /// <summary>
    /// True when AllowAnonymous sits where the page's attributes sit: an @attribute directive, or the attribute list directly above the page class in a
    /// .cs file. A [AllowAnonymous] on a nested type in @code or in a class body is not a hit.
    /// </summary>
    private static bool HasAllowAnonymous(string path, string text)
    {
        var clean = Comments().Replace(text, " ");
        var isCode = path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
        var stem = Path.GetFileName(path).Split('.')[0];
        return AttributeLists(clean).Any(list =>
            list.Names.Any(IsAllowAnonymous)
            && (isCode
                ? !list.IsDirective && ClassDeclaration().Match(clean[list.End..]) is { Success: true } declaration && declaration.Groups["name"].Value == stem
                : list.IsDirective));
    }

    private static bool IsAllowAnonymous(string name) =>
        name is "AllowAnonymous" or "AllowAnonymousAttribute"
            or "Microsoft.AspNetCore.Authorization.AllowAnonymous" or "Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute";

    /// <summary>The attribute name without a namespace qualifier or the Attribute suffix.</summary>
    private static string Simple(string name)
    {
        var simple = name[(name.LastIndexOf('.') + 1)..];
        return simple.EndsWith("Attribute", StringComparison.Ordinal) ? simple[..^"Attribute".Length] : simple;
    }

    private static IEnumerable<(IReadOnlyList<string> Names, bool IsDirective, int End)> AttributeLists(string text)
    {
        var consumed = 0;
        foreach (Match start in AttributeListStart().Matches(text))
        {
            if (start.Index < consumed)
            {
                continue;
            }

            var open = start.Index + start.Length - 1;
            var close = MatchingBracket(text, open);
            if (close < 0)
            {
                yield break;
            }

            consumed = close + 1;
            yield return (SplitAttributes(text[(open + 1)..close]), start.Value.StartsWith("@attribute", StringComparison.Ordinal), consumed);
        }
    }

    private static int MatchingBracket(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '"':
                    i = SkipString(text, i);
                    break;
                case '[':
                    depth++;
                    break;
                case ']':
                    if (--depth == 0)
                    {
                        return i;
                    }

                    break;
            }
        }

        return -1;
    }

    private static int SkipString(string text, int quote)
    {
        for (var i = quote + 1; i < text.Length; i++)
        {
            if (text[i] == '\\')
            {
                i++;
            }
            else if (text[i] == '"')
            {
                return i;
            }
        }

        return text.Length;
    }

    private static List<string> SplitAttributes(string body)
    {
        var names = new List<string>();
        var item = new StringBuilder();
        var depth = 0;
        for (var i = 0; i <= body.Length; i++)
        {
            var c = i < body.Length ? body[i] : ',';
            if (c == '"')
            {
                var end = SkipString(body, i);
                item.Append(body, i, Math.Min(end + 1, body.Length) - i);
                i = end;
                continue;
            }

            if (c is '(' or '[')
            {
                depth++;
            }
            else if (c is ')' or ']')
            {
                depth--;
            }

            if (c == ',' && depth == 0)
            {
                var name = item.ToString().Trim();
                var target = name.IndexOf(':', StringComparison.Ordinal);
                if (target > 0 && name[..target].All(char.IsLetter))
                {
                    name = name[(target + 1)..].Trim();
                }

                var paren = name.IndexOf('(', StringComparison.Ordinal);
                names.Add((paren >= 0 ? name[..paren] : name).Trim());
                item.Clear();
            }
            else
            {
                item.Append(c);
            }
        }

        return names;
    }
}
