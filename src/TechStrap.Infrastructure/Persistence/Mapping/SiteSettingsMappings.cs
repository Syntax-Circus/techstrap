using TechStrap.Domain.Settings;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Mapping;

/// <summary>Maps between <see cref="SiteSettingsRecord"/> and <see cref="SiteSettings"/> (D-026).</summary>
internal static class SiteSettingsMappings
{
    public static SiteSettings ToDomain(this SiteSettingsRecord record) =>
        SiteSettings.Restore(record.DefaultPack, record.Version);

    /// <summary>Copies the mutable field. The id is never overwritten.</summary>
    public static void CopyTo(this SiteSettings settings, SiteSettingsRecord record) =>
        record.DefaultPack = settings.DefaultPackKey;
}
