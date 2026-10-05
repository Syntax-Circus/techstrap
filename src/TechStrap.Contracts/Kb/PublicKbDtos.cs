namespace TechStrap.Contracts.Kb;

/// <summary>
/// One public search hit. <paramref name="Snippet"/> is plain text from the article summary with every HTML character encoded, so it is
/// safe to place in markup as it is. <paramref name="ProductKey"/> is null for a shared article.
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
