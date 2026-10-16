using SyntaxCircus.Common;
using TechStrap.Contracts.Features;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Clients;

/// <summary>The curated feature board. Writes use the non-retrying API connection.</summary>
public interface IFeaturesClient
{
    Task<Result<PagedResponse<FeatureSummaryDto>>> ListAsync(Guid? productId, string? search, string? status, int page, CancellationToken cancellationToken);
    Task<Result<FeatureDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<FeatureDto>> CreateAsync(CreateFeatureRequest request, CancellationToken cancellationToken);
    Task<Result<FeatureDto>> UpdateAsync(Guid id, UpdateFeatureRequest request, CancellationToken cancellationToken);
    Task<Result<FeatureDto>> PublishAsync(Guid id, uint version, bool publish, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<LinkedFeatureTicketDto>>> TicketsAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<TicketStateDto>> LinkTicketAsync(Guid ticketId, Guid? featureId, uint version, CancellationToken cancellationToken);
}

internal sealed class FeaturesClient(ApiConnection connection) : IFeaturesClient
{
    public Task<Result<PagedResponse<FeatureSummaryDto>>> ListAsync(Guid? productId, string? search, string? status, int page, CancellationToken cancellationToken) =>
        connection.GetAsync<PagedResponse<FeatureSummaryDto>>(ApiUri.Build("api/features", ("productId", productId), ("q", string.IsNullOrWhiteSpace(search) ? null : search.Trim()), ("status", status), ("page", page), ("pageSize", 25)), cancellationToken);
    public Task<Result<FeatureDto>> GetAsync(Guid id, CancellationToken cancellationToken) => connection.GetAsync<FeatureDto>($"api/features/{id}", cancellationToken);
    public Task<Result<FeatureDto>> CreateAsync(CreateFeatureRequest request, CancellationToken cancellationToken) => connection.SendAsync<FeatureDto>(HttpMethod.Post, "api/features", request, cancellationToken);
    public Task<Result<FeatureDto>> UpdateAsync(Guid id, UpdateFeatureRequest request, CancellationToken cancellationToken) => connection.SendAsync<FeatureDto>(HttpMethod.Put, $"api/features/{id}", request, cancellationToken);
    public Task<Result<FeatureDto>> PublishAsync(Guid id, uint version, bool publish, CancellationToken cancellationToken) => connection.SendAsync<FeatureDto>(HttpMethod.Post, ApiUri.Build($"api/features/{id}/{(publish ? "publish" : "unpublish")}", ("version", version)), null, cancellationToken);
    public async Task<Result<IReadOnlyList<LinkedFeatureTicketDto>>> TicketsAsync(Guid id, CancellationToken cancellationToken) => ProductsClient.Narrow(await connection.GetAsync<List<LinkedFeatureTicketDto>>($"api/features/{id}/tickets", cancellationToken));
    public Task<Result<TicketStateDto>> LinkTicketAsync(Guid ticketId, Guid? featureId, uint version, CancellationToken cancellationToken) => connection.SendAsync<TicketStateDto>(HttpMethod.Post, $"api/tickets/{ticketId}/feature", new LinkTicketFeatureRequest(featureId, version), cancellationToken);
}
