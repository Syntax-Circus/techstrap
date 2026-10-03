using System.Net;
using System.Text.RegularExpressions;

namespace TechStrap.Application.Intake;

/// <summary>Customer bodies are plain text (no Markdown, no HTML). This turns them into safe HTML paragraphs before sanitizing.</summary>
public static partial class CustomerText
{
    [GeneratedRegex(@"\n\s*\n", RegexOptions.CultureInvariant)]
    private static partial Regex ParagraphBreak();

    public static string ToHtml(string text)
    {
        var normalised = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        var paragraphs = ParagraphBreak().Split(normalised)
            .Select(paragraph => paragraph.Trim())
            .Where(paragraph => paragraph.Length > 0)
            .Select(paragraph => "<p>" + string.Join("<br>", paragraph.Split('\n').Select(line => WebUtility.HtmlEncode(line.Trim()))) + "</p>");
        return string.Concat(paragraphs);
    }
}
