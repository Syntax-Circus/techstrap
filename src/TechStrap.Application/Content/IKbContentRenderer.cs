namespace TechStrap.Application.Content;

/// <summary>
/// Knowledge-base Markdown to safe HTML (D-021, D-044). One call runs the KB Markdown pipeline (raw HTML off, pipe tables) and the
/// KB sanitiser, so the editor preview and the public article page cannot drift apart. The result is safe to place in a page as it is.
/// Agent replies and customer messages do not use it: they keep <see cref="IMarkdownRenderer"/> and <see cref="IHtmlSanitizer"/>.
/// </summary>
public interface IKbContentRenderer
{
    string Render(string markdown);
}
