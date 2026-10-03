namespace TechStrap.Application.Content;

/// <summary>
/// Markdown to HTML (D-014, D-035). Raw HTML in the source is not passed through. The output is NOT safe on its own:
/// callers always pass it through <see cref="IHtmlSanitizer"/>. PHASE-08 reuses this for KB articles.
/// </summary>
public interface IMarkdownRenderer
{
    string ToHtml(string markdown);
}
