using System.Text.RegularExpressions;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Branding;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Features.Settings.Products;

/// <summary>
/// The form model of the product editor. It holds exactly what is on screen, validates it with the server's rules (the colour with the same Contracts constant as the API's pattern),
/// and builds the requests. <see cref="IsActive"/> is always carried (the update request has a non-nullable flag, and an omitted one would deactivate the product) and <see cref="Version"/>
/// is the one the product was loaded with.
/// </summary>
internal sealed partial class ProductEditorViewModel
{
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^[A-Z][A-Z0-9]{1,9}$")]
    private static partial Regex NumberPrefixPattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string NumberPrefix { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string LogoPath { get; set; } = string.Empty;

    /// <summary>The logo address the product was loaded with. An address saved before the logo rule (a relative path) is accepted again while the field still holds it, as the API accepts it.</summary>
    public string OriginalLogoPath { get; set; } = string.Empty;

    public string AccentColour { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;

    public string ReplyTo { get; set; } = string.Empty;

    public string PortalHost { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public uint Version { get; set; }

    public static ProductEditorViewModel From(ProductDto product) => new()
    {
        Key = product.Key,
        Name = product.Name,
        NumberPrefix = product.NumberPrefix,
        DisplayName = product.Branding.DisplayName,
        LogoPath = product.Branding.LogoPath ?? string.Empty,
        OriginalLogoPath = product.Branding.LogoPath ?? string.Empty,
        AccentColour = product.Branding.AccentColour,
        FromAddress = product.Branding.FromAddress ?? string.Empty,
        ReplyTo = product.Branding.ReplyTo ?? string.Empty,
        PortalHost = product.PortalHost ?? string.Empty,
        IsActive = product.IsActive,
        Version = product.Version,
    };

    public CreateProductRequest ToCreateRequest() => new(Key.Trim(), Name.Trim(), NumberPrefix.Trim(), ToBranding(), NormalisedHost());

    public UpdateProductRequest ToUpdateRequest() => new(Name.Trim(), ToBranding(), IsActive, Version, NormalisedHost());

    private ProductBrandingRequest ToBranding() =>
        new(DisplayName.Trim(), Blank(LogoPath), Blank(AccentColour), Blank(FromAddress), Blank(ReplyTo));

    // Blank (or an invalid value, which Check has already refused) goes as null; a valid host goes trimmed and lower-case.
    private string? NormalisedHost() => ProductHostRules.TryNormalize(PortalHost, out var host) ? host : null;

    private bool LogoUnchanged => OriginalLogoPath.Length > 0 && string.Equals(LogoPath.Trim(), OriginalLogoPath, StringComparison.Ordinal);

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The message for one field, or null when it is fine. Key and ticket number prefix are checked only when the product is being created.</summary>
    public string? Check(string field, bool creating) => field switch
    {
        ApiFields.Key when creating => string.IsNullOrWhiteSpace(Key) ? ProductsCopy.KeyRequired
            : Key.Trim().Length > ProductFields.KeyMaxLength || !KeyPattern().IsMatch(Key.Trim()) ? ProductsCopy.KeyInvalid : null,
        ApiFields.Name => string.IsNullOrWhiteSpace(Name) ? ProductsCopy.NameRequired
            : Name.Trim().Length > ProductFields.NameMaxLength ? ProductsCopy.NameTooLong : null,
        ApiFields.NumberPrefix when creating => NumberPrefixPattern().IsMatch(NumberPrefix.Trim()) ? null : ProductsCopy.NumberPrefixInvalid,
        ApiFields.DisplayName => string.IsNullOrWhiteSpace(DisplayName) ? ProductsCopy.DisplayNameRequired
            : DisplayName.Trim().Length > ProductFields.DisplayNameMaxLength ? ProductsCopy.NameTooLong : null,
        ApiFields.LogoPath => BrandingRules.IsAcceptableLogoUrl(LogoPath) || LogoUnchanged ? null : ProductsCopy.LogoInvalid,
        ApiFields.AccentColour => string.IsNullOrWhiteSpace(AccentColour) || Regex.IsMatch(AccentColour.Trim(), BrandingRules.ColourPattern) ? null : ProductsCopy.AccentInvalid,
        ApiFields.FromAddress => IsEmailOrBlank(FromAddress) ? null : ProductsCopy.EmailInvalid,
        ApiFields.ReplyTo => IsEmailOrBlank(ReplyTo) ? null : ProductsCopy.EmailInvalid,
        ApiFields.PortalHost => ProductHostRules.TryNormalize(PortalHost, out _) ? null : ProductsCopy.PortalHostInvalid,
        _ => null,
    };

    private static bool IsEmailOrBlank(string value) =>
        string.IsNullOrWhiteSpace(value) || (value.Trim().Length <= ProductFields.EmailMaxLength && EmailPattern().IsMatch(value.Trim()));
}
