using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Settings;

namespace TechStrap.Application.Settings;

public interface IGetSiteSettingsRequestHandler
{
    Task<Result<SiteSettingsDto>> HandleAsync(CancellationToken cancellationToken);
}

/// <summary>GET /api/settings/site (Admin, D-053): the default theme pack and the version to send back on update.</summary>
public sealed class GetSiteSettingsRequestHandler(ICurrentAgentClaims currentAgent, IAgentRepository agents, ISiteSettingsRepository siteSettings) : IGetSiteSettingsRequestHandler
{
    public async Task<Result<SiteSettingsDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result<SiteSettingsDto>.Failure(actor.Errors[0]);
        }

        var settings = await siteSettings.GetAsync(cancellationToken);
        return Result<SiteSettingsDto>.Success(new SiteSettingsDto(settings.DefaultPackKey, settings.Version));
    }
}
