using System.Text;
using System.Text.RegularExpressions;

namespace TechStrap.Architecture.Tests;

/// <summary>
/// Rules for the Admin app (PHASE-07 T20). The Admin is a thin client of the API: no data access, no direct HTTP in a component, and a short,
/// reviewed list of pages that render without a circuit. Every rule is a pure function over text or a parsed project so the tests can feed it a
/// deliberately bad sample and prove it fails. The text rules fail closed: a file they cannot parse is a violation, never a skip. The authoritative
/// static-page check is the reflection test in TechStrap.Admin.Tests (StaticPageReflectionTests); this text rule is the early, readable gate.
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

    /// <summary>The attribute name in an attribute list: after "[", "," or a target colon. Matches any spelling, on any line, in any list.</summary>
    [GeneratedRegex(@"[\[,:]\s*(?:\w+\s*\.\s*)*ExcludeFromInteractiveRouting(?:Attribute)?\b", RegexOptions.CultureInvariant)]
    private static partial Regex ExcludeToken();

    [GeneratedRegex(@"@attribute[ \t]*(?=\[)", RegexOptions.CultureInvariant)]
    private static partial Regex AttributeDirective();

    [GeneratedRegex(@"\G\s*(?:(?:public|internal|protected|private|sealed|partial|abstract|static)\s+)*(?:class|record|struct)\s+(?<name>\w+)", RegexOptions.CultureInvariant)]
    private static partial Regex ClassDeclaration();

    [GeneratedRegex(@"<!--.*?-->|@\*.*?\*@", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex RazorComments();

    [GeneratedRegex(@"^[ \t]*//.*$", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex WholeLineSlashComments();

    /// <summary>
    /// The start of an element that carries code or style: the tag name must be exactly script or style (any case except PascalCase, which Razor reads
    /// as a component) followed by whitespace, "/" or ">", so script-x and Style are not hits. A script is inline unless it has its own src attribute.
    /// </summary>
    [GeneratedRegex(@"<(?!(?-i:Script|Style)[\s/>])(?<name>style|script)(?=[\s/>])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex InlineTagStart();

    public static IReadOnlyList<string> PackageViolations(ProjectNode admin) =>
        [.. admin.PackageReferences
            .Where(package => !AllowedPackages.Contains(package))
            .Order(StringComparer.Ordinal)
            .Select(package => $"{admin.Name} must not reference package {package}. The Admin talks to the API only; add the package to AdminRules.AllowedPackages if it is a reviewed choice.")];

    public static IReadOnlyList<string> HttpClientViolations(IEnumerable<(string Path, string Text)> files) =>
        [.. files
            .Where(f => IsComponentFile(f.Path) && HttpClientUse().IsMatch(f.Text))
            .Select(f => $"{f.Path} uses HttpClient or IHttpClientFactory. Components call the API only through a typed client from Clients/.")];

    public static IReadOnlyList<string> InlineMarkupViolations(IEnumerable<(string Path, string Text)> files)
    {
        var violations = new List<string>();
        foreach (var (path, text) in files.Where(f => f.Path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)))
        {
            if (!TryStripRazorComments(text, out var clean, out var error))
            {
                violations.Add($"could not parse {path}: {error}");
            }
            else if (HasInlineElement(clean))
            {
                violations.Add($"{path} has an inline <script> or a <style> element. The CSP allows neither; use wwwroot/js modules and Styles/_*.scss.");
            }
        }

        return violations;
    }

    public static IReadOnlyList<string> StaticPageViolations(IEnumerable<(string Path, string Text)> files)
    {
        var violations = new List<string>();
        var analysed = new List<(string Key, bool Excluded, bool Anonymous)>();
        foreach (var group in files
                     .Where(f => IsSourceFile(f.Path))
                     .Select(f => (Path: f.Path.Replace('\\', '/'), f.Text))
                     .GroupBy(f => ComponentKey(f.Path), StringComparer.Ordinal)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var excluded = false;
            var anonymous = false;
            foreach (var (path, text) in group)
            {
                var result = Analyse(path, text);
                excluded |= result.Excluded;
                anonymous |= result.Anonymous;
                if (result.Error is not null)
                {
                    violations.Add($"could not parse {path}: {result.Error}");
                }
            }

            if (excluded)
            {
                analysed.Add((group.Key, excluded, anonymous));
            }
        }

        var expectedKeys = StaticPagePaths.Values.Select(relative => $"src/{ReferenceRules.Admin}/{relative}").ToHashSet(StringComparer.Ordinal);
        foreach (var (key, _, anonymous) in analysed)
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

        var found = analysed.Select(component => component.Key).ToHashSet(StringComparer.Ordinal);
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

    // ---- Static-page analysis ---------------------------------------------------------------------------------------------------------------

    private readonly record struct FileAnalysis(bool Excluded, bool Anonymous, string? Error);

    private readonly record struct AttributeList(string? Target, IReadOnlyList<string> Names);

    /// <summary>
    /// Excluded: the ExcludeFromInteractiveRouting name appears in any attribute list (a token search after comments are removed, so spelling,
    /// line breaks and same-line lists cannot hide it). Anonymous: AllowAnonymous sits where the page's own attributes sit: an @attribute directive
    /// in a .razor file, or an attribute list directly above the class named like the file in a .cs file, with no target other than "type".
    /// A file that cannot be parsed reports an error and is treated as excluded if the raw text names the attribute, never as clean.
    /// </summary>
    private static FileAnalysis Analyse(string path, string text) =>
        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? AnalyseCode(path, text) : AnalyseRazor(text);

    private static FileAnalysis AnalyseCode(string path, string text)
    {
        if (!TryMaskCSharp(text, out var masked, out var error))
        {
            return new FileAnalysis(ExcludeToken().IsMatch(text), false, error);
        }

        var lists = new List<(int Start, int End, AttributeList List)>();
        for (var i = 0; i < masked.Length; i++)
        {
            if (masked[i] != '[')
            {
                continue;
            }

            var close = MatchingBracket(masked, i);
            if (close < 0)
            {
                return new FileAnalysis(ExcludeToken().IsMatch(masked), false, "unbalanced bracket");
            }

            lists.Add((i, close + 1, ParseList(masked[(i + 1)..close])));
            i = close;
        }

        var stem = Path.GetFileName(path).Split('.')[0];
        var anonymous = false;
        for (var i = 0; i < lists.Count && !anonymous; i++)
        {
            var runEnd = lists[i].End;
            var last = i;
            while (last + 1 < lists.Count && string.IsNullOrWhiteSpace(masked[runEnd..lists[last + 1].Start]))
            {
                last++;
                runEnd = lists[last].End;
            }

            var declaration = ClassDeclaration().Match(masked, runEnd);
            if (declaration.Success && declaration.Groups["name"].Value == stem)
            {
                anonymous = lists.Skip(i).Take(last - i + 1).Any(l => OnTheType(l.List) && l.List.Names.Any(IsAllowAnonymous));
            }

            i = last;
        }

        return new FileAnalysis(ExcludeToken().IsMatch(masked), anonymous, null);
    }

    private static FileAnalysis AnalyseRazor(string text)
    {
        if (!TryStripRazorComments(text, out var clean, out var error))
        {
            return new FileAnalysis(ExcludeToken().IsMatch(text), false, error);
        }

        clean = WholeLineSlashComments().Replace(clean, string.Empty);
        var anonymous = false;
        foreach (Match directive in AttributeDirective().Matches(clean))
        {
            var open = directive.Index + directive.Length;
            var lineEnd = clean.IndexOf('\n', open);
            var line = lineEnd < 0 ? clean[open..] : clean[open..lineEnd];
            if (!TryMaskCSharp(line, out var masked, out var lineError))
            {
                return new FileAnalysis(ExcludeToken().IsMatch(clean), false, lineError);
            }

            var close = MatchingBracket(masked, 0);
            if (close < 0)
            {
                return new FileAnalysis(ExcludeToken().IsMatch(clean), false, "unbalanced bracket in @attribute");
            }

            var list = ParseList(masked[1..close]);
            anonymous |= OnTheType(list) && list.Names.Any(IsAllowAnonymous);
        }

        return new FileAnalysis(ExcludeToken().IsMatch(clean), anonymous, null);
    }

    private static bool OnTheType(AttributeList list) => list.Target is null or "type";

    private static bool IsAllowAnonymous(string name) =>
        name is "AllowAnonymous" or "AllowAnonymousAttribute"
            or "Microsoft.AspNetCore.Authorization.AllowAnonymous" or "Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute";

    /// <summary>The index of the "]" that closes the "[" at <paramref name="open"/> in already-masked text, or -1.</summary>
    private static int MatchingBracket(string masked, int open)
    {
        var depth = 0;
        for (var i = open; i < masked.Length; i++)
        {
            if (masked[i] == '[')
            {
                depth++;
            }
            else if (masked[i] == ']' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Splits the inside of an attribute list ("A, B(1, 2), assembly: C") on top-level commas. Input is masked, so it holds no string contents.</summary>
    private static AttributeList ParseList(string body)
    {
        var names = new List<string>();
        string? target = null;
        var depth = 0;
        var start = 0;
        for (var i = 0; i <= body.Length; i++)
        {
            if (i < body.Length && body[i] is '(' or '[' or '{')
            {
                depth++;
            }
            else if (i < body.Length && body[i] is ')' or ']' or '}')
            {
                depth--;
            }
            else if (i == body.Length || (body[i] == ',' && depth == 0))
            {
                var item = body[start..i].Trim();
                var colon = item.IndexOf(':', StringComparison.Ordinal);
                if (colon > 0 && item[..colon].All(char.IsLetter) && !item.StartsWith("::", StringComparison.Ordinal))
                {
                    target = item[..colon];
                    item = item[(colon + 1)..].Trim();
                }

                var paren = item.IndexOf('(', StringComparison.Ordinal);
                names.Add((paren >= 0 ? item[..paren] : item).Trim());
                start = i + 1;
            }
        }

        return new AttributeList(target, names);
    }

    // ---- Lexing -----------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Replaces comments with spaces and the contents of string and char literals with "x" (keeping newlines and the delimiters), so brackets and
    /// attribute names that sit inside a literal or a comment are never seen. Handles regular, verbatim (@"", where "" escapes a quote and \ is literal),
    /// interpolated (holes are lexed as code), raw strings and char literals such as ']'. Returns false with a reason on an unterminated construct.
    /// </summary>
    private static bool TryMaskCSharp(string text, out string masked, out string error)
    {
        var output = new StringBuilder(text.Length);
        var i = 0;
        var failure = LexCode(text, ref i, output, inHole: false);
        masked = output.ToString();
        error = failure ?? string.Empty;
        return failure is null;
    }

    private static string? LexCode(string t, ref int i, StringBuilder o, bool inHole)
    {
        var depth = 0;
        while (i < t.Length)
        {
            var c = t[i];
            if (inHole && c == '}' && depth == 0)
            {
                return null;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
            }

            if (c == '/' && i + 1 < t.Length && t[i + 1] == '/')
            {
                while (i < t.Length && t[i] != '\n')
                {
                    o.Append(' ');
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < t.Length && t[i + 1] == '*')
            {
                var end = t.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    return "unterminated comment";
                }

                for (; i < end + 2; i++)
                {
                    o.Append(t[i] == '\n' ? '\n' : ' ');
                }

                continue;
            }

            if (c is '"' or '$' or '@')
            {
                var j = i;
                var verbatim = false;
                var interpolated = false;
                while (j < t.Length && t[j] is '$' or '@')
                {
                    verbatim |= t[j] == '@';
                    interpolated |= t[j] == '$';
                    j++;
                }

                if (j < t.Length && t[j] == '"')
                {
                    o.Append(t, i, j - i);
                    i = j;
                    var failure = LexString(t, ref i, o, verbatim, interpolated);
                    if (failure is not null)
                    {
                        return failure;
                    }

                    continue;
                }

                o.Append(t, i, j - i);
                i = j;
                continue;
            }

            if (c == '\'')
            {
                var j = i + 1;
                if (j < t.Length && t[j] == '\\')
                {
                    j += 2;
                    while (j < t.Length && t[j] != '\'' && t[j] != '\n' && j - i < 12)
                    {
                        j++;
                    }
                }
                else
                {
                    j++;
                }

                if (j >= t.Length || t[j] != '\'')
                {
                    return "bad char literal";
                }

                o.Append('\'').Append('x', j - i - 1).Append('\'');
                i = j + 1;
                continue;
            }

            o.Append(c);
            i++;
        }

        return inHole ? "unterminated interpolation hole" : null;
    }

    private static string? LexString(string t, ref int i, StringBuilder o, bool verbatim, bool interpolated)
    {
        var quotes = 0;
        while (i + quotes < t.Length && t[i + quotes] == '"')
        {
            quotes++;
        }

        if (quotes >= 3)
        {
            var closing = new string('"', quotes);
            var end = t.IndexOf(closing, i + quotes, StringComparison.Ordinal);
            if (end < 0)
            {
                return "unterminated raw string";
            }

            o.Append(closing);
            for (var k = i + quotes; k < end; k++)
            {
                o.Append(t[k] == '\n' ? '\n' : 'x');
            }

            o.Append(closing);
            i = end + quotes;
            return null;
        }

        o.Append('"');
        i++;
        while (i < t.Length)
        {
            var c = t[i];
            if (c == '"')
            {
                if (verbatim && i + 1 < t.Length && t[i + 1] == '"')
                {
                    o.Append("xx");
                    i += 2;
                    continue;
                }

                o.Append('"');
                i++;
                return null;
            }

            if (c == '\n' && !verbatim)
            {
                return "unterminated string";
            }

            if (!verbatim && c == '\\' && i + 1 < t.Length)
            {
                o.Append("xx");
                i += 2;
                continue;
            }

            if (interpolated && (c == '{' || c == '}') && i + 1 < t.Length && t[i + 1] == c)
            {
                o.Append("xx");
                i += 2;
                continue;
            }

            if (interpolated && c == '{')
            {
                o.Append('{');
                i++;
                var failure = LexCode(t, ref i, o, inHole: true);
                if (failure is not null)
                {
                    return failure;
                }

                o.Append('}');
                i++;
                continue;
            }

            o.Append(c == '\n' ? '\n' : 'x');
            i++;
        }

        return "unterminated string";
    }

    /// <summary>Replaces each complete &lt;!-- --&gt; and @* *@ comment with a space. A comment opener left over is an unterminated comment.</summary>
    private static bool TryStripRazorComments(string text, out string clean, out string error)
    {
        clean = RazorComments().Replace(text, " ");
        error = string.Empty;
        if (clean.Contains("<!--", StringComparison.Ordinal) || clean.Contains("@*", StringComparison.Ordinal))
        {
            error = "unterminated comment";
            return false;
        }

        return true;
    }

    private static bool HasInlineElement(string markup)
    {
        foreach (Match tag in InlineTagStart().Matches(markup))
        {
            if (tag.Groups["name"].Value.Equals("style", StringComparison.OrdinalIgnoreCase) || !ScriptHasSrc(markup, tag.Index + tag.Length))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reads the attributes of a script tag, skipping quoted values, and reports whether one of them is named src.</summary>
    private static bool ScriptHasSrc(string t, int i)
    {
        while (i < t.Length)
        {
            while (i < t.Length && (char.IsWhiteSpace(t[i]) || t[i] == '/'))
            {
                i++;
            }

            if (i >= t.Length || t[i] == '>')
            {
                return false;
            }

            var nameStart = i;
            while (i < t.Length && !char.IsWhiteSpace(t[i]) && t[i] is not ('=' or '/' or '>'))
            {
                i++;
            }

            if (t.AsSpan(nameStart, i - nameStart).Equals("src", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            while (i < t.Length && char.IsWhiteSpace(t[i]))
            {
                i++;
            }

            if (i < t.Length && t[i] == '=')
            {
                i++;
                while (i < t.Length && char.IsWhiteSpace(t[i]))
                {
                    i++;
                }

                if (i < t.Length && t[i] is '"' or '\'')
                {
                    var close = t.IndexOf(t[i], i + 1);
                    i = close < 0 ? t.Length : close + 1;
                }
                else
                {
                    while (i < t.Length && !char.IsWhiteSpace(t[i]) && t[i] != '>')
                    {
                        i++;
                    }
                }
            }
        }

        return false;
    }
}
