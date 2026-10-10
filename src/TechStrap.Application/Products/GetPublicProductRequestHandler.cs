using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Branding;
using TechStrap.Contracts.Products;

namespace TechStrap.Application.Products;

public interface IGetPublicProductRequestHandler
{
    Task<Result<PublicProductDto>> HandleAsync(string? productKey, CancellationToken cancellationToken);
}

public sealed class GetPublicProductRequestHandler(IProductRepository products, IProductLogoUrls logoUrls) : IGetPublicProductRequestHandler
{
    public async Task<Result<PublicProductDto>> HandleAsync(string? productKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productKey))
        {
            return Result<PublicProductDto>.Failure(ProductErrors.NotFound());
        }

        var product = await products.GetByKeyAsync(productKey.Trim(), cancellationToken);
        if (product is null || !product.IsActive)
        {
            return Result<PublicProductDto>.Failure(ProductErrors.NotFound());
        }

        var branding = product.Branding;
        ProductAccent.TryDerive(branding.AccentColour, out var colors);
        return Result<PublicProductDto>.Success(new PublicProductDto(
            product.Key, branding.DisplayName, ProductLogos.EffectiveLogoUrl(branding, logoUrls), colors.Accent, colors.OnAccent, colors.AccentInk, product.PortalHost, branding.Tagline, ProductMapping.SkinOf(product)));
    }
}
