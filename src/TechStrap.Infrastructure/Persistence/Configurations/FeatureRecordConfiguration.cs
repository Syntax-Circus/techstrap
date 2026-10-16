using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TechStrap.Domain.Rules;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Configurations;

internal sealed class FeatureRecordConfiguration : IEntityTypeConfiguration<FeatureRecord>
{
    public void Configure(EntityTypeBuilder<FeatureRecord> b)
    {
        b.ToTable("features");
        b.HasKey(f => f.Id);
        b.Property(f => f.Id).ValueGeneratedNever();
        b.Property(f => f.Slug).HasMaxLength(DomainLimits.KbSlugMaxLength).IsRequired();
        b.Property(f => f.Title).HasMaxLength(DomainLimits.KbTitleMaxLength).IsRequired();
        b.Property(f => f.DescriptionMarkdown).IsRequired();
        b.Property(f => f.PublicationState).HasEnumAsString().IsRequired();
        b.Property(f => f.ProgressStatus).HasEnumAsString().IsRequired();
        b.HasXminConcurrencyToken(f => f.Version);
        b.HasOne<ProductRecord>().WithMany().HasForeignKey(f => f.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(f => new { f.ProductId, f.Slug }).IsUnique();
        b.HasIndex(f => new { f.ProductId, f.PublicationState, f.UpdatedAt });
    }
}
