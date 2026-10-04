using SyntaxCircus.Common;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;

namespace TechStrap.Admin.Clients;

/// <summary>Active products for an Agent, all products for an Admin.</summary>
public interface IProductsClient
{
    Task<Result<IReadOnlyList<ProductDto>>> ListAsync(CancellationToken cancellationToken);
}

public interface ITagsClient
{
    Task<Result<IReadOnlyList<TagDto>>> ListAsync(CancellationToken cancellationToken);
}

internal sealed class AgentsClient(ApiConnection connection) : IAgentsClient
{
    public const int PageSize = 100;

    // Stops a misbehaving API (a TotalCount that never converges) from looping for ever: 100 pages of 100 is 10 000 agents.
    private const int MaxPages = 100;

    public Task<Result<AgentDto>> GetMeAsync(CancellationToken cancellationToken) => connection.GetAsync<AgentDto>("api/agents/me", cancellationToken);

    public async Task<Result<IReadOnlyList<AgentListItemDto>>> ListAllAsync(CancellationToken cancellationToken)
    {
        var all = new List<AgentListItemDto>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var result = await connection.GetAsync<PagedResponse<AgentListItemDto>>(ApiUri.Build("api/agents", ("page", page), ("pageSize", PageSize)), cancellationToken);
            if (result.IsFailure)
            {
                return Result<IReadOnlyList<AgentListItemDto>>.Failure(result.Errors[0], [.. result.Errors.Skip(1)]);
            }

            all.AddRange(result.Value.Items);
            if (result.Value.Items.Count == 0 || all.Count >= result.Value.TotalCount)
            {
                break;
            }
        }

        return Result<IReadOnlyList<AgentListItemDto>>.Success(all);
    }
}

internal sealed class ProductsClient(ApiConnection connection) : IProductsClient
{
    public async Task<Result<IReadOnlyList<ProductDto>>> ListAsync(CancellationToken cancellationToken) =>
        Narrow(await connection.GetAsync<List<ProductDto>>("api/products", cancellationToken));

    internal static Result<IReadOnlyList<T>> Narrow<T>(Result<List<T>> result) =>
        result.IsSuccess ? Result<IReadOnlyList<T>>.Success(result.Value) : Result<IReadOnlyList<T>>.Failure(result.Errors[0], [.. result.Errors.Skip(1)]);
}

internal sealed class TagsClient(ApiConnection connection) : ITagsClient
{
    public async Task<Result<IReadOnlyList<TagDto>>> ListAsync(CancellationToken cancellationToken) =>
        ProductsClient.Narrow(await connection.GetAsync<List<TagDto>>("api/tags", cancellationToken));
}
