using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TechStrap.Domain.Rules;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Configurations;

internal sealed class TicketRecordConfiguration : IEntityTypeConfiguration<TicketRecord>
{
    public void Configure(EntityTypeBuilder<TicketRecord> builder)
    {
        builder.ToTable("tickets");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Number).HasMaxLength(DomainLimits.TicketNumberMaxLength).IsRequired();
        builder.Property(t => t.Subject).HasMaxLength(DomainLimits.SubjectMaxLength).IsRequired();
        builder.Property(t => t.Status).HasEnumAsString().IsRequired();
        builder.Property(t => t.Priority).HasEnumAsString().IsRequired();
        builder.Property(t => t.Channel).HasEnumAsString().IsRequired();
        builder.Property(t => t.Metadata).HasColumnType("jsonb");
        builder.Property(t => t.CustomFields).HasColumnType("jsonb");
        builder.HasXminConcurrencyToken(t => t.Version);
        builder.HasWeightedSearchVector(t => t.SearchVector, ("subject", FullTextSearch.WeightA));

        builder.HasOne<ProductRecord>().WithMany().HasForeignKey(t => t.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RequesterRecord>().WithMany().HasForeignKey(t => t.RequesterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AgentRecord>().WithMany().HasForeignKey(t => t.AssigneeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TicketRecord>().WithMany().HasForeignKey(t => t.ParentTicketId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(t => t.Tags).WithOne().HasForeignKey(tag => tag.TicketId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.Number).IsUnique();
        builder.HasIndex(t => new { t.ProductId, t.Status, t.LastActivityAt });
        builder.HasIndex(t => new { t.AssigneeId, t.Status });
        builder.HasIndex(t => t.RequesterId);
        builder.HasIndex(t => t.ParentTicketId);
        builder.HasIndex(t => t.SolvedAt).HasDatabaseName("ix_tickets_solved_at_when_solved").HasFilter("status = 'Solved'");
        builder.HasIndex(t => t.LastActivityAt).HasDatabaseName("ix_tickets_last_activity_at_when_spam").HasFilter("is_spam = true");
    }
}

internal sealed class TicketTagRecordConfiguration : IEntityTypeConfiguration<TicketTagRecord>
{
    public void Configure(EntityTypeBuilder<TicketTagRecord> builder)
    {
        builder.ToTable("ticket_tags");
        builder.HasKey(t => new { t.TicketId, t.TagId });
        builder.HasOne<TagRecord>().WithMany().HasForeignKey(t => t.TagId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => t.TagId);
    }
}
