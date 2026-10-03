using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TechStrap.Domain.Rules;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Configurations;

internal sealed class KbCategoryRecordConfiguration : IEntityTypeConfiguration<KbCategoryRecord>
{
    public void Configure(EntityTypeBuilder<KbCategoryRecord> builder)
    {
        builder.ToTable("kb_categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(DomainLimits.NameMaxLength).IsRequired();
        builder.Property(c => c.Slug).HasMaxLength(DomainLimits.KbSlugMaxLength).IsRequired();
        builder.HasOne<ProductRecord>().WithMany().HasForeignKey(c => c.ProductId).OnDelete(DeleteBehavior.Restrict);

        // Shared categories (null product) share one slug space: nulls count as equal.
        builder.HasIndex(c => new { c.ProductId, c.Slug }).IsUnique().AreNullsDistinct(false);
    }
}

internal sealed class KbArticleRecordConfiguration : IEntityTypeConfiguration<KbArticleRecord>
{
    public void Configure(EntityTypeBuilder<KbArticleRecord> builder)
    {
        builder.ToTable("kb_articles");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Slug).HasMaxLength(DomainLimits.KbSlugMaxLength).IsRequired();
        builder.Property(a => a.Title).HasMaxLength(DomainLimits.KbTitleMaxLength).IsRequired();
        builder.Property(a => a.Summary).HasMaxLength(DomainLimits.KbSummaryMaxLength);
        builder.Property(a => a.BodyMarkdown).IsRequired();
        builder.Property(a => a.Status).HasEnumAsString().IsRequired();
        builder.HasXminConcurrencyToken(a => a.Version);
        builder.HasOne<ProductRecord>().WithMany().HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<KbCategoryRecord>().WithMany().HasForeignKey(a => a.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AgentRecord>().WithMany().HasForeignKey(a => a.AuthorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.ProductId, a.Slug }).IsUnique().AreNullsDistinct(false);
        builder.HasIndex(a => new { a.Status, a.PublishedAt });
        builder.HasIndex(a => a.CategoryId);
    }
}

internal sealed class TicketArticleRecordConfiguration : IEntityTypeConfiguration<TicketArticleRecord>
{
    public void Configure(EntityTypeBuilder<TicketArticleRecord> builder)
    {
        builder.ToTable("ticket_articles");
        builder.HasKey(l => new { l.MessageId, l.ArticleId });
        builder.HasOne<TicketRecord>().WithMany().HasForeignKey(l => l.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MessageRecord>().WithMany().HasForeignKey(l => l.MessageId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<KbArticleRecord>().WithMany().HasForeignKey(l => l.ArticleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(l => l.TicketId);
        builder.HasIndex(l => l.ArticleId);
    }
}
