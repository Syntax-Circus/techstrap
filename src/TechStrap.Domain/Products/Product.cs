using TechStrap.Domain.Rules;
using TechStrap.Domain.Tickets;

namespace TechStrap.Domain.Products;

/// <summary>Customer-facing branding of a product (D-002). The accent is a stored <c>#RRGGBB</c>; derivation is in Contracts (D-025).</summary>
public sealed record ProductBranding
{
    public const string DefaultAccentColour = "#1F6FEB";

    // Private constructor and get-only properties: neither `new` nor `with` can build a branding that skipped validation.
    private ProductBranding(string displayName, string? logoPath, string accentColour, string? fromAddress, string? replyTo, string? tagline, string? uploadedLogo)
    {
        Tagline = tagline;
        UploadedLogo = uploadedLogo;
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

    /// <summary>One line of plain text shown on the landing card, or null (D-052).</summary>
    public string? Tagline { get; }

    /// <summary>The stored file name of the uploaded logo (<c>{32 hex}.{ext}</c> under <c>product-logos/</c>), never a URL; it supersedes <see cref="LogoPath"/> (D-052).</summary>
    public string? UploadedLogo { get; }

    /// <summary>Rebuilds branding that was validated when it was stored; persistence mappings use it, callers use <see cref="Create"/>.</summary>
    public static ProductBranding Restore(string displayName, string? logoPath, string accentColour, string? fromAddress, string? replyTo, string? tagline = null, string? uploadedLogo = null) =>
        new(displayName, logoPath, accentColour, fromAddress, replyTo, tagline, uploadedLogo);

    public static DomainResult<ProductBranding> Create(
        string? displayName,
        string? logoPath,
        string? accentColour,
        string? fromAddress,
        string? replyTo,
        string? tagline = null) =>
        Build(displayName, Guard.OptionalImageUrl(logoPath, DomainLimits.UrlMaxLength, "logo-path"), accentColour, fromAddress, replyTo, tagline, uploadedLogo: null);

    /// <summary>The same branding with the uploaded logo replaced (null removes it). The name is the store's own and is not validated here.</summary>
    public ProductBranding WithUploadedLogo(string? fileName) => new(DisplayName, LogoPath, AccentColour, FromAddress, ReplyTo, Tagline, fileName);

    /// <summary>
    /// Branding for an update of a stored product. The logo address the product already has is accepted as it is when the request carries it unchanged (compared after trimming), because
    /// products saved before the logo rule (a relative path, say) must stay editable: renaming one must not need a logo it never had. A different address, or one for a product that has none,
    /// is checked by the full rule; every other field is always checked. The uploaded logo is carried over from <paramref name="current"/>: the request cannot carry it (D-052), only the logo routes change it.
    /// </summary>
    public static DomainResult<ProductBranding> CreateForUpdate(
        ProductBranding current,
        string? displayName,
        string? logoPath,
        string? accentColour,
        string? fromAddress,
        string? replyTo,
        string? tagline = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        var unchanged = string.Equals(logoPath?.Trim(), current.LogoPath, StringComparison.Ordinal);
        var logo = unchanged
            ? DomainResult<string?>.Ok(current.LogoPath)
            : Guard.OptionalImageUrl(logoPath, DomainLimits.UrlMaxLength, "logo-path");
        return Build(displayName, logo, accentColour, fromAddress, replyTo, tagline, current.UploadedLogo);
    }

    private static DomainResult<ProductBranding> Build(string? displayName, DomainResult<string?> logo, string? accentColour, string? fromAddress, string? replyTo, string? tagline, string? uploadedLogo)
    {
        var line = Guard.OptionalTagline(tagline, DomainLimits.TaglineMaxLength, "tagline");
        var name = Guard.RequiredText(displayName, DomainLimits.NameMaxLength, "display-name");
        var accent = Guard.Colour(accentColour ?? DefaultAccentColour, "accent-colour");
        var from = Guard.OptionalEmail(fromAddress, "from-address");
        var reply = Guard.OptionalEmail(replyTo, "reply-to");

        return Guard.FirstError(name, logo, accent, from, reply, line) is { } error
            ? error
            : DomainResult<ProductBranding>.Ok(new ProductBranding(name.Value, logo.Value, accent.Value, from.Value, reply.Value, line.Value, uploadedLogo));
    }
}

/// <summary>A product (a customer application) that receives tickets. The number prefix is fixed at creation (D-009).</summary>
public sealed class Product
{
    private Product(Guid id, string key, string name, string numberPrefix, ProductBranding branding, bool isActive, uint version, string? portalHost, bool listedOnLanding, string? skinJson)
    {
        SkinJson = skinJson;
        ListedOnLanding = listedOnLanding;
        Id = id;
        Key = key;
        Name = name;
        NumberPrefix = numberPrefix;
        Branding = branding;
        IsActive = isActive;
        Version = version;
        PortalHost = portalHost;
    }

    public Guid Id { get; }

    /// <summary>URL slug, unique (used in public routes).</summary>
    public string Key { get; }

    public string Name { get; private set; }

    /// <summary>Upper-case prefix of the ticket numbers this product issues, unique.</summary>
    public string NumberPrefix { get; }

    public ProductBranding Branding { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>The public hostname this product's portal is served on (lower-case, unique), or null when it has none (D-050).</summary>
    public string? PortalHost { get; private set; }

    /// <summary>Whether the product appears on the Portal's landing page when it lists products (D-052). It changes nothing else: the product stays reachable by key and host.</summary>
    public bool ListedOnLanding { get; private set; }

    /// <summary>The product's skin as JSON (D-053): opaque to Domain, validated by the Contracts grammar in Application. Null means no skin.</summary>
    public string? SkinJson { get; private set; }

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
        return DomainResult<Product>.Ok(new Product(EntityId.New(clock), slug.Value, productName.Value, prefix!, branding ?? defaultBranding, isActive: true, version: 0, portalHost: null, listedOnLanding: true, skinJson: null));
    }

    public static Product Restore(Guid id, string key, string name, string numberPrefix, ProductBranding branding, bool isActive, uint version, string? portalHost = null, bool listedOnLanding = true, string? skinJson = null) =>
        new(id, key, name, numberPrefix, branding, isActive, version, portalHost, listedOnLanding, skinJson);

    /// <summary>Stores the skin JSON (D-053); null or whitespace clears it. Only the length is checked here; the grammar is validated in Application.</summary>
    public DomainResult SetSkinJson(string? json)
    {
        var text = string.IsNullOrWhiteSpace(json) ? null : json.Trim();
        if (text is { Length: > DomainLimits.SkinJsonMaxLength })
        {
            return DomainErrors.Validation("skin-too-long", $"The skin must be at most {DomainLimits.SkinJsonMaxLength} characters.", "skin");
        }

        SkinJson = text;
        return DomainResult.Ok();
    }

    /// <summary>Sets the portal hostname from user input (trimmed and lower-cased); a blank input clears it. Uniqueness across products is the caller's check.</summary>
    public DomainResult SetPortalHost(string? input)
    {
        if (!HostNameShape.TryNormalize(input, out var host))
        {
            return DomainErrors.Validation(
                "product-host-invalid",
                "Use a hostname such as support.example.com: letters, digits and hyphens, no scheme, port or path.",
                "portal-host");
        }

        PortalHost = host;
        return DomainResult.Ok();
    }

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

    /// <summary>Lists the product on the Portal's landing page or takes it off (D-052).</summary>
    public void SetListedOnLanding(bool listed) => ListedOnLanding = listed;

    /// <summary>Sets or clears (null) the uploaded logo's stored file name; the logo routes are the only callers.</summary>
    public void SetUploadedLogo(string? fileName) => Branding = Branding.WithUploadedLogo(fileName);
}
