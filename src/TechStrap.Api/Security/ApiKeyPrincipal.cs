using System.Security.Claims;
using TechStrap.Contracts.ApiKeys;

namespace TechStrap.Api.Security;

/// <summary>Reads the key principal built by <see cref="ProductApiKeyValidator"/>.</summary>
internal static class ApiKeyPrincipal
{
    public static (Guid? KeyId, Guid? ProductId, bool Trusted) Read(ClaimsPrincipal user) =>
        (
            ParseGuid(user.FindFirstValue(ApiKeyClaimTypes.KeyId)),
            ParseGuid(user.FindFirstValue(ApiKeyClaimTypes.ProductId)),
            string.Equals(user.FindFirstValue(ApiKeyClaimTypes.Kind), ApiKeyKinds.Trusted, StringComparison.Ordinal));

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}
