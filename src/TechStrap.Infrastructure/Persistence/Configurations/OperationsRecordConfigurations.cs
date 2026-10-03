using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TechStrap.Domain.Rules;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Configurations;

internal sealed class AdminEventRecordConfiguration : IEntityTypeConfiguration<AdminEventRecord>
{
    public void Configure(EntityTypeBuilder<AdminEventRecord> builder)
    {
        builder.ToTable("admin_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Type).HasEnumAsString().IsRequired();
        builder.Property(e => e.SubjectType).HasEnumAsString().IsRequired();
        builder.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(e => e.OccurredAt);
        builder.HasIndex(e => new { e.SubjectType, e.SubjectId });
    }
}

internal sealed class EmailOutboxRecordConfiguration : IEntityTypeConfiguration<EmailOutboxRecord>
{
    public void Configure(EntityTypeBuilder<EmailOutboxRecord> builder)
    {
        builder.ToTable("email_outbox");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Kind).HasMaxLength(DomainLimits.KindMaxLength).IsRequired();
        builder.Property(e => e.ToAddress).HasMaxLength(DomainLimits.EmailMaxLength).IsRequired();
        builder.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.Status).HasEnumAsString().IsRequired();
        builder.Property(e => e.ClaimedBy).HasMaxLength(DomainLimits.NameMaxLength);
        builder.Property(e => e.LastError).HasMaxLength(DomainLimits.ErrorMaxLength);

        // The worker polls only due Pending rows, so the partial index keeps that scan small.
        builder.HasIndex(e => e.NextAttemptAt).HasDatabaseName("ix_email_outbox_next_attempt_at_when_pending").HasFilter("status = 'Pending'");
        // The other claim branch: a Sending row whose lease expired (a crashed worker), or has none.
        builder.HasIndex(e => e.LockedUntil).HasDatabaseName("ix_email_outbox_locked_until_when_sending").HasFilter("status = 'Sending'");
        // Dead letters are rare and listed newest first. A plain status index is not selective enough for the claim query (the planner prefers it over the two partial indexes above).
        builder.HasIndex(e => e.CreatedAt).HasDatabaseName("ix_email_outbox_created_at_when_dead_lettered").HasFilter("status = 'DeadLettered'");
        builder.HasIndex(e => e.TicketId);
    }
}

internal sealed class IntakeIdempotencyKeyRecordConfiguration : IEntityTypeConfiguration<IntakeIdempotencyKeyRecord>
{
    public void Configure(EntityTypeBuilder<IntakeIdempotencyKeyRecord> builder)
    {
        builder.ToTable("intake_idempotency_keys");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).ValueGeneratedNever();
        builder.Property(k => k.KeyHash).HasMaxLength(DomainLimits.HashMaxLength).IsRequired();
        builder.Property(k => k.Response).HasColumnType("jsonb").IsRequired();
        builder.HasOne<ProductApiKeyRecord>().WithMany().HasForeignKey(k => k.ApiKeyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TicketRecord>().WithMany().HasForeignKey(k => k.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(k => new { k.ApiKeyId, k.KeyHash }).IsUnique();
        builder.HasIndex(k => k.CreatedAt);
        builder.HasIndex(k => k.TicketId);
    }
}
