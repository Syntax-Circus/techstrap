namespace TechStrap.Contracts.ApiKeys;

/// <summary>Key kinds (D-001). Trusted keys are server-side; public keys are embedded in client apps.</summary>
public static class ApiKeyKinds
{
    public const string Trusted = "Trusted";
    public const string Public = "Public";
}

/// <summary>A product API key without its secret: the prefix identifies it.</summary>
public sealed record ProductApiKeyDto(
    Guid Id,
    Guid ProductId,
    string Kind,
    string KeyPrefix,
    string? Label,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt);

public sealed record CreateProductApiKeyRequest(string? Kind, string? Label);

/// <summary>The only place the plaintext key ever appears. It cannot be shown again.</summary>
public sealed record CreateProductApiKeyResponse(ProductApiKeyDto Key, string PlaintextKey);
