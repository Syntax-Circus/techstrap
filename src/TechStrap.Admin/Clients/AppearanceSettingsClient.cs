using SyntaxCircus.Common;
using TechStrap.Contracts.Settings;

namespace TechStrap.Admin.Clients;

/// <summary>Admin-only deployment appearance settings. Reads retry; writes never retry.</summary>
public interface IAppearanceSettingsClient
{
    Task<Result<SiteSettingsDto>> GetAsync(CancellationToken cancellationToken);
    Task<Result<SiteSettingsDto>> UpdateAsync(UpdateSiteSettingsRequest request, CancellationToken cancellationToken);
}

internal sealed class AppearanceSettingsClient(ApiConnection connection) : IAppearanceSettingsClient
{
    public Task<Result<SiteSettingsDto>> GetAsync(CancellationToken cancellationToken) =>
        connection.GetAsync<SiteSettingsDto>("api/settings/site", cancellationToken);

    public Task<Result<SiteSettingsDto>> UpdateAsync(UpdateSiteSettingsRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<SiteSettingsDto>(HttpMethod.Put, "api/settings/site", request, cancellationToken);
}
