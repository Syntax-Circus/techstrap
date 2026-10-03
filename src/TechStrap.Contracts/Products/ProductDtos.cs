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
public sealed record ProductDto(
    Guid Id,
    string Key,
    string Name,
    string NumberPrefix,
    bool IsActive,
    ProductBrandingDto Branding,
    uint Version);

/// <summary>Key and number prefix are permanent once created. A null branding derives the default from the name.</summary>
public sealed record CreateProductRequest(string? Key, string? Name, string? NumberPrefix, ProductBrandingRequest? Branding);

/// <summary><paramref name="Version"/> must equal the version last read; otherwise the update is a 409 conflict.</summary>
public sealed record UpdateProductRequest(string? Name, ProductBrandingRequest Branding, bool IsActive, uint Version);
