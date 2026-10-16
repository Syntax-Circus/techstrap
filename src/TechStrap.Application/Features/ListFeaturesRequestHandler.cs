using SyntaxCircus.Common;
using TechStrap.Contracts.Features;
using TechStrap.Contracts.Paging;

namespace TechStrap.Application.Features;

public interface IListFeaturesRequestHandler
{
    Task<Result<PagedResponse<FeatureSummaryDto>>> HandleAsync(Guid? productId, string? q, string? status, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>Handles the ListFeatures feature board operation.</summary>
public sealed class ListFeaturesRequestHandler(IFeatureService features) : IListFeaturesRequestHandler
{
    public Task<Result<PagedResponse<FeatureSummaryDto>>> HandleAsync(Guid? productId, string? q, string? status, int page, int pageSize, CancellationToken cancellationToken) =>
        features.ListAsync(productId, q, status, page, pageSize, cancellationToken);
}
