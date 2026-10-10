using Microsoft.Extensions.Options;
using TechStrap.Application.Products;
using TechStrap.Contracts.Products;

namespace TechStrap.Infrastructure.Attachments;

/// <summary>Builds an uploaded logo's absolute address from the configured Api public URL; null when none is configured (the Worker then keeps the linked logo).</summary>
internal sealed class ConfiguredProductLogoUrls(IOptions<ProductLogoUrlOptions> options) : IProductLogoUrls
{
    public string? UrlFor(string fileName)
    {
        var configured = options.Value.PublicUrl.Trim();
        if (configured.Length == 0)
        {
            return null;
        }

        // Built from the parsed address, never the raw string; start-up validation has already refused anything that is not an absolute http(s) URL.
        var baseUrl = new Uri(configured, UriKind.Absolute).GetLeftPart(UriPartial.Path).TrimEnd('/');
        return $"{baseUrl}/{ProductLogoLimits.PathPrefix}{fileName}";
    }
}
