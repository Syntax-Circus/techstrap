using SyntaxCircus.Common;
using TechStrap.Contracts.ApiKeys;
using TechStrap.Domain.Products;

namespace TechStrap.Application.ApiKeys;

internal static class ApiKeyMapping
{
    public static ProductApiKeyDto ToDto(ProductApiKey key) =>
        new(key.Id, key.ProductId, key.Kind == ApiKeyKind.Trusted ? ApiKeyKinds.Trusted : ApiKeyKinds.Public, key.KeyPrefix, key.Label, key.CreatedAt, key.RevokedAt, key.LastUsedAt);

    public static bool TryParseKind(string? value, out ApiKeyKind kind)
    {
        kind = default;
        if (string.Equals(value?.Trim(), ApiKeyKinds.Trusted, StringComparison.OrdinalIgnoreCase))
        {
            kind = ApiKeyKind.Trusted;
            return true;
        }

        if (string.Equals(value?.Trim(), ApiKeyKinds.Public, StringComparison.OrdinalIgnoreCase))
        {
            kind = ApiKeyKind.Public;
            return true;
        }

        return false;
    }

    public static ResultError KindInvalid() =>
        new("api-key-kind-invalid", "Choose Trusted (server-side) or Public (inside an app).", ResultErrorKind.Validation, "kind");

    public static ResultError NotFound() => new("api-key-not-found", "That API key does not exist for this product.", ResultErrorKind.NotFound);
}
