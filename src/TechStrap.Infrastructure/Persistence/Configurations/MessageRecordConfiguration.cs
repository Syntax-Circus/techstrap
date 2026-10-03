using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TechStrap.Domain.Rules;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Configurations;

internal sealed class MessageRecordConfiguration : IEntityTypeConfiguration<MessageRecord>
{
    public void Configure(EntityTypeBuilder<MessageRecord> builder)
    {
        builder.ToTable("messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.AuthorType).HasEnumAsString().IsRequired();
        builder.Property(m => m.Visibility).HasEnumAsString().IsRequired();
        builder.Property(m => m.Body).IsRequired();
        builder.Property(m => m.MessageId).HasMaxLength(DomainLimits.MessageIdMaxLength);
        builder.Property(m => m.InReplyTo).HasMaxLength(DomainLimits.MessageIdMaxLength);
        builder.HasOne<TicketRecord>().WithMany().HasForeignKey(m => m.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => new { m.TicketId, m.CreatedAt });
    }
}

internal sealed class AttachmentRecordConfiguration : IEntityTypeConfiguration<AttachmentRecord>
{
    public void Configure(EntityTypeBuilder<AttachmentRecord> builder)
    {
        builder.ToTable("attachments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.FileName).HasMaxLength(DomainLimits.FileNameMaxLength).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(DomainLimits.ContentTypeMaxLength).IsRequired();
        builder.Property(a => a.StorageKey).HasMaxLength(DomainLimits.StorageKeyMaxLength).IsRequired();
        builder.HasOne<TicketRecord>().WithMany().HasForeignKey(a => a.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MessageRecord>().WithMany().HasForeignKey(a => a.MessageId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => a.MessageId);
        builder.HasIndex(a => a.TicketId);
    }
}
