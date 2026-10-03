namespace TechStrap.Api.Security;

/// <summary>Claims on the principal that <see cref="ProductApiKeyValidator"/> builds for a valid product API key.</summary>
public static class ApiKeyClaimTypes
{
    public const string KeyId = "techstrap:api_key_id";
    public const string ProductId = "techstrap:product_id";
    public const string Kind = "techstrap:api_key_kind"; // ApiKeyKinds.Trusted or ApiKeyKinds.Public
}
