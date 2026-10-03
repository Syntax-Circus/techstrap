using Markdig;
using Markdig.Extensions.EmphasisExtras;
using TechStrap.Application.Content;

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

    public string ToHtml(string markdown) => Markdown.ToHtml(markdown ?? string.Empty, _pipeline);
}
