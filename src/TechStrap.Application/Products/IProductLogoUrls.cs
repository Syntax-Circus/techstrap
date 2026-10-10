using TechStrap.Domain.Products;

namespace TechStrap.Application.Products;

/// <summary>
/// The absolute public address of an uploaded product logo, <c>{api public url}/product-logos/{fileName}</c> (D-052). The Api builds it from <c>TECHSTRAP_API_PUBLIC_URL</c>; the Worker builds it from its own
/// optional copy of that setting and answers null when it is blank, so emails then fall back to the linked logo.
/// </summary>
public interface IProductLogoUrls
{
    string? UrlFor(string fileName);
}

/// <summary>The one rule for which logo a reader sees: the uploaded logo when there is one and this host can address it, else the linked logo address (D-052).</summary>
public static class ProductLogos
{
    public static string? EffectiveLogoUrl(ProductBranding branding, IProductLogoUrls urls)
    {
        ArgumentNullException.ThrowIfNull(branding);
        ArgumentNullException.ThrowIfNull(urls);
        return branding.UploadedLogo is { Length: > 0 } name ? urls.UrlFor(name) ?? branding.LogoPath : branding.LogoPath;
    }
}
