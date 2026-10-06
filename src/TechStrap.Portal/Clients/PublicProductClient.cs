using SyntaxCircus.Common;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Clients;

internal sealed class PublicProductClient(ApiConnection api) : IPublicProductClient
{
    public Task<Result<PublicProductDto>> GetAsync(string key, CancellationToken cancellationToken) =>
        ProductKeyShape.IsWellFormed(key)
            ? api.GetAsync<PublicProductDto>($"api/public/products/{key}", cancellationToken)
            : Task.FromResult(Result<PublicProductDto>.Failure(ProblemMapping.NotFound()));

    public Task<Result<IReadOnlyList<PublicProductSummaryDto>>> ListAsync(CancellationToken cancellationToken) =>
        api.GetAsync<IReadOnlyList<PublicProductSummaryDto>>("api/public/products", cancellationToken);
}
