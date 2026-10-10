using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TechStrap.Domain.Rules;
using TechStrap.Domain.Settings;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Configurations;

internal sealed class SiteSettingsRecordConfiguration : IEntityTypeConfiguration<SiteSettingsRecord>
{
    public void Configure(EntityTypeBuilder<SiteSettingsRecord> builder)
    {
        builder.ToTable("site_settings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.DefaultPack).HasMaxLength(DomainLimits.PackKeyMaxLength).IsRequired();
        builder.HasXminConcurrencyToken(s => s.Version);

        // The one row exists from the migration onwards (D-053).
        builder.HasData(new SiteSettingsRecord { Id = SiteSettingsRecord.SingletonId, DefaultPack = SiteSettings.DefaultPack });
    }
}
