using Markdig;
using Markdig.Extensions.EmphasisExtras;
using TechStrap.Application.Content;
using TechStrap.Application.Intake;

namespace TechStrap.Infrastructure.Content;

/// <summary>Markdig-backed renderer with raw HTML disabled. Output must still be sanitized by the caller.</summary>
internal sealed class MarkdigMarkdownRenderer : IMarkdownRenderer
{
    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseSoftlineBreakAsHardlineBreak()
        .UseAutoLinks()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .Build();

    public string ToHtml(string markdown)
    {
        var text = markdown ?? string.Empty;
        try
        {
            return Markdown.ToHtml(text, _pipeline);
        }
        catch (ArgumentException)
        {
            // Markdig refuses very deep nesting (for example 128 unclosed "[" from pasted terminal output). The agent's text is
            // kept as encoded plain text; nothing from the body is logged.
            return CustomerText.ToHtml(text);
        }
    }
}
