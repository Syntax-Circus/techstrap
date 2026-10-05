namespace TechStrap.Application.Content;

/// <summary>
/// Knowledge-base Markdown to safe HTML (D-021, D-044). One call runs the KB Markdown pipeline (raw HTML off, pipe tables) and the
/// KB sanitiser, so the editor preview and the public article page cannot drift apart. The result is safe to place in a page as it is.
/// Agent replies and customer messages do not use it: they keep <see cref="IMarkdownRenderer"/> and <see cref="IHtmlSanitizer"/>.
/// </summary>
public interface IKbContentRenderer
{
    /// <summary>
    /// Renders safe HTML. A body over <see cref="TechStrap.Contracts.Kb.KbLimits.MaxRenderedElements"/> is never sanitised (the sanitiser is
    /// roughly quadratic in element count, so a large table or list would burn CPU on every read): it comes back as encoded text in one
    /// paragraph. Writers refuse such a body with <see cref="IsTooComplex"/>, so a stored article never reaches that fallback.
    /// </summary>
    string Render(string markdown);

    /// <summary>
    /// True when the parsed Markdown has more than <see cref="TechStrap.Contracts.Kb.KbLimits.MaxRenderedElements"/> blocks and inlines
    /// (table cells and list items included). It parses once and counts in a single linear pass; it never sanitises.
    /// </summary>
    bool IsTooComplex(string markdown);
}
