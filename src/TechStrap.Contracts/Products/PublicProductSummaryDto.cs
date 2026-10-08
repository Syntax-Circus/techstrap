namespace TechStrap.Contracts.Products;

/// <summary>
/// One row of the public product list (PHASE-09c): the key, the display name and the product's own hostname, nothing else. No logo, no colours, no id, no email. The list holds
/// active products only and exists for the portal's sitemap; the portal's root page never shows it.
/// </summary>
/// <param name="Key">The product key.</param>
/// <param name="DisplayName">The name customers see.</param>
/// <param name="PortalHost">The product's own public hostname (lower-case, e.g. support.example.com), or null when it is served only on the default portal host.</param>
public sealed record PublicProductSummaryDto(string Key, string DisplayName, string? PortalHost = null);

/// <summary>Limits of the public product list.</summary>
public static class PublicProductLimits
{
    /// <summary>The most products the public list returns: a defensive cap, far above any real install.</summary>
    public const int MaxListed = 1_000;
}
