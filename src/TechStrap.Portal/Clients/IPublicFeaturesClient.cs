using SyntaxCircus.Common;
using TechStrap.Contracts.Features;
using TechStrap.Contracts.Paging;

namespace TechStrap.Portal.Clients;

public interface IPublicFeaturesClient
{
    Task<Result<PagedResponse<PublicFeatureSummaryDto>>> ListAsync(string productKey, string? query, string? status, int page, int pageSize, CancellationToken cancellationToken);
    Task<Result<PublicFeatureDto>> GetAsync(string productKey, string slug, CancellationToken cancellationToken);
}
