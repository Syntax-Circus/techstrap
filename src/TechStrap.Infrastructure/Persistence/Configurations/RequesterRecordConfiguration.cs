using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TechStrap.Domain.Rules;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Configurations;

internal sealed class RequesterRecordConfiguration : IEntityTypeConfiguration<RequesterRecord>
{
    public void Configure(EntityTypeBuilder<RequesterRecord> builder)
    {
        builder.ToTable("requesters");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        // citext: the database itself compares email addresses case-insensitively.
        builder.Property(r => r.Email).HasColumnType("citext").IsRequired();
        builder.Property(r => r.Name).HasMaxLength(DomainLimits.NameMaxLength);
        builder.Property(r => r.ExternalUserRef).HasMaxLength(DomainLimits.OidcSubjectMaxLength);
        builder.HasXminConcurrencyToken(r => r.Version);
        builder.HasIndex(r => r.Email).IsUnique();
        builder.HasIndex(r => r.ExternalUserRef);
    }
}
