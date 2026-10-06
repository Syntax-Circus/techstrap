using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Domain.Rules;

namespace TechStrap.Application.Knowledge;

public interface IListPublicKbCategoryArticlesRequestHandler
{
    Task<Result<PagedResponse<PublicKbArticleSummaryDto>>> HandleAsync(string? productKey, string? categorySlug, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>
/// GET /api/public/kb/{productKey}/categories/{categorySlug}/articles (anonymous, PHASE-09c, D-045 addendum). One page of the Published articles the
/// product can see in the category, newest update first, 10 a page and at most 25. Everything that is not a category with at least one such article (an
/// unknown or inactive product, a malformed or unknown slug, another product's category, an empty category) is the same 404, so a caller cannot tell
/// which keys or categories exist. A page past the end is a 200 with no items. Every text field of the DTO is plain text.
/// </summary>
public sealed class ListPublicKbCategoryArticlesRequestHandler(IProductRepository products, IKbRepository knowledgeBase) : IListPublicKbCategoryArticlesRequestHandler
{
    public async Task<Result<PagedResponse<PublicKbArticleSummaryDto>>> HandleAsync(
        string? productKey, string? categorySlug, int page, int pageSize, CancellationToken cancellationToken)
    {
        var size = pageSize < 1 ? KbLimits.DefaultPublicSearchPageSize : Math.Min(pageSize, KbLimits.MaxPublicSearchPageSize);
        var product = await PublicProductScope.ResolveAsync(products, productKey, cancellationToken);
        if (product is null || !PublicProductScope.IsSlug(categorySlug, DomainLimits.KbSlugMaxLength))
        {
            return Result<PagedResponse<PublicKbArticleSummaryDto>>.Failure(KbErrors.CategoryNotFound());
        }

        var found = await knowledgeBase.ListPublicCategoryArticlesAsync(product.Id, categorySlug!.Trim(), Paging.NormalizePage(page), size, cancellationToken);
        if (found is null)
        {
            return Result<PagedResponse<PublicKbArticleSummaryDto>>.Failure(KbErrors.CategoryNotFound());
        }

        return Result<PagedResponse<PublicKbArticleSummaryDto>>.Success(new PagedResponse<PublicKbArticleSummaryDto>(
            [.. found.Items.Select(item => new PublicKbArticleSummaryDto(item.Slug, item.Title, item.Summary, item.CategorySlug, item.CategoryName, item.ProductKey, item.UpdatedAt))],
            found.Page, found.PageSize, found.TotalCount));
    }
}
