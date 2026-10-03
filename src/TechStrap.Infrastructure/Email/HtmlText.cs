using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace TechStrap.Infrastructure.Email;

/// <summary>Converts stored, already-sanitised message HTML to the plain-text alternative of an email.</summary>
internal static partial class HtmlText
{
    private static readonly HashSet<string> _blockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "ul", "ol", "li", "blockquote", "pre", "table", "tr", "h1", "h2", "h3", "h4", "h5", "h6", "hr",
    };

    // Deeper nesting is flattened to its text content so a hostile body cannot overflow the stack.
    private const int MaxDepth = 128;

    public static string ToPlainText(string html)
    {
        var document = new HtmlParser().ParseDocument($"<body>{html}</body>");
        var text = new StringBuilder();
        Walk(document.Body!, text, 0);
        return text.ToString().Trim();
    }

    private static void Walk(INode node, StringBuilder text, int depth)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child)
            {
                case IText run:
                    text.Append(Whitespace().Replace(run.Data, " "));
                    break;
                case IHtmlBreakRowElement:
                    text.Append('\n');
                    break;
                case IElement element:
                    WalkElement(element, text, depth);
                    break;
            }
        }
    }

    private static void WalkElement(IElement element, StringBuilder text, int depth)
    {
        var tag = element.LocalName;
        if (tag is "script" or "style")
        {
            return;
        }

        if (depth >= MaxDepth)
        {
            AppendFlatText(element, text);
            return;
        }

        var block = _blockTags.Contains(tag);
        if (block)
        {
            EnsureBreak(text, tag == "li" ? 1 : 2);
            if (tag == "li")
            {
                text.Append("- ");
            }
        }

        var start = text.Length;
        Walk(element, text, depth + 1);
        if (tag == "a" && element.GetAttribute("href") is { } href && IsLinkable(href))
        {
            var label = text.ToString(start, text.Length - start).Trim();
            if (!string.Equals(label, href, StringComparison.OrdinalIgnoreCase))
            {
                text.Append(" (").Append(href).Append(')');
            }
        }

        if (block)
        {
            EnsureBreak(text, tag == "li" ? 1 : 2);
        }
    }

    /// <summary>Appends the descendant text of <paramref name="root"/> using an explicit stack, so depth cannot exhaust the call stack.</summary>
    private static void AppendFlatText(INode root, StringBuilder text)
    {
        var pending = new Stack<INode>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node is IText run)
            {
                text.Append(Whitespace().Replace(run.Data, " "));
            }
            else if (node is not (IHtmlScriptElement or IHtmlStyleElement))
            {
                for (var i = node.ChildNodes.Length - 1; i >= 0; i--)
                {
                    pending.Push(node.ChildNodes[i]);
                }
            }
        }
    }

    private static bool IsLinkable(string href) =>
        href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    /// <summary>Makes the buffer end with at least <paramref name="newlines"/> line breaks (none at the very start).</summary>
    private static void EnsureBreak(StringBuilder text, int newlines)
    {
        if (text.Length == 0)
        {
            return;
        }

        while (text.Length > 0 && text[^1] == ' ')
        {
            text.Length--;
        }

        var existing = 0;
        while (existing < text.Length && text[text.Length - 1 - existing] == '\n')
        {
            existing++;
        }

        text.Append('\n', Math.Max(0, newlines - existing));
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
