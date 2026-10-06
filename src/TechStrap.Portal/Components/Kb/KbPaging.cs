using System.Globalization;

namespace TechStrap.Portal.Components.Kb;

/// <summary>
/// The page number of a paged help-centre list, read from text. The page is bound as text and parsed here, because the framework's own binding to a number answers 500 for <c>?page=abc</c> or a number that
/// does not fit (the spike). Anything that is not a whole number of one or more is page one, so a link someone mangled still shows the first page and nothing throws.
/// </summary>
public static class KbPaging
{
    public static int Parse(string? text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var page) && page >= 1 ? page : 1;

    /// <summary>The number of pages of <paramref name="totalCount"/> items, <paramref name="pageSize"/> to a page (zero for none).</summary>
    public static int TotalPages(int totalCount, int pageSize) => pageSize < 1 || totalCount < 1 ? 0 : (int)(((long)totalCount + pageSize - 1) / pageSize);
}
