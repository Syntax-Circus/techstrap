using Microsoft.EntityFrameworkCore;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Settings;
using TechStrap.Infrastructure.Persistence.Mapping;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Repositories;

internal sealed class SiteSettingsRepository(TechStrapDbContext context) : ISiteSettingsRepository
{
    public async Task<SiteSettings> GetAsync(CancellationToken cancellationToken)
    {
        var record = await context.Set<SiteSettingsRecord>().FirstOrDefaultAsync(s => s.Id == SiteSettingsRecord.SingletonId, cancellationToken);
        return record?.ToDomain() ?? SiteSettings.Restore(SiteSettings.DefaultPack, 0);
    }

    public void Update(SiteSettings settings)
    {
        var record = context.Set<SiteSettingsRecord>().Local.FindEntry(SiteSettingsRecord.SingletonId)?.Entity
            ?? throw new InvalidOperationException("The site settings were not loaded in this unit of work; load them through the repository before updating them.");
        settings.CopyTo(record);
        context.ApplyOriginalVersion(record, settings.Version);
    }
}
