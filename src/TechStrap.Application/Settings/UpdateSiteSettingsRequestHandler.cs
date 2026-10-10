using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Auditing;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Settings;
using TechStrap.Contracts.Skins;
using TechStrap.Domain.Admin;

namespace TechStrap.Application.Settings;

public interface IUpdateSiteSettingsRequestHandler
{
    Task<Result<SiteSettingsDto>> HandleAsync(UpdateSiteSettingsRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// PUT /api/settings/site (Admin, D-053). The caller sends the Version it read; a different stored version is a 409 so a second admin's edit is never
/// silently overwritten. A null pack leaves the setting alone; an unknown key is a 400 on <c>default-pack</c>. The settings are one row, so the audit
/// entry carries a fixed subject id.
/// </summary>
public sealed class UpdateSiteSettingsRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    ISiteSettingsRepository siteSettings,
    IAdminEventRepository adminEvents,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IUpdateSiteSettingsRequestHandler
{
    /// <summary>The audit subject id of the single settings row.</summary>
    internal static readonly Guid SubjectId = new("00000000-0000-0000-0000-000000000001");

    public async Task<Result<SiteSettingsDto>> HandleAsync(UpdateSiteSettingsRequest request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result<SiteSettingsDto>.Failure(actor.Errors[0]);
        }

        var settings = await siteSettings.GetAsync(cancellationToken);
        if (settings.Version != request.Version)
        {
            return Result<SiteSettingsDto>.Failure(SettingsErrors.Stale());
        }

        var key = request.DefaultPack?.Trim().ToLowerInvariant();
        if (key is not null && !SkinPacks.IsKnown(key))
        {
            return Result<SiteSettingsDto>.Failure(SettingsErrors.PackUnknown());
        }

        if (key is null || string.Equals(settings.DefaultPackKey, key, StringComparison.Ordinal))
        {
            return Result<SiteSettingsDto>.Success(new SiteSettingsDto(settings.DefaultPackKey, settings.Version));
        }

        var changed = settings.SetDefaultPack(key);
        if (changed.IsFailure)
        {
            return Result<SiteSettingsDto>.Failure(changed.Error!.ToError());
        }

        siteSettings.Update(settings);
        AdminAudit.Record(adminEvents, AdminEventType.SiteSettingsUpdated, actor.Value, AdminSubjectType.SiteSettings, SubjectId, new { defaultPack = key }, clock);

        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            return Result<SiteSettingsDto>.Failure(committed.Errors[0]);
        }

        var saved = await siteSettings.GetAsync(cancellationToken);
        return Result<SiteSettingsDto>.Success(new SiteSettingsDto(saved.DefaultPackKey, saved.Version));
    }
}
