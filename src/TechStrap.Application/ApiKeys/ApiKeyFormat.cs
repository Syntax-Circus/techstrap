using TechStrap.Domain.Products;

namespace TechStrap.Application.ApiKeys;

/// <summary>
/// Key format: kind prefix ("tsk_" trusted, "tsp_" public) plus 43 base64url characters (32 random bytes). The first 12 characters
/// are stored to identify a key; the hash is "sha256:" and the lower-case hex SHA-256 of the whole key. A plain hash is enough
/// because the secret has 256 bits of entropy, and lookups need a deterministic hash.
/// </summary>
public static class ApiKeyFormat
{
    public const string TrustedPrefix = "tsk_";
    public const string PublicPrefix = "tsp_";
    public const int SecretBytes = 32;
    public const int SecretLength = 43;
    public const int StoredPrefixLength = 12;
    public const string HashScheme = "sha256:";

    public static string KindPrefix(ApiKeyKind kind) => kind == ApiKeyKind.Trusted ? TrustedPrefix : PublicPrefix;
}
