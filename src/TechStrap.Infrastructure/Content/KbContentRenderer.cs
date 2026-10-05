using System.Net;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Syntax;
using TechStrap.Application.Content;
using TechStrap.Contracts.Kb;

namespace TechStrap.Infrastructure.Content;

/// <summary>
/// The knowledge-base content profile (D-021, D-044): Markdig with raw HTML off and pipe tables, then <see cref="KbHtmlSanitizer"/>.
/// The message profile (<see cref="MarkdigMarkdownRenderer"/> and <see cref="HtmlSanitizerAdapter"/>) is deliberately not widened.
/// </summary>
internal sealed class KbContentRenderer : IKbContentRenderer
{
    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UsePipeTables()
        .UseAutoLinks()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .Build();

    private readonly KbHtmlSanitizer _sanitizer = new();

    public string Render(string markdown)
    {
        var text = markdown ?? string.Empty;
        string html;
        try
        {
            var document = Markdown.Parse(text, _pipeline);
            if (CountExceedsCap(document))
            {
                return Sanitize(EncodedParagraph(text));
            }

            html = document.ToHtml(_pipeline);
        }
        catch (ArgumentException)
        {
            // Markdig refuses very deep nesting (for example 128 unclosed "[" from pasted terminal output) with a plain
            // ArgumentException that has no dedicated type or code, and matching its message text would turn a Markdig wording
            // change into a 500, so the catch stays broad. The text is shown as encoded plain text; nothing from the body is logged.
            html = EncodedParagraph(text);
        }

        return Sanitize(html);
    }

    public bool IsTooComplex(string markdown)
    {
        try
        {
            return CountExceedsCap(Markdown.Parse(markdown ?? string.Empty, _pipeline));
        }
        catch (ArgumentException)
        {
            // Too deeply nested: Render shows it as encoded text, which is cheap, so it is not "too complex" to store.
            return false;
        }
    }

    // One linear walk over every block and inline; it stops as soon as the cap is passed.
    private static bool CountExceedsCap(MarkdownDocument document)
    {
        var count = 0;
        foreach (var _ in document.Descendants())
        {
            if (++count > KbLimits.MaxRenderedElements)
            {
                return true;
            }
        }

        return false;
    }

    private static string EncodedParagraph(string text) => "<p>" + WebUtility.HtmlEncode(text) + "</p>";

    private string Sanitize(string html) => _sanitizer.Sanitize(html);
}
