using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Products;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Products;

public interface IListProductsRequestHandler
{
    Task<Result<IReadOnlyList<ProductDto>>> HandleAsync(CancellationToken cancellationToken);
}

/// <summary>GET /api/products (Agent): agents get active products; admins get all (D-022).</summary>
public sealed class ListProductsRequestHandler(ICurrentAgentClaims currentAgent, IProductRepository products) : IListProductsRequestHandler
{
    public async Task<Result<IReadOnlyList<ProductDto>>> HandleAsync(CancellationToken cancellationToken)
    {
        if (currentAgent.Current is not { } claims)
        {
            return Result<IReadOnlyList<ProductDto>>.Failure(AgentErrors.AccessRequired());
        }

        var found = await products.ListAsync(activeOnly: claims.Role != AgentRole.Admin, cancellationToken);
        return Result<IReadOnlyList<ProductDto>>.Success([.. found.Select(ProductMapping.ToDto)]);
    }
}
