using TechStrap.Contracts.Branding;
using TechStrap.Contracts.Products;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Products;

internal static class ProductMapping
{
    public static ProductDto ToDto(Product product)
    {
        var branding = product.Branding;

        // The stored accent is always a valid #RRGGBB (ProductBranding.Create); the derived colours are what customers see (D-025, D-031).
        var colours = ProductAccent.TryDerive(branding.AccentColour, out var derived)
            ? derived
            : new ProductAccentColors(branding.AccentColour, "#FFFFFF", branding.AccentColour);
        return new ProductDto(
            product.Id,
            product.Key,
            product.Name,
            product.NumberPrefix,
            product.IsActive,
            new ProductBrandingDto(branding.DisplayName, branding.LogoPath, colours.Accent, colours.OnAccent, colours.AccentInk, branding.FromAddress, branding.ReplyTo),
            product.Version);
    }
}
