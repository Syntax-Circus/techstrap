using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TechStrap.Domain.Rules;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Configurations;

internal sealed class TagRecordConfiguration : IEntityTypeConfiguration<TagRecord>
{
    public void Configure(EntityTypeBuilder<TagRecord> builder)
    {
        builder.ToTable("tags");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Slug).HasMaxLength(DomainLimits.SlugMaxLength).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(DomainLimits.TagNameMaxLength).IsRequired();
        builder.Property(t => t.Colour).HasMaxLength(7).IsRequired();
        builder.HasIndex(t => t.Slug).IsUnique();
    }
}
