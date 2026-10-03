using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TechStrap.Domain.Rules;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Configurations;

internal sealed class TicketEventRecordConfiguration : IEntityTypeConfiguration<TicketEventRecord>
{
    public void Configure(EntityTypeBuilder<TicketEventRecord> builder)
    {
        builder.ToTable("ticket_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Type).HasEnumAsString().IsRequired();
        builder.Property(e => e.ActorType).HasEnumAsString().IsRequired();
        builder.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();

        // Hard-deleting a ticket (Admin only, D-006) removes its events in the database; the application never deletes one.
        builder.HasOne<TicketRecord>().WithMany().HasForeignKey(e => e.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.TicketId, e.OccurredAt });
    }
}

internal sealed class TicketAccessTokenRecordConfiguration : IEntityTypeConfiguration<TicketAccessTokenRecord>
{
    public void Configure(EntityTypeBuilder<TicketAccessTokenRecord> builder)
    {
        builder.ToTable("ticket_access_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.TokenHash).HasMaxLength(DomainLimits.HashMaxLength).IsRequired();
        builder.HasOne<TicketRecord>().WithMany().HasForeignKey(t => t.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<RequesterRecord>().WithMany().HasForeignKey(t => t.RequesterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => new { t.TicketId, t.RequesterId });
    }
}
