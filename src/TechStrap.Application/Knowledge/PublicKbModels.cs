using TechStrap.Domain.Knowledge;

namespace TechStrap.Application.Knowledge;

/// <summary>
/// A public (portal) full-text search over Published articles (D-044). With <see cref="ProductId"/> it searches that product's articles and the shared
/// ones; <see cref="CategorySlug"/> narrows to one category. Implementations normalise <see cref="Page"/> and <see cref="PageSize"/>
/// through <c>Paging</c> and truncate <see cref="Text"/> to <c>DomainLimits.SearchTextMaxLength</c>.
/// </summary>
public sealed record PublicKbSearchQuery(string Text, Guid ProductId, string? CategorySlug = null, int Page = 1, int PageSize = 10);

/// <summary>One search hit. <see cref="Snippet"/> is plain text cut from the article summary by <c>ts_headline</c>. It is not HTML: the consumer (the portal) encodes it when it shows it, as it does every public text field.</summary>
public sealed record PublicKbSearchHit(string Slug, string Title, string Snippet, string CategorySlug, string CategoryName, string? ProductKey);

/// <summary>A Published article with the category and product names the portal address needs. <see cref="ProductKey"/> is null for a shared article.</summary>
public sealed record PublicKbArticleView(KbArticle Article, string CategorySlug, string CategoryName, string? ProductKey);

/// <summary>A category with the number of Published articles a product can see in it.</summary>
public sealed record PublicKbCategoryCount(KbCategory Category, int ArticleCount);

/// <summary>The current portal address parts of a Published article visible to a product, for re-checking a link queued earlier.</summary>
public sealed record PublicKbLinkTarget(Guid ArticleId, string CategorySlug, string Slug);

public sealed record PublicKbSitemapRow(string? ProductKey, string CategorySlug, string Slug, DateTimeOffset UpdatedAt);

/// <summary>One row of a category's article list: <see cref="Summary"/> is the author's plain-text summary (null when there is none), <see cref="ProductKey"/> is null for a shared article.</summary>
public sealed record PublicKbCategoryArticle(string Slug, string Title, string? Summary, string CategorySlug, string CategoryName, string? ProductKey, DateTimeOffset UpdatedAt);
