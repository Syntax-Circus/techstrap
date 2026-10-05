using System.Text;
using System.Text.RegularExpressions;

namespace TechStrap.Admin.Features.Kb;

/// <summary>
/// The Markdown the editor's toolbar adds. Blazor cannot read where the caret is in a textarea, so a snippet is always added at the end of the text (D-044: no caret insertion, paste or
/// drag-and-drop yet). Each one starts on its own line after a blank line, except an inline one, which follows a space, so it never glues itself to the last word.
/// </summary>
public static partial class MarkdownSnippets
{
    public const string Bold = "**bold text**";
    public const string Italic = "*italic text*";
    public const string Link = "[link text](https://)";
    public const string List = "- first item\n- second item";
    public const string Code = "```\ncode\n```";

    /// <summary>The longest alt text kept for an image whose file name is the only description the editor has.</summary>
    public const int MaxAltLength = 100;

    [GeneratedRegex(@"[\[\]()<>\\`*_\r\n]+")]
    private static partial Regex AltUnsafe();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary><paramref name="text"/> with <paramref name="snippet"/> after it: a block starts after a blank line, an inline one after a space.</summary>
    public static string Append(string? text, string snippet, bool inline)
    {
        if (string.IsNullOrEmpty(text))
        {
            return snippet;
        }

        var separator = inline
            ? text.EndsWith(' ') || text.EndsWith('\n') ? string.Empty : " "
            : text.EndsWith("\n\n", StringComparison.Ordinal) ? string.Empty : text.EndsWith('\n') ? "\n" : "\n\n";
        return text + separator + snippet;
    }

    /// <summary>
    /// <c>![alt](url)</c>. The alt text is cleaned of the characters that would end or change the Markdown. In the address a space and the two round brackets are percent-encoded: they are the
    /// characters that would end the link early.
    /// </summary>
    public static string Image(string altText, string url)
    {
        var encoded = new StringBuilder(url.Length);
        foreach (var c in url.Trim())
        {
            encoded.Append(c switch
            {
                ' ' => "%20",
                '(' => "%28",
                ')' => "%29",
                _ => c.ToString(),
            });
        }

        return $"![{CleanAlt(altText)}]({encoded})";
    }

    /// <summary>The alt text an image gets from its file name: the name without its extension, with the Markdown characters taken out, and cut to <see cref="MaxAltLength"/>.</summary>
    public static string AltFromFileName(string? fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName?.Trim() ?? string.Empty).Replace('-', ' ').Replace('_', ' ');
        var alt = CleanAlt(name);
        return alt.Length == 0 ? "image" : alt;
    }

    private static string CleanAlt(string text)
    {
        var cleaned = Whitespace().Replace(AltUnsafe().Replace(text, " "), " ").Trim();
        return cleaned.Length > MaxAltLength ? cleaned[..MaxAltLength].TrimEnd() : cleaned;
    }
}
