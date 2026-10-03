using TechStrap.Domain.Products;

namespace TechStrap.Application.ApiKeys;

/// <summary>Generates product API keys and hashes and verifies them (D-001). Only the prefix and the hash are ever stored.</summary>
public interface IApiKeyHasher
{
    GeneratedApiKey Generate(ApiKeyKind kind);

    string Hash(string plaintextKey);

    /// <summary>Constant-time comparison of the key's hash with the stored hash.</summary>
    bool Verify(string plaintextKey, string keyHash);
}

/// <summary>A new key. <see cref="PlaintextKey"/> is shown to the admin once and never stored.</summary>
public sealed record GeneratedApiKey(string PlaintextKey, string KeyPrefix, string KeyHash);
