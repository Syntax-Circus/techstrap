namespace TechStrap.Contracts.ApiKeys;

/// <summary>Key kinds (D-001). Trusted keys are server-side; public keys are embedded in client apps.</summary>
public static class ApiKeyKinds
{
    /// <summary>A key for server-side callers; its metadata is trusted. Wire value <c>Trusted</c>.</summary>
    public const string Trusted = "Trusted";
    /// <summary>A key embedded in client apps; its metadata is stored but flagged untrusted. Wire value <c>Public</c>.</summary>
    public const string Public = "Public";
}

/// <summary>A product API key without its secret: the prefix identifies it.</summary>
/// <param name="Id">The key's id.</param>
/// <param name="ProductId">The product the key belongs to.</param>
/// <param name="Kind">One of <see cref="ApiKeyKinds"/>.</param>
/// <param name="KeyPrefix">The visible start of the key, which identifies it.</param>
/// <param name="Label">A free-text label; may be null.</param>
/// <param name="CreatedAt">When the key was created.</param>
/// <param name="RevokedAt">When the key was revoked; null while it is active.</param>
/// <param name="LastUsedAt">When the key last authenticated a request; null if never.</param>
public sealed record ProductApiKeyDto(
    Guid Id,
    Guid ProductId,
    string Kind,
    string KeyPrefix,
    string? Label,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt);

/// <summary>Creates a key for a product.</summary>
/// <param name="Kind">One of <see cref="ApiKeyKinds"/>; required.</param>
/// <param name="Label">A free-text label; optional.</param>
public sealed record CreateProductApiKeyRequest(string? Kind, string? Label);

/// <summary>The only place the plaintext key ever appears. It cannot be shown again.</summary>
/// <param name="Key">The key's details, without the secret.</param>
/// <param name="PlaintextKey">The full key. Shown once; it cannot be retrieved later.</param>
public sealed record CreateProductApiKeyResponse(ProductApiKeyDto Key, string PlaintextKey);
