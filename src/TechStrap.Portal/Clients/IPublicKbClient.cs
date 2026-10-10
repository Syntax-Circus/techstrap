using SyntaxCircus.Common;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;

namespace TechStrap.Portal.Clients;

/// <summary>
/// The public knowledge base (P09-T02). Every call is a read: retried like every read, anonymous, and forwarding the visitor's address. Every text field of a result is plain text, a consumer encodes it; only
/// <see cref="PublishedKbArticleDto.Html"/> is HTML (the API sanitized it). A product key, a category slug or an article slug that is not a slug is the uniform not-found error and no call is made.
/// </summary>
public interface IPublicKbClient
{
    /// <summary>
    /// The first page of the published articles of the product (and the shared ones) that match the text, best first, <paramref name="pageSize"/> at most. The API cuts a longer text at
    /// <see cref="KbLimits.MaxSearchTextChars"/> and answers a blank text or an unknown product with an empty page.
    /// </summary>
    Task<Result<PagedResponse<PublicKbSearchResultDto>>> SearchAsync(string productKey, string text, int pageSize, CancellationToken cancellationToken);

    /// <summary>The same for page <paramref name="page"/> (the API normalizes a page below one).</summary>
    Task<Result<PagedResponse<PublicKbSearchResultDto>>> SearchAsync(string productKey, string text, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>The categories the product can see (its own and the shared ones) with their published article counts; an empty category is left out, and an unknown product gives an empty list.</summary>
    Task<Result<IReadOnlyList<PublicKbCategoryDto>>> ListCategoriesAsync(string productKey, CancellationToken cancellationToken);

    /// <summary>One page of a category's published articles, newest update first. An unknown, invisible or empty category (and an unknown product) is the not-found error.</summary>
    Task<Result<PagedResponse<PublicKbArticleSummaryDto>>> ListCategoryArticlesAsync(string productKey, string categorySlug, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>A published article. A draft, an archived article, another product's article, a wrong category and an unknown product are the same not-found error.</summary>
    Task<Result<PublishedKbArticleDto>> GetArticleAsync(string productKey, string categorySlug, string slug, CancellationToken cancellationToken);

    /// <summary>One entry per published article the product can see, newest update first (a shared article has no product key). It feeds the sitemap.</summary>
    Task<Result<IReadOnlyList<KbSitemapEntryDto>>> GetSitemapAsync(string productKey, CancellationToken cancellationToken);
}
