using TechStrap.Contracts.Products;

namespace TechStrap.Api.Startup;

/// <summary>Request size limit for the product logo upload (D-052). The store enforces the exact limit; this stops an oversize body early, as a 413.</summary>
public static class ProductLogoRequestLimits
{
    /// <summary>The logo limit plus 1 MiB for the multipart framing, so a logo a little over 1 MiB still reaches the store's <c>product-logo-too-large</c> answer.</summary>
    public const long FormBytes = ProductLogoLimits.MaxBytes + (1024 * 1024);
}
