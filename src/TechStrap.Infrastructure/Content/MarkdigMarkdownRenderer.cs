using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using TechStrap.Application.Content;

namespace TechStrap.Infrastructure.Content;

/// <summary>
/// Markdig-backed renderer. Raw HTML in the source is never emitted as markup: it is escaped and shown as text,
/// and raw HTML carrying an inline event handler (onclick=, onerror=, ...) is dropped entirely. Output must still be
/// sanitized by the caller.
/// </summary>
internal sealed partial class MarkdigMarkdownRenderer : TechStrap.Application.Content.IMarkdownRenderer
{
    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseSoftlineBreakAsHardlineBreak()
        .UseAutoLinks()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .Use<EscapeRawHtmlExtension>()
        .Build();

    public string ToHtml(string markdown) => Markdown.ToHtml(markdown ?? string.Empty, _pipeline);

    private static bool HasEventHandler(string raw) => EventHandlerAttribute().IsMatch(raw);

    [GeneratedRegex(@"[\s/""']on[a-z]+\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EventHandlerAttribute();

    private sealed class EscapeRawHtmlExtension : IMarkdownExtension
    {
        public void Setup(MarkdownPipelineBuilder pipeline)
        {
        }

        public void Setup(MarkdownPipeline pipeline, Markdig.Renderers.IMarkdownRenderer renderer)
        {
            if (renderer is not HtmlRenderer html)
            {
                return;
            }

            html.ObjectRenderers.RemoveAll(r => r is HtmlInlineRenderer || r is HtmlBlockRenderer);
            html.ObjectRenderers.Add(new EscapingInlineRenderer());
            html.ObjectRenderers.Add(new EscapingBlockRenderer());
        }
    }

    private sealed class EscapingInlineRenderer : HtmlObjectRenderer<HtmlInline>
    {
        protected override void Write(HtmlRenderer renderer, HtmlInline obj)
        {
            if (!HasEventHandler(obj.Tag))
            {
                renderer.WriteEscape(obj.Tag);
            }
        }
    }

    private sealed class EscapingBlockRenderer : HtmlObjectRenderer<HtmlBlock>
    {
        protected override void Write(HtmlRenderer renderer, HtmlBlock obj)
        {
            var raw = obj.Lines.ToString();
            if (HasEventHandler(raw))
            {
                return;
            }

            renderer.Write("<p>").WriteEscape(raw).WriteLine("</p>");
        }
    }
}
