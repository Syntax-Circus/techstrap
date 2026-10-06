namespace TechStrap.Contracts.Products;

/// <summary>
/// One row of the public product list (PHASE-09c): the key and the display name, nothing else. No logo, no colours, no id, no email. The list holds
/// active products only and exists for the portal's sitemap; the portal's root page never shows it.
/// </summary>
public sealed record PublicProductSummaryDto(string Key, string DisplayName);

/// <summary>Limits of the public product list.</summary>
public static class PublicProductLimits
{
    /// <summary>The most products the public list returns: a defensive cap, far above any real install.</summary>
    public const int MaxListed = 1_000;
}
