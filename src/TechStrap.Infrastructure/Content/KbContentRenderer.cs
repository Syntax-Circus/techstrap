using System.Net;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using TechStrap.Application.Content;

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
            html = Markdown.ToHtml(text, _pipeline);
        }
        catch (ArgumentException)
        {
            // Markdig refuses very deep nesting (for example 128 unclosed "[" from pasted terminal output). The text is shown as
            // encoded plain text; nothing from the body is logged.
            html = "<p>" + WebUtility.HtmlEncode(text) + "</p>";
        }

        return _sanitizer.Sanitize(html);
    }
}
