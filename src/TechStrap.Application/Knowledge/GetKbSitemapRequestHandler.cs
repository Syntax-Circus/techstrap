using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

public interface IGetKbSitemapRequestHandler
{
    Task<Result<IReadOnlyList<KbSitemapEntryDto>>> HandleAsync(string? productKey, CancellationToken cancellationToken);
}

/// <summary>
/// GET /api/public/kb/{productKey}/sitemap (anonymous, D-044). One entry per Published article the product can see, newest update first, for the portal
/// sitemap. A shared article has a null product key; the portal builds its address from the key it is serving. An unknown or inactive product gives an empty list.
/// </summary>
public sealed class GetKbSitemapRequestHandler(IProductRepository products, IKbRepository knowledgeBase) : IGetKbSitemapRequestHandler
{
    public async Task<Result<IReadOnlyList<KbSitemapEntryDto>>> HandleAsync(string? productKey, CancellationToken cancellationToken)
    {
        var product = await PublicProductScope.ResolveAsync(products, productKey, cancellationToken);
        if (product is null)
        {
            return Result<IReadOnlyList<KbSitemapEntryDto>>.Success([]);
        }

        var rows = await knowledgeBase.ListPublicSitemapAsync(product.Id, cancellationToken);
        return Result<IReadOnlyList<KbSitemapEntryDto>>.Success([.. rows.Select(row => new KbSitemapEntryDto(row.ProductKey, row.CategorySlug, row.Slug, row.UpdatedAt))]);
    }
}
