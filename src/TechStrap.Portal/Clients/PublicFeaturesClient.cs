using System.Globalization;
using SyntaxCircus.Common;
using TechStrap.Contracts.Features;
using TechStrap.Contracts.Paging;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Clients;

internal sealed class PublicFeaturesClient(ApiConnection api) : IPublicFeaturesClient
{
    public Task<Result<PagedResponse<PublicFeatureSummaryDto>>> ListAsync(string productKey, string? query, string? status, int page, int pageSize, CancellationToken cancellationToken) =>
        ProductKeyShape.IsWellFormed(productKey)
            ? api.GetAsync<PagedResponse<PublicFeatureSummaryDto>>(ApiQuery.Build($"api/public/products/{productKey}/features", ("q", query), ("status", status), ("page", page.ToString(CultureInfo.InvariantCulture)), ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture))), cancellationToken)
            : NotFound<PagedResponse<PublicFeatureSummaryDto>>();

    public Task<Result<PublicFeatureDto>> GetAsync(string productKey, string slug, CancellationToken cancellationToken) =>
        ProductKeyShape.IsWellFormed(productKey) && KbSlugShape.IsWellFormed(slug)
            ? api.GetAsync<PublicFeatureDto>($"api/public/products/{productKey}/features/{slug}", cancellationToken)
            : NotFound<PublicFeatureDto>();

    private static Task<Result<T>> NotFound<T>() => Task.FromResult(Result<T>.Failure(ProblemMapping.NotFound()));
}
