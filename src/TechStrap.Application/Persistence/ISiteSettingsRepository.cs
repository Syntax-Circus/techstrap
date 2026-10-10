using TechStrap.Domain.Settings;

namespace TechStrap.Application.Persistence;

/// <summary>
/// The single deployment-wide settings row (D-053). <c>Update</c> requires the settings to have been loaded in the current scope and checks the
/// version the Domain object carries, so a copy that was read before another change conflicts.
/// </summary>
public interface ISiteSettingsRepository
{
    /// <summary>The settings; never null. The migration seeds the row, and a missing row reads as the <c>classic</c> default with version 0.</summary>
    Task<SiteSettings> GetAsync(CancellationToken cancellationToken);

    void Update(SiteSettings settings);
}
