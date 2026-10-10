using TechStrap.Contracts.Branding;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Skins;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Products;

internal static class ProductMapping
{
    public static ProductDto ToDto(Product product, IProductLogoUrls logoUrls)
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
            new ProductBrandingDto(branding.DisplayName, branding.LogoPath, colours.Accent, colours.OnAccent, colours.AccentInk, branding.FromAddress, branding.ReplyTo,
                branding.Tagline, branding.UploadedLogo is { } name ? logoUrls.UrlFor(name) : null),
            product.Version,
            product.PortalHost,
            product.ListedOnLanding,
            SkinOf(product));
    }

    /// <summary>The product's stored skin, or null when it has none or the stored JSON no longer parses (the Portal then falls back to the default pack).</summary>
    public static ProductSkin? SkinOf(Product product) =>
        SkinSerializer.TryDeserialize(product.SkinJson, out var skin) ? skin : null;
}
