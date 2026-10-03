using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Products;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Products;

public interface IGetProductRequestHandler
{
    Task<Result<ProductDto>> HandleAsync(Guid productId, CancellationToken cancellationToken);
}

/// <summary>GET /api/products/{id} (Agent): an inactive product is visible to admins only (D-022).</summary>
public sealed class GetProductRequestHandler(ICurrentAgentClaims currentAgent, IProductRepository products) : IGetProductRequestHandler
{
    public async Task<Result<ProductDto>> HandleAsync(Guid productId, CancellationToken cancellationToken)
    {
        if (currentAgent.Current is not { } claims)
        {
            return Result<ProductDto>.Failure(AgentErrors.AccessRequired());
        }

        var product = await products.GetByIdAsync(productId, cancellationToken);
        if (product is null || (!product.IsActive && claims.Role != AgentRole.Admin))
        {
            return Result<ProductDto>.Failure(ProductErrors.NotFound());
        }

        return Result<ProductDto>.Success(ProductMapping.ToDto(product));
    }
}
