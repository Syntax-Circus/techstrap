namespace TechStrap.Contracts.Products;

/// <summary>Branding as stored, plus the colours derived from the accent (D-025, D-031) for previews.</summary>
public sealed record ProductBrandingDto(
    string DisplayName,
    string? LogoPath,
    string AccentColour,
    string OnAccentColour,
    string AccentInkColour,
    string? FromAddress,
    string? ReplyTo);

/// <summary>Branding input. A null accent means the default accent; the accent must be #RRGGBB (D-031).</summary>
public sealed record ProductBrandingRequest(
    string? DisplayName,
    string? LogoPath,
    string? AccentColour,
    string? FromAddress,
    string? ReplyTo);

/// <summary><paramref name="Version"/> is the concurrency token; send it back unchanged in <see cref="UpdateProductRequest"/>.</summary>
/// <param name="Id">The product id.</param>
/// <param name="Key">The permanent product key.</param>
/// <param name="Name">The internal product name.</param>
/// <param name="NumberPrefix">The permanent ticket number prefix.</param>
/// <param name="IsActive">Whether the product is active.</param>
/// <param name="Branding">The stored branding with derived colours.</param>
/// <param name="Version">The concurrency token.</param>
/// <param name="PortalHost">The product's own public hostname (lower-case, e.g. support.example.com), or null when it is served only on the default portal host.</param>
public sealed record ProductDto(
    Guid Id,
    string Key,
    string Name,
    string NumberPrefix,
    bool IsActive,
    ProductBrandingDto Branding,
    uint Version,
    string? PortalHost = null);

/// <summary>Key and number prefix are permanent once created. A null branding derives the default from the name.</summary>
/// <param name="Key">The permanent product key.</param>
/// <param name="Name">The internal product name.</param>
/// <param name="NumberPrefix">The permanent ticket number prefix.</param>
/// <param name="Branding">The branding input, or null for the default.</param>
/// <param name="PortalHost">The product's own public hostname (lower-case, e.g. support.example.com), or null when it is served only on the default portal host.</param>
public sealed record CreateProductRequest(string? Key, string? Name, string? NumberPrefix, ProductBrandingRequest? Branding, string? PortalHost = null);

/// <summary><paramref name="Version"/> must equal the version last read; otherwise the update is a 409 conflict.</summary>
/// <param name="Name">The internal product name.</param>
/// <param name="Branding">The branding input.</param>
/// <param name="IsActive">Whether the product is active.</param>
/// <param name="Version">The version last read.</param>
/// <param name="PortalHost">The product's own public hostname (lower-case, e.g. support.example.com), or null when it is served only on the default portal host. Null leaves the stored host unchanged; an empty or whitespace value clears it; any other value is normalised, validated and set.</param>
public sealed record UpdateProductRequest(string? Name, ProductBrandingRequest Branding, bool IsActive, uint Version, string? PortalHost = null);
