using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TechStrap.Domain.Rules;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Configurations;

internal sealed class AgentRecordConfiguration : IEntityTypeConfiguration<AgentRecord>
{
    public void Configure(EntityTypeBuilder<AgentRecord> builder)
    {
        builder.ToTable("agents");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.OidcSubject).HasMaxLength(DomainLimits.OidcSubjectMaxLength).IsRequired();
        builder.Property(a => a.Name).HasMaxLength(DomainLimits.NameMaxLength);
        builder.Property(a => a.Email).HasMaxLength(DomainLimits.EmailMaxLength).IsRequired();
        builder.Property(a => a.Role).HasEnumAsString().IsRequired();
        builder.Property(a => a.PublicDisplayName).HasMaxLength(DomainLimits.PublicDisplayNameMaxLength);
        builder.HasIndex(a => a.OidcSubject).IsUnique();
        builder.HasIndex(a => a.Email);
    }
}

internal sealed class AgentNotificationPreferenceRecordConfiguration : IEntityTypeConfiguration<AgentNotificationPreferenceRecord>
{
    public void Configure(EntityTypeBuilder<AgentNotificationPreferenceRecord> builder)
    {
        builder.ToTable("agent_notification_preferences");
        builder.HasKey(p => new { p.AgentId, p.ProductId });
        builder.HasOne<AgentRecord>().WithMany().HasForeignKey(p => p.AgentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ProductRecord>().WithMany().HasForeignKey(p => p.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}
