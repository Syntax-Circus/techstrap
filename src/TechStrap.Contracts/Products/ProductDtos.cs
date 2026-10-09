namespace TechStrap.Contracts.Products;

/// <summary>Branding as stored, plus the colours derived from the accent (D-025, D-031) for previews. <paramref name="UploadedLogoUrl"/> is the absolute address of an uploaded logo (D-052), read-only: it is set through the logo upload route, not through this DTO's request.</summary>
/// <param name="DisplayName">The name customers see.</param>
/// <param name="LogoPath">The linked logo address, or null.</param>
/// <param name="AccentColour">The accent colour as #RRGGBB.</param>
/// <param name="OnAccentColour">The text colour that reads on the accent.</param>
/// <param name="AccentInkColour">The accent adjusted for text on the page background.</param>
/// <param name="FromAddress">The sender address for customer emails, or null.</param>
/// <param name="ReplyTo">The reply-to address for customer emails, or null.</param>
/// <param name="Tagline">One line of plain text shown on the product's landing card, or null.</param>
/// <param name="UploadedLogoUrl">The absolute https address of the uploaded logo, or null when none is uploaded (or the host cannot build it). It supersedes <paramref name="LogoPath"/> wherever a logo is shown.</param>
public sealed record ProductBrandingDto(
    string DisplayName,
    string? LogoPath,
    string AccentColour,
    string OnAccentColour,
    string AccentInkColour,
    string? FromAddress,
    string? ReplyTo,
    string? Tagline = null,
    string? UploadedLogoUrl = null);

/// <summary>Branding input. A null accent means the default accent; the accent must be #RRGGBB (D-031).</summary>
/// <param name="DisplayName">The name customers see, or null to derive it.</param>
/// <param name="LogoPath">The linked logo address, or null.</param>
/// <param name="AccentColour">The accent colour as #RRGGBB, or null for the default.</param>
/// <param name="FromAddress">The sender address for customer emails, or null.</param>
/// <param name="ReplyTo">The reply-to address for customer emails, or null.</param>
/// <param name="Tagline">One line of plain text, at most 160 characters, shown on the landing card; null or blank clears it.</param>
public sealed record ProductBrandingRequest(
    string? DisplayName,
    string? LogoPath,
    string? AccentColour,
    string? FromAddress,
    string? ReplyTo,
    string? Tagline = null);

/// <summary><paramref name="Version"/> is the concurrency token; send it back unchanged in <see cref="UpdateProductRequest"/>.</summary>
/// <param name="Id">The product id.</param>
/// <param name="Key">The permanent product key.</param>
/// <param name="Name">The internal product name.</param>
/// <param name="NumberPrefix">The permanent ticket number prefix.</param>
/// <param name="IsActive">Whether the product is active.</param>
/// <param name="Branding">The stored branding with derived colours.</param>
/// <param name="Version">The concurrency token.</param>
/// <param name="PortalHost">The product's own public hostname (lower-case, e.g. support.example.com), or null when it is served only on the default portal host.</param>
/// <param name="ListedOnLanding">Whether the product appears on the Portal's landing page when it lists products (D-052). It changes nothing else.</param>
public sealed record ProductDto(
    Guid Id,
    string Key,
    string Name,
    string NumberPrefix,
    bool IsActive,
    ProductBrandingDto Branding,
    uint Version,
    string? PortalHost = null,
    bool ListedOnLanding = true);

/// <summary>Key and number prefix are permanent once created. A null branding derives the default from the name.</summary>
/// <param name="Key">The permanent product key.</param>
/// <param name="Name">The internal product name.</param>
/// <param name="NumberPrefix">The permanent ticket number prefix.</param>
/// <param name="Branding">The branding input, or null for the default.</param>
/// <param name="PortalHost">The product's own public hostname (lower-case, e.g. support.example.com), or null when it is served only on the default portal host.</param>
/// <param name="ListedOnLanding">Whether the product appears on the landing page; null means true.</param>
public sealed record CreateProductRequest(string? Key, string? Name, string? NumberPrefix, ProductBrandingRequest? Branding, string? PortalHost = null, bool? ListedOnLanding = null);

/// <summary><paramref name="Version"/> must equal the version last read; otherwise the update is a 409 conflict.</summary>
/// <param name="Name">The internal product name.</param>
/// <param name="Branding">The branding input.</param>
/// <param name="IsActive">Whether the product is active.</param>
/// <param name="Version">The version last read.</param>
/// <param name="PortalHost">The product's own public hostname (lower-case, e.g. support.example.com), or null when it is served only on the default portal host. Null leaves the stored host unchanged; an empty or whitespace value clears it; any other value is normalised, validated and set.</param>
/// <param name="ListedOnLanding">Whether the product appears on the landing page. Null leaves the stored value unchanged (a 0.2.0 client never unlists a product); true or false sets it.</param>
public sealed record UpdateProductRequest(string? Name, ProductBrandingRequest Branding, bool IsActive, uint Version, string? PortalHost = null, bool? ListedOnLanding = null);
