using System.Security.Claims;
using SyntaxCircus.AspNetCore.Authentication;
using TechStrap.Application.ApiKeys;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.ApiKeys;
using TechStrap.Domain.Products;

namespace TechStrap.Api.Security;

/// <summary>Validates a product API key against the database: known hash, not revoked, product active.</summary>
internal sealed class ProductApiKeyValidator(IApiKeyHasher hasher, IProductRepository products) : IApiKeyValidator
{
    private readonly IApiKeyHasher _hasher = hasher;
    private readonly IProductRepository _products = products;

    public async Task<ApiKeyValidationResult> ValidateAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return ApiKeyValidationResult.Invalid;
        }

        var key = await _products.GetApiKeyByHashAsync(_hasher.Hash(apiKey.Trim()), cancellationToken);
        if (key is null || key.IsRevoked)
        {
            return ApiKeyValidationResult.Invalid;
        }

        var product = await _products.GetByIdAsync(key.ProductId, cancellationToken);
        if (product is null || !product.IsActive)
        {
            return ApiKeyValidationResult.Invalid;
        }

        return ApiKeyValidationResult.Valid(
        [
            new Claim(ClaimTypes.NameIdentifier, key.Id.ToString("D")),
            new Claim(ApiKeyClaimTypes.KeyId, key.Id.ToString("D")),
            new Claim(ApiKeyClaimTypes.ProductId, key.ProductId.ToString("D")),
            new Claim(ApiKeyClaimTypes.Kind, key.Kind == ApiKeyKind.Trusted ? ApiKeyKinds.Trusted : ApiKeyKinds.Public),
        ]);
    }
}
