using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Settings;

namespace TechStrap.Application.Settings;

public interface IGetPublicSiteRequestHandler
{
    Task<Result<PublicSiteDto>> HandleAsync(CancellationToken cancellationToken);
}

/// <summary>GET /api/public/site (anonymous, D-053): the default pack key the Portal resolves product skins against. Nothing else of the settings leaves.</summary>
public sealed class GetPublicSiteRequestHandler(ISiteSettingsRepository siteSettings) : IGetPublicSiteRequestHandler
{
    public async Task<Result<PublicSiteDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var settings = await siteSettings.GetAsync(cancellationToken);
        return Result<PublicSiteDto>.Success(new PublicSiteDto(settings.DefaultPackKey));
    }
}
