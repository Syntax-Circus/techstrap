namespace TechStrap.Portal.Components;

/// <summary>
/// One <c>h1</c> per page (PHASE-09 T16, UX brief): the page's own title is the <c>h1</c>, so a body the API sanitized and sent (an article, a message) that carries an <c>h1</c> of its own (a Markdown line that
/// starts with a single <c>#</c>) is shown as an <c>h2</c>. This is the only change the Portal makes to such a body. The sanitizer allows no attribute on a heading and writes its tags in lower case, so the two
/// tag spellings below are the only heading forms there are. The replacement is textual, so a literal <c>&lt;h1&gt;</c> inside an attribute value would be rewritten too (cosmetic only: it is not markup there).
/// </summary>
public static class BodyHeadings
{
    public static string DemoteTitle(string html) =>
        html.Replace("<h1>", "<h2>", StringComparison.Ordinal).Replace("</h1>", "</h2>", StringComparison.Ordinal);
}
