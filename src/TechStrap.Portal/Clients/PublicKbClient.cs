using System.Globalization;
using SyntaxCircus.Common;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Clients;

internal sealed class PublicKbClient(ApiConnection api) : IPublicKbClient
{
    public Task<Result<PagedResponse<PublicKbSearchResultDto>>> SearchAsync(string productKey, string text, int pageSize, CancellationToken cancellationToken) =>
        ProductKeyShape.IsWellFormed(productKey)
            ? api.GetAsync<PagedResponse<PublicKbSearchResultDto>>(
                ApiQuery.Build($"api/public/kb/{productKey}/search", ("q", text), ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture))),
                cancellationToken)
            : Task.FromResult(Result<PagedResponse<PublicKbSearchResultDto>>.Failure(ProblemMapping.NotFound()));
}
