using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TechStrap.Application.Webhooks;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Configurations;

internal sealed class WebhookSettingsRecordConfiguration : IEntityTypeConfiguration<WebhookSettingsRecord>
{
    public void Configure(EntityTypeBuilder<WebhookSettingsRecord> b)
    {
        b.ToTable("webhook_settings");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Mode).HasMaxLength(16);
        b.Property(x => x.Endpoints).HasColumnType("jsonb");
        b.Property(x => x.Version).IsConcurrencyToken();
    }
}

internal sealed class WebhookDeliveryRecordConfiguration : IEntityTypeConfiguration<WebhookDeliveryRecord>
{
    public void Configure(EntityTypeBuilder<WebhookDeliveryRecord> b)
    {
        b.ToTable("webhook_deliveries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => new { x.EventId, x.EndpointId }).IsUnique();
        b.HasIndex(x => new { x.Status, x.NextAttemptAt });
        b.HasIndex(x => new { x.Status, x.LockedUntil });
        b.HasIndex(x => new { x.ProductId, x.CreatedAt });
        b.Property(x => x.Url).HasMaxLength(WebhookPolicy.MaxUrlLength);
        b.Property(x => x.EndpointName).HasMaxLength(WebhookPolicy.MaxNameLength);
        b.Property(x => x.Format).HasMaxLength(16);
        b.Property(x => x.EventType).HasMaxLength(40);
        b.Property(x => x.Status).HasMaxLength(16).IsConcurrencyToken();
        b.Property(x => x.LastError).HasMaxLength(100);
        b.Property(x => x.Payload).HasColumnType("jsonb");
        b.Property(x => x.ClaimId).IsConcurrencyToken();
        b.HasOne<TicketRecord>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
    }
}
