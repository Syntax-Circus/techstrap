using System.Text.RegularExpressions;

namespace TechStrap.Tests.Shared;

/// <summary>Detects active content in rendered HTML (attribute patterns are matched inside a tag, so encoded text such as <c>&amp;lt;a href="javascript:x"&amp;gt;</c> is inert): executable or embedding tags, in-tag event handlers, script-bearing attribute values, srcdoc and CSS expression().</summary>
public static partial class XssAssertions
{
    [GeneratedRegex(@"<(script|iframe|object|embed|base|meta|form|math|template|style|link|frameset)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ActiveTag();

    [GeneratedRegex(@"<(?:[^>""']|""[^""]*""|'[^']*')*(?:[\s/]|(?<=[""']))on[a-z]+\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EventHandler();

    [GeneratedRegex(@"<(?:[^>""']|""[^""]*""|'[^']*')*=\s*[""']?\s*(javascript|vbscript|data:text/html)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptScheme();

    [GeneratedRegex(@"<(?:[^>""']|""[^""]*""|'[^']*')*\bsrcdoc\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SrcDoc();

    [GeneratedRegex(@"<(?:[^>""']|""[^""]*""|'[^']*')*[""']?[^""'>]*expression\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CssExpression();

    /// <summary>True when <paramref name="html"/> holds none of the active-content patterns.</summary>
    public static bool ContainsNoActiveContent(string html) => Find(html) is null;

    /// <summary>Fails with the matching pattern and the offending fragment when <paramref name="html"/> holds active content.</summary>
    public static void ShouldHaveNoActiveContent(string html, string because)
    {
        if (Find(html) is { } hit)
        {
            throw new ShouldAssertException($"Active content ({hit.Name}) '{hit.Fragment}' in the output for {because}. Output: {html}");
        }
    }

    private static (string Name, string Fragment)? Find(string html)
    {
        foreach (var (name, regex) in Patterns())
        {
            var match = regex.Match(html);
            if (match.Success)
            {
                return (name, match.Value);
            }
        }

        return null;
    }

    private static (string Name, Regex Regex)[] Patterns() =>
    [
        ("tag", ActiveTag()),
        ("event handler", EventHandler()),
        ("script scheme", ScriptScheme()),
        ("srcdoc", SrcDoc()),
        ("expression", CssExpression()),
    ];
}
