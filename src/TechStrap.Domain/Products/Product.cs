using TechStrap.Domain.Rules;
using TechStrap.Domain.Tickets;

namespace TechStrap.Domain.Products;

/// <summary>Customer-facing branding of a product (D-002). The accent is a stored <c>#RRGGBB</c>; derivation is in Contracts (D-025).</summary>
public sealed record ProductBranding(string DisplayName, string? LogoPath, string AccentColour, string? FromAddress, string? ReplyTo)
{
    public const string DefaultAccentColour = "#1F6FEB";

    public static DomainResult<ProductBranding> Create(
        string? displayName,
        string? logoPath,
        string? accentColour,
        string? fromAddress,
        string? replyTo)
    {
        var name = Guard.RequiredText(displayName, DomainLimits.NameMaxLength, "display-name");
        var logo = Guard.OptionalText(logoPath, DomainLimits.UrlMaxLength, "logo-path");
        var accent = Guard.Colour(accentColour ?? DefaultAccentColour, "accent-colour");
        var from = Guard.OptionalEmail(fromAddress, "from-address");
        var reply = Guard.OptionalEmail(replyTo, "reply-to");

        return Guard.FirstError(name, logo, accent, from, reply) is { } error
            ? error
            : DomainResult<ProductBranding>.Ok(new ProductBranding(name.Value, logo.Value, accent.Value, from.Value, reply.Value));
    }
}

/// <summary>A product (a customer application) that receives tickets. The number prefix is fixed at creation (D-009).</summary>
public sealed class Product
{
    private Product(Guid id, string key, string name, string numberPrefix, ProductBranding branding, bool isActive)
    {
        Id = id;
        Key = key;
        Name = name;
        NumberPrefix = numberPrefix;
        Branding = branding;
        IsActive = isActive;
    }

    public Guid Id { get; }

    /// <summary>URL slug, unique (used in public routes).</summary>
    public string Key { get; }

    public string Name { get; private set; }

    /// <summary>Upper-case prefix of the ticket numbers this product issues, unique.</summary>
    public string NumberPrefix { get; }

    public ProductBranding Branding { get; private set; }

    public bool IsActive { get; private set; }

    public static DomainResult<Product> Create(string? key, string? name, string? numberPrefix, ProductBranding? branding, TimeProvider clock)
    {
        var slug = Guard.Slug(key, DomainLimits.SlugMaxLength, "key");
        var productName = Guard.RequiredText(name, DomainLimits.NameMaxLength, "name");
        if (Guard.FirstError(slug, productName) is { } error)
        {
            return error;
        }

        var prefix = numberPrefix?.Trim();
        if (!TicketNumber.IsValidPrefix(prefix))
        {
            return DomainErrors.Validation("number-prefix-invalid", "A number prefix is 2 to 10 upper-case letters or digits and starts with a letter.", "number-prefix");
        }

        var defaultBranding = ProductBranding.Create(productName.Value, null, null, null, null).Value;
        return DomainResult<Product>.Ok(new Product(EntityId.New(clock), slug.Value, productName.Value, prefix!, branding ?? defaultBranding, isActive: true));
    }

    public static Product Restore(Guid id, string key, string name, string numberPrefix, ProductBranding branding, bool isActive) =>
        new(id, key, name, numberPrefix, branding, isActive);

    public DomainResult UpdateDetails(string? name, ProductBranding branding)
    {
        var productName = Guard.RequiredText(name, DomainLimits.NameMaxLength, "name");
        if (productName.IsFailure)
        {
            return productName.Error!;
        }

        Name = productName.Value;
        Branding = branding;
        return DomainResult.Ok();
    }

    public void SetActive(bool isActive) => IsActive = isActive;
}
