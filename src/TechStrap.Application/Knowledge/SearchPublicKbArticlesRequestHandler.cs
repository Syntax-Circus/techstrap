using System.Net;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;

namespace TechStrap.Application.Knowledge;

public interface ISearchPublicKbArticlesRequestHandler
{
    Task<Result<PagedResponse<PublicKbSearchResultDto>>> HandleAsync(string? productKey, string? text, string? categorySlug, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>
/// GET /api/public/kb/{productKey}/search (anonymous, D-044). Published articles of the product and the shared space, best match first, 10 a page and
/// at most 25. An unknown or inactive product, and blank text, give an empty page, never a 404, so a caller cannot tell which keys exist. The snippet
/// is cut from the summary by the database and HTML-encoded here, so it is safe in markup as it is. Also used by contact-form deflection.
/// </summary>
public sealed class SearchPublicKbArticlesRequestHandler(IProductRepository products, IKbRepository knowledgeBase) : ISearchPublicKbArticlesRequestHandler
{
    public async Task<Result<PagedResponse<PublicKbSearchResultDto>>> HandleAsync(
        string? productKey, string? text, string? categorySlug, int page, int pageSize, CancellationToken cancellationToken)
    {
        var size = pageSize < 1 ? KbLimits.DefaultPublicSearchPageSize : Math.Min(pageSize, KbLimits.MaxPublicSearchPageSize);
        var normalisedPage = Paging.NormalizePage(page);
        var product = await PublicProductScope.ResolveAsync(products, productKey, cancellationToken);
        if (product is null || string.IsNullOrWhiteSpace(text))
        {
            return Result<PagedResponse<PublicKbSearchResultDto>>.Success(new PagedResponse<PublicKbSearchResultDto>([], normalisedPage, size, 0));
        }

        var found = await knowledgeBase.SearchPublicAsync(new PublicKbSearchQuery(text, product.Id, categorySlug, normalisedPage, size), cancellationToken);
        return Result<PagedResponse<PublicKbSearchResultDto>>.Success(new PagedResponse<PublicKbSearchResultDto>(
            [.. found.Items.Select(hit => new PublicKbSearchResultDto(hit.Slug, hit.Title, WebUtility.HtmlEncode(hit.Snippet), hit.CategorySlug, hit.CategoryName, hit.ProductKey))],
            found.Page, found.PageSize, found.TotalCount));
    }
}
