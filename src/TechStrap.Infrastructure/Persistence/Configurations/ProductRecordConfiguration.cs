using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TechStrap.Domain.Rules;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Configurations;

internal sealed class ProductRecordConfiguration : IEntityTypeConfiguration<ProductRecord>
{
    public void Configure(EntityTypeBuilder<ProductRecord> builder)
    {
        builder.ToTable("products");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Key).HasMaxLength(DomainLimits.SlugMaxLength).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(DomainLimits.NameMaxLength).IsRequired();
        builder.Property(p => p.NumberPrefix).HasMaxLength(DomainLimits.NumberPrefixMaxLength).IsRequired();
        builder.Property(p => p.DisplayName).HasMaxLength(DomainLimits.NameMaxLength).IsRequired();
        builder.Property(p => p.Logo).HasMaxLength(DomainLimits.UrlMaxLength);
        builder.Property(p => p.AccentColour).HasMaxLength(DomainLimits.ColourHexLength).IsRequired();
        builder.Property(p => p.FromAddress).HasMaxLength(DomainLimits.EmailMaxLength);
        builder.Property(p => p.ReplyTo).HasMaxLength(DomainLimits.EmailMaxLength);
        builder.HasXminConcurrencyToken(p => p.Version);
        builder.HasIndex(p => p.Key).IsUnique();
        builder.HasIndex(p => p.NumberPrefix).IsUnique();
    }
}

internal sealed class ProductApiKeyRecordConfiguration : IEntityTypeConfiguration<ProductApiKeyRecord>
{
    public void Configure(EntityTypeBuilder<ProductApiKeyRecord> builder)
    {
        builder.ToTable("product_api_keys");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).ValueGeneratedNever();
        builder.Property(k => k.Kind).HasEnumAsString().IsRequired();
        builder.Property(k => k.KeyHash).HasMaxLength(DomainLimits.HashMaxLength).IsRequired();
        builder.Property(k => k.KeyPrefix).HasMaxLength(DomainLimits.KeyPrefixMaxLength).IsRequired();
        builder.Property(k => k.Label).HasMaxLength(DomainLimits.LabelMaxLength);
        builder.HasOne<ProductRecord>().WithMany().HasForeignKey(k => k.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(k => k.KeyHash).IsUnique();
        builder.HasIndex(k => k.ProductId);
    }
}

internal sealed class ProductTicketSequenceRecordConfiguration : IEntityTypeConfiguration<ProductTicketSequenceRecord>
{
    public void Configure(EntityTypeBuilder<ProductTicketSequenceRecord> builder)
    {
        builder.ToTable("product_ticket_sequences");
        builder.HasKey(s => s.ProductId);
        builder.Property(s => s.ProductId).ValueGeneratedNever();
        builder.HasOne<ProductRecord>().WithOne().HasForeignKey<ProductTicketSequenceRecord>(s => s.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}
