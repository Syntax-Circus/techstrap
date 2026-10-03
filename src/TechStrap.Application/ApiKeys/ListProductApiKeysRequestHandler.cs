using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Application.Products;
using TechStrap.Contracts.ApiKeys;

namespace TechStrap.Application.ApiKeys;

public interface IListProductApiKeysRequestHandler
{
    Task<Result<IReadOnlyList<ProductApiKeyDto>>> HandleAsync(Guid productId, CancellationToken cancellationToken);
}

/// <summary>GET /api/products/{id}/api-keys (Admin): newest first, revoked keys included. Never carries a secret or hash.</summary>
public sealed class ListProductApiKeysRequestHandler(IProductRepository products) : IListProductApiKeysRequestHandler
{
    public async Task<Result<IReadOnlyList<ProductApiKeyDto>>> HandleAsync(Guid productId, CancellationToken cancellationToken)
    {
        if (await products.GetByIdAsync(productId, cancellationToken) is null)
        {
            return Result<IReadOnlyList<ProductApiKeyDto>>.Failure(ProductErrors.NotFound());
        }

        return Result<IReadOnlyList<ProductApiKeyDto>>.Success([.. (await products.ListApiKeysAsync(productId, cancellationToken)).Select(ApiKeyMapping.ToDto)]);
    }
}
