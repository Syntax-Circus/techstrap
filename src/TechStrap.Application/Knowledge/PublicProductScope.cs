using TechStrap.Application.Persistence;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Knowledge;

/// <summary>Resolves a portal product key. Blank, unknown and inactive keys all give null, so the public KB never says which keys exist.</summary>
internal static class PublicProductScope
{
    public static async Task<Product?> ResolveAsync(IProductRepository products, string? productKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productKey))
        {
            return null;
        }

        var product = await products.GetByKeyAsync(productKey.Trim(), cancellationToken);
        return product is { IsActive: true } ? product : null;
    }
}
