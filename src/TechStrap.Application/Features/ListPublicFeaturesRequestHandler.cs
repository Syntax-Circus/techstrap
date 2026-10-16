using SyntaxCircus.Common;
using TechStrap.Contracts.Features;
using TechStrap.Contracts.Paging;

namespace TechStrap.Application.Features;

public interface IListPublicFeaturesRequestHandler
{
    Task<Result<PagedResponse<PublicFeatureSummaryDto>>> HandleAsync(string key, string? q, string? status, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>Handles the ListPublicFeatures feature board operation.</summary>
public sealed class ListPublicFeaturesRequestHandler(IFeatureService features) : IListPublicFeaturesRequestHandler
{
    public Task<Result<PagedResponse<PublicFeatureSummaryDto>>> HandleAsync(string key, string? q, string? status, int page, int pageSize, CancellationToken cancellationToken) =>
        features.ListPublicAsync(key, q, status, page, pageSize, cancellationToken);
}
