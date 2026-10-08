namespace TechStrap.Contracts.Kb;

/// <summary>
/// One public search hit. Every text field is plain text, the snippet (cut from the article summary) included: the consumer must encode it. 
/// Only <see cref="PublishedKbArticleDto.Html"/> is HTML. <paramref name="ProductKey"/> is null for a shared article.
/// </summary>
/// <param name="Slug">The article's slug.</param>
/// <param name="Title">The article title.</param>
/// <param name="Snippet">A plain-text excerpt cut from the article summary.</param>
/// <param name="CategorySlug">The slug of the article's category.</param>
/// <param name="CategoryName">The category's display name.</param>
/// <param name="ProductKey">The product key, or null for a shared article.</param>
public sealed record PublicKbSearchResultDto(string Slug, string Title, string Snippet, string CategorySlug, string CategoryName, string? ProductKey);

/// <summary>A published article for the portal. <paramref name="Html"/> is sanitised; there is no author and no id.</summary>
/// <param name="ProductKey">The product key, or null for a shared article.</param>
/// <param name="CategorySlug">The slug of the category.</param>
/// <param name="CategoryName">The category's display name.</param>
/// <param name="Slug">The article's slug.</param>
/// <param name="Title">The article title.</param>
/// <param name="Summary">The author's summary; may be null.</param>
/// <param name="Html">The sanitised HTML body.</param>
/// <param name="PublishedAt">When the article was published.</param>
/// <param name="UpdatedAt">When the article was last changed.</param>
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
/// <param name="Slug">The category's slug.</param>
/// <param name="Name">The display name.</param>
/// <param name="Description">The category description; may be null.</param>
/// <param name="ArticleCount">The number of published articles the product can see in it.</param>
public sealed record PublicKbCategoryDto(string Slug, string Name, string? Description, int ArticleCount);

/// <summary>One published article in the sitemap.</summary>
/// <param name="ProductKey">The product key, or null for a shared article.</param>
/// <param name="CategorySlug">The slug of the article's category.</param>
/// <param name="Slug">The article's slug.</param>
/// <param name="UpdatedAt">When the article was last changed.</param>
public sealed record KbSitemapEntryDto(string? ProductKey, string CategorySlug, string Slug, DateTimeOffset UpdatedAt);

/// <summary>
/// One row of a category's article list (PHASE-09c). Every text field is plain text: the consumer must encode it. <paramref name="Summary"/> is the
/// author's summary (not a search snippet) and may be null. <paramref name="ProductKey"/> is null for a shared article. There is no body, no author and no id.
/// </summary>
/// <param name="Slug">The article's slug.</param>
/// <param name="Title">The article title.</param>
/// <param name="Summary">The author's summary; may be null.</param>
/// <param name="CategorySlug">The slug of the article's category.</param>
/// <param name="CategoryName">The category's display name.</param>
/// <param name="ProductKey">The product key, or null for a shared article.</param>
/// <param name="UpdatedAt">When the article was last changed.</param>
public sealed record PublicKbArticleSummaryDto(string Slug, string Title, string? Summary, string CategorySlug, string CategoryName, string? ProductKey, DateTimeOffset UpdatedAt);
