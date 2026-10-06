namespace TechStrap.Contracts.Kb;

/// <summary>
/// One public search hit. Every text field is plain text, the snippet (cut from the article summary) included: the consumer must encode it. 
/// Only <see cref="PublishedKbArticleDto.Html"/> is HTML. <paramref name="ProductKey"/> is null for a shared article.
/// </summary>
public sealed record PublicKbSearchResultDto(string Slug, string Title, string Snippet, string CategorySlug, string CategoryName, string? ProductKey);

/// <summary>A published article for the portal. <paramref name="Html"/> is sanitised; there is no author and no id.</summary>
public sealed record PublishedKbArticleDto(
    string? ProductKey,
    string CategorySlug,
    string CategoryName,
    string Slug,
    string Title,
    string? Summary,
    string Html,
    DateTimeOffset PublishedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A category with the number of published articles the product can see in it (its own plus the shared ones).</summary>
public sealed record PublicKbCategoryDto(string Slug, string Name, string? Description, int ArticleCount);

public sealed record KbSitemapEntryDto(string? ProductKey, string CategorySlug, string Slug, DateTimeOffset UpdatedAt);

/// <summary>
/// One row of a category's article list (PHASE-09c). Every text field is plain text: the consumer must encode it. <paramref name="Summary"/> is the
/// author's summary (not a search snippet) and may be null. <paramref name="ProductKey"/> is null for a shared article. There is no body, no author and no id.
/// </summary>
public sealed record PublicKbArticleSummaryDto(string Slug, string Title, string? Summary, string CategorySlug, string CategoryName, string? ProductKey, DateTimeOffset UpdatedAt);
