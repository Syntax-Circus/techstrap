using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Products;

namespace TechStrap.Application.Products;

public interface IListPublicProductsRequestHandler
{
    Task<Result<IReadOnlyList<PublicProductSummaryDto>>> HandleAsync(CancellationToken cancellationToken);
}

/// <summary>
/// GET /api/public/products (anonymous, PHASE-09c, D-045 addendum). The ACTIVE products' key and display name, ordered by key and capped at
/// <see cref="PublicProductLimits.MaxListed"/>, for the portal's sitemap. An inactive product never appears. Listing the keys is accepted because the sitemap publishes them anyway.
/// </summary>
public sealed class ListPublicProductsRequestHandler(IProductRepository products) : IListPublicProductsRequestHandler
{
    public async Task<Result<IReadOnlyList<PublicProductSummaryDto>>> HandleAsync(CancellationToken cancellationToken)
    {
        var active = await products.ListAsync(activeOnly: true, cancellationToken);
        return Result<IReadOnlyList<PublicProductSummaryDto>>.Success(
            [.. active
                .Where(product => product.IsActive)
                .OrderBy(product => product.Key, StringComparer.Ordinal)
                .Take(PublicProductLimits.MaxListed)
                .Select(product => new PublicProductSummaryDto(product.Key, product.Branding.DisplayName))]);
    }
}
