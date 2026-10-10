using TechStrap.Domain.Products;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Mapping;

/// <summary>Maps between <see cref="ProductRecord"/> / <see cref="ProductApiKeyRecord"/> and the Domain types (D-026).</summary>
internal static class ProductMappings
{
    public static Product ToDomain(this ProductRecord record) =>
        Product.Restore(
            record.Id,
            record.Key,
            record.Name,
            record.NumberPrefix,
            ProductBranding.Restore(record.DisplayName, record.Logo, record.AccentColour, record.FromAddress, record.ReplyTo, record.Tagline, record.UploadedLogo),
            record.IsActive,
            record.Version,
            record.PortalHost,
            record.ListedOnLanding,
            record.Skin);

    public static ProductRecord ToRecord(this Product product)
    {
        var record = new ProductRecord
        {
            Id = product.Id,
            Key = product.Key,
            NumberPrefix = product.NumberPrefix,
        };
        product.CopyTo(record);
        return record;
    }

    /// <summary>Copies the mutable fields. The key, prefix and the ticket counter are never overwritten.</summary>
    public static void CopyTo(this Product product, ProductRecord record)
    {
        record.Name = product.Name;
        record.DisplayName = product.Branding.DisplayName;
        record.Logo = product.Branding.LogoPath;
        record.AccentColour = product.Branding.AccentColour;
        record.FromAddress = product.Branding.FromAddress;
        record.ReplyTo = product.Branding.ReplyTo;
        record.IsActive = product.IsActive;
        record.PortalHost = product.PortalHost;
        record.Tagline = product.Branding.Tagline;
        record.UploadedLogo = product.Branding.UploadedLogo;
        record.ListedOnLanding = product.ListedOnLanding;
        record.Skin = product.SkinJson;
    }

    public static ProductApiKey ToDomain(this ProductApiKeyRecord record) =>
        ProductApiKey.Restore(
            record.Id, record.ProductId, record.Kind, record.KeyPrefix, record.KeyHash, record.Label, record.CreatedAt, record.RevokedAt, record.LastUsedAt);

    public static ProductApiKeyRecord ToRecord(this ProductApiKey key) => new()
    {
        Id = key.Id,
        ProductId = key.ProductId,
        Kind = key.Kind,
        KeyPrefix = key.KeyPrefix,
        KeyHash = key.KeyHash,
        Label = key.Label,
        CreatedAt = key.CreatedAt,
        RevokedAt = key.RevokedAt,
        LastUsedAt = key.LastUsedAt,
    };

    public static void CopyTo(this ProductApiKey key, ProductApiKeyRecord record)
    {
        record.RevokedAt = key.RevokedAt;
        record.LastUsedAt = key.LastUsedAt;
    }
}
