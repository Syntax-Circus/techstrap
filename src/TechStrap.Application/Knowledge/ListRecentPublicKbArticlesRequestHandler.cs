using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

public interface IListRecentPublicKbArticlesRequestHandler
{
    Task<Result<IReadOnlyList<PublicKbArticleSummaryDto>>> HandleAsync(string? productKey, int pageSize, CancellationToken cancellationToken);
}

/// <summary>
/// GET /api/public/kb/{productKey}/articles (anonymous, portal SEO). The most recently updated Published articles the product can see across all its
/// categories, newest update first; the count is clamped to 1..20 (the controller supplies the default of 8 when none is sent). An unknown or inactive product is the same 404 as the category article
/// list (a caller cannot tell which keys exist); a product with no published article is a 200 with an empty list. Every text field of the DTO is plain text.
/// </summary>
public sealed class ListRecentPublicKbArticlesRequestHandler(IProductRepository products, IKbRepository knowledgeBase) : IListRecentPublicKbArticlesRequestHandler
{
    public async Task<Result<IReadOnlyList<PublicKbArticleSummaryDto>>> HandleAsync(string? productKey, int pageSize, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(pageSize, 1, KbLimits.MaxRecentArticlesCount);
        var product = await PublicProductScope.ResolveAsync(products, productKey, cancellationToken);
        if (product is null)
        {
            return Result<IReadOnlyList<PublicKbArticleSummaryDto>>.Failure(KbErrors.CategoryNotFound());
        }

        var found = await knowledgeBase.ListPublicRecentArticlesAsync(product.Id, size, cancellationToken);
        return Result<IReadOnlyList<PublicKbArticleSummaryDto>>.Success(
            [.. found.Select(item => new PublicKbArticleSummaryDto(item.Slug, item.Title, item.Summary, item.CategorySlug, item.CategoryName, item.ProductKey, item.UpdatedAt))]);
    }
}
