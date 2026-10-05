namespace TechStrap.Contracts.Kb;

/// <summary>Wire names for the KB article status. Contracts carries no enums (naming rule); handlers parse these, case-insensitive.</summary>
public static class KbArticleStatuses
{
    public const string Draft = "Draft";
    public const string Published = "Published";
    public const string Archived = "Archived";
}

/// <summary>Knowledge-base limits and fixed names that more than one project reads (D-044).</summary>
public static class KbLimits
{
    /// <summary>The largest image an agent may upload: 5 MB.</summary>
    public const long MaxImageBytes = 5L * 1024 * 1024;

    /// <summary>The multipart field that carries the image on <c>POST api/kb/images</c>.</summary>
    public const string ImageFieldName = "file";

    /// <summary>The public path prefix of an uploaded image: <c>kb-images/{guid}.{ext}</c>.</summary>
    public const string ImagePathPrefix = "kb-images/";

    /// <summary>The longest Markdown source the preview endpoint renders. It equals the article body limit, so a body that can be saved can be previewed.</summary>
    public const int MaxPreviewChars = 200_000;

    /// <summary>
    /// The most Markdown elements (blocks plus inlines, table cells and list items included) one article body may parse to. The sanitiser is
    /// roughly quadratic in element count, so a body over this is refused on save and preview (<c>kb-body-too-complex</c>) and never sanitised (D-044).
    /// </summary>
    public const int MaxRenderedElements = 5_000;

    public const int DefaultPublicSearchPageSize = 10;

    public const int MaxPublicSearchPageSize = 25;

    /// <summary>The category slug the portal reserves for its KB search page; no category may use it.</summary>
    public const string ReservedCategorySlug = "search";

    /// <summary>The most characters of a search query the public search reads; longer text is cut.</summary>
    public const int MaxSearchTextChars = 200;

    /// <summary>The most entries one sitemap lists (the newest updates first); the sitemap protocol allows 50,000.</summary>
    public const int MaxSitemapEntries = 10_000;
}
