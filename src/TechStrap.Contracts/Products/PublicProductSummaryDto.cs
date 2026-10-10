namespace TechStrap.Contracts.Products;

/// <summary>
/// One row of the public product list (PHASE-09c, D-045; extended by D-052): the key, the display name, the product's own hostname, and what a landing card shows (tagline, effective logo, accent) plus whether it is
/// listed. No id, no email. The list holds active products only; it feeds the Portal's sitemap, its product-host map and, when <c>TECHSTRAP_PORTAL_LANDING=Products</c>, its landing page, which shows the rows whose
/// <see cref="ListedOnLanding"/> is true.
/// </summary>
/// <param name="Key">The product key.</param>
/// <param name="DisplayName">The name customers see.</param>
/// <param name="PortalHost">The product's own public hostname (lower-case, e.g. support.example.com), or null when it is served only on the default portal host.</param>
/// <param name="Tagline">One line of plain text about the product, or null.</param>
/// <param name="LogoUrl">The logo address to show: the uploaded logo when there is one, else the linked logo address, else null.</param>
/// <param name="AccentColour">The accent colour as #RRGGBB, or null for the default.</param>
/// <param name="ListedOnLanding">Whether the product appears on the landing page.</param>
public sealed record PublicProductSummaryDto(string Key, string DisplayName, string? PortalHost = null, string? Tagline = null, string? LogoUrl = null, string? AccentColour = null, bool ListedOnLanding = true);

/// <summary>Limits of the public product list.</summary>
public static class PublicProductLimits
{
    /// <summary>The most products the public list returns: a defensive cap, far above any real install.</summary>
    public const int MaxListed = 1_000;
}
