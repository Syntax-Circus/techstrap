using System.Net;
using System.Text.RegularExpressions;

namespace TechStrap.Portal.Components.Kb;

/// <summary>
/// The description of an article for a search engine (PHASE-09c): the author's summary, else the first sentence of the body as plain text. The body is the API's sanitised HTML, read here only to take the words out of it:
/// the result is plain text that Razor encodes when it is written into a meta tag, and it is never put back into markup, so an imperfect tag strip cannot become an injection.
/// </summary>
public static partial class KbPlainText
{
    /// <summary>The longest description: a search engine shows about 160 characters.</summary>
    public const int MaxDescription = 160;

    /// <summary>The longest any one pattern may run on a body. The body is the API's sanitised HTML, which can be large, and an unclosed-tag body makes a lazy pattern quadratic, so a pattern that runs this long ends the description (empty) instead of holding a request.</summary>
    public const int MatchTimeoutMs = 100;

    [GeneratedRegex(@"<p(?:\s[^>]*)?>(.*?)</p\s*>", RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: MatchTimeoutMs)]
    private static partial Regex FirstParagraph();

    [GeneratedRegex(@"<(?:script|style)\b.*?</(?:script|style)\s*>", RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: MatchTimeoutMs)]
    private static partial Regex ScriptAndStyle();

    // A tag that ends a line of text (a paragraph, a heading, a list item, a row, a break) is a space, so two blocks do not run together; any other tag (emphasis, a link, code) is nothing, so a word is not split.
    [GeneratedRegex(@"</?(?:p|div|h[1-6]|li|ul|ol|tr|td|th|br|hr|pre|table|thead|tbody|blockquote)[^>]*>", RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: MatchTimeoutMs)]
    private static partial Regex BlockTag();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.CultureInvariant | RegexOptions.Singleline, matchTimeoutMilliseconds: MatchTimeoutMs)]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: MatchTimeoutMs)]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^.*?[.!?](?=\s|$)", RegexOptions.CultureInvariant | RegexOptions.Singleline, matchTimeoutMilliseconds: MatchTimeoutMs)]
    private static partial Regex FirstSentence();

    /// <summary>The summary when it has words, else the first sentence of the body's first paragraph (or of its text when it has none), cut at <see cref="MaxDescription"/> characters at a word; empty when there is no text at all.</summary>
    public static string Describe(string? summary, string? html)
    {
        var fromSummary = Collapse(summary);
        if (fromSummary.Length > 0)
        {
            return Cut(fromSummary);
        }

        try
        {
            var body = html ?? string.Empty;
            var paragraph = FirstParagraph().Match(body);
            var text = Collapse(Strip(paragraph.Success ? paragraph.Groups[1].Value : body));
            if (text.Length == 0)
            {
                return string.Empty;
            }

            var sentence = FirstSentence().Match(text);
            return Cut(sentence.Success ? sentence.Value : text);
        }
        catch (RegexMatchTimeoutException)
        {
            // A description is an extra for a search engine; a body that is too costly to read has none.
            return string.Empty;
        }
    }

    // Known and accepted: a '>' inside a quoted attribute value ends a tag early for these patterns, so the rest of that value can show up as stray words in the description (<a title="a>b">). It is cosmetic only: the result is plain
    // text that is encoded when written, never put back into markup, and the API's sanitiser leaves few attributes. A robust fix needs a real HTML parser (AngleSharp is not a Portal package, and a new dependency for a meta tag is not worth it).
    private static string Strip(string html) => WebUtility.HtmlDecode(AnyTag().Replace(BlockTag().Replace(ScriptAndStyle().Replace(html, " "), " "), string.Empty));

    private static string Collapse(string? text) => Whitespace().Replace(text ?? string.Empty, " ").Trim();

    private static string Cut(string text)
    {
        if (text.Length <= MaxDescription)
        {
            return text;
        }

        var cut = text[..MaxDescription];
        if (char.IsHighSurrogate(cut[^1]))
        {
            cut = cut[..^1];
        }

        var space = cut.LastIndexOf(' ');
        return (space > MaxDescription / 2 ? cut[..space] : cut).TrimEnd() + "...";
    }
}
