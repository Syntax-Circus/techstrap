using TechStrap.Domain.Rules;
using TechStrap.Domain.Tickets;

namespace TechStrap.Domain.Products;

/// <summary>Customer-facing branding of a product (D-002). The accent is a stored <c>#RRGGBB</c>; derivation is in Contracts (D-025).</summary>
public sealed record ProductBranding
{
    public const string DefaultAccentColour = "#1F6FEB";

    // Private constructor and get-only properties: neither `new` nor `with` can build a branding that skipped validation.
    private ProductBranding(string displayName, string? logoPath, string accentColour, string? fromAddress, string? replyTo)
    {
        DisplayName = displayName;
        LogoPath = logoPath;
        AccentColour = accentColour;
        FromAddress = fromAddress;
        ReplyTo = replyTo;
    }

    public string DisplayName { get; }

    public string? LogoPath { get; }

    public string AccentColour { get; }

    public string? FromAddress { get; }

    public string? ReplyTo { get; }

    /// <summary>Rebuilds branding that was validated when it was stored; persistence mappings use it, callers use <see cref="Create"/>.</summary>
    public static ProductBranding Restore(string displayName, string? logoPath, string accentColour, string? fromAddress, string? replyTo) =>
        new(displayName, logoPath, accentColour, fromAddress, replyTo);

    public static DomainResult<ProductBranding> Create(
        string? displayName,
        string? logoPath,
        string? accentColour,
        string? fromAddress,
        string? replyTo) =>
        Build(displayName, Guard.OptionalImageUrl(logoPath, DomainLimits.UrlMaxLength, "logo-path"), accentColour, fromAddress, replyTo);

    /// <summary>
    /// Branding for an update of a stored product. The logo address the product already has is accepted as it is when the request carries it unchanged (compared after trimming), because
    /// products saved before the logo rule (a relative path, say) must stay editable: renaming one must not need a logo it never had. A different address, or one for a product that has none,
    /// is checked by the full rule; every other field is always checked.
    /// </summary>
    public static DomainResult<ProductBranding> CreateForUpdate(
        ProductBranding current,
        string? displayName,
        string? logoPath,
        string? accentColour,
        string? fromAddress,
        string? replyTo)
    {
        ArgumentNullException.ThrowIfNull(current);
        var unchanged = string.Equals(logoPath?.Trim(), current.LogoPath, StringComparison.Ordinal);
        var logo = unchanged
            ? DomainResult<string?>.Ok(current.LogoPath)
            : Guard.OptionalImageUrl(logoPath, DomainLimits.UrlMaxLength, "logo-path");
        return Build(displayName, logo, accentColour, fromAddress, replyTo);
    }

    private static DomainResult<ProductBranding> Build(string? displayName, DomainResult<string?> logo, string? accentColour, string? fromAddress, string? replyTo)
    {
        var name = Guard.RequiredText(displayName, DomainLimits.NameMaxLength, "display-name");
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
    private Product(Guid id, string key, string name, string numberPrefix, ProductBranding branding, bool isActive, uint version)
    {
        Id = id;
        Key = key;
        Name = name;
        NumberPrefix = numberPrefix;
        Branding = branding;
        IsActive = isActive;
        Version = version;
    }

    public Guid Id { get; }

    /// <summary>URL slug, unique (used in public routes).</summary>
    public string Key { get; }

    public string Name { get; private set; }

    /// <summary>Upper-case prefix of the ticket numbers this product issues, unique.</summary>
    public string NumberPrefix { get; }

    public ProductBranding Branding { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>
    /// Opaque optimistic-concurrency token as loaded (Postgres <c>xmin</c>); 0 for a product that was never stored. The persistence layer
    /// applies it as the original token when the product is updated, so a stale copy is rejected on save.
    /// </summary>
    public uint Version { get; }

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
        return DomainResult<Product>.Ok(new Product(EntityId.New(clock), slug.Value, productName.Value, prefix!, branding ?? defaultBranding, isActive: true, version: 0));
    }

    public static Product Restore(Guid id, string key, string name, string numberPrefix, ProductBranding branding, bool isActive, uint version) =>
        new(id, key, name, numberPrefix, branding, isActive, version);

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
