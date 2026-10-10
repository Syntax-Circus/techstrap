using TechStrap.Contracts.Skins;

namespace TechStrap.Contracts.Products;

/// <summary>Public branding for the portal. It never carries emails, keys or internal ids.</summary>
/// <param name="Key">The product key.</param>
/// <param name="DisplayName">The name customers see.</param>
/// <param name="LogoPath">The logo address, or null.</param>
/// <param name="AccentColour">The accent color as #RRGGBB.</param>
/// <param name="OnAccentColour">The text color that reads on the accent.</param>
/// <param name="AccentInkColour">The accent adjusted for text on the page background.</param>
/// <param name="PortalHost">The product's own public hostname (lower-case, e.g. support.example.com), or null when it is served only on the default portal host.</param>
/// <param name="Tagline">One line of plain text about the product, or null.</param>
/// <param name="Skin">The product's skin (D-053), or null when it sets none; the Portal resolves it against the deployment's default pack.</param>
public sealed record PublicProductDto(
    string Key,
    string DisplayName,
    string? LogoPath,
    string AccentColour,
    string OnAccentColour,
    string AccentInkColour,
    string? PortalHost = null,
    string? Tagline = null,
    ProductSkin? Skin = null);
