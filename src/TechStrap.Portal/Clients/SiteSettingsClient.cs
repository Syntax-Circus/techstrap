using SyntaxCircus.Common;
using TechStrap.Contracts.Settings;

namespace TechStrap.Portal.Clients;

internal sealed class SiteSettingsClient(ApiConnection api) : ISiteSettingsClient
{
    public Task<Result<PublicSiteDto>> GetAsync(CancellationToken cancellationToken) =>
        api.GetAsync<PublicSiteDto>("api/public/site", cancellationToken);
}
