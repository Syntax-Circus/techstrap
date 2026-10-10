using TechStrap.Contracts.Branding;
using TechStrap.Contracts.Products;

namespace TechStrap.Portal.Products;

/// <summary>
/// What a page needs to look like one product's own, and nothing it does not (D-045; there is no <c>BrandingThemeFactory</c>). A stored product is data an agent typed, and a logo stored before the
/// Admin validated it was never checked, so it is re-checked here, once, for every page:
/// <list type="bullet">
/// <item>The accent is kept only when <see cref="ProductAccent.TryDerive"/> accepts it, in that rule's own spelling. <c>AccentScope</c> derives the on-accent and ink colours from it with the same
/// single implementation, so none of the DTO's three colour strings is ever written to a style attribute as it arrived.</item>
/// <item>The logo is kept only when <see cref="BrandingRules.IsAcceptableLogoUrl"/> accepts it and it is https, or http to <c>localhost</c> or <c>127.0.0.1</c> when
/// <paramref name="allowLoopbackImages"/> is set (Development), which is exactly what the Content-Security-Policy's <c>img-src</c> allows (<c>TechStrapCsp.ForBlazorApp</c>).</item>
/// </list>
/// The name is kept as text; every renderer encodes it, and no page may render it as markup.
/// </summary>
public sealed record ProductThemeViewModel(string Key, string DisplayName, string? Accent, string? LogoUrl)
{
    public static ProductThemeViewModel From(PublicProductDto product, bool allowLoopbackImages)
    {
        ArgumentNullException.ThrowIfNull(product);
        var name = product.DisplayName?.Trim();
        return new ProductThemeViewModel(
            product.Key,
            string.IsNullOrEmpty(name) ? product.Key : name,
            ProductAccent.TryDerive(product.AccentColour, out var colours) ? colours.Accent : null,
            AcceptableLogoUrl(product.LogoPath, allowLoopbackImages));
    }

    internal static string? AcceptableLogoUrl(string? logoUrl, bool allowLoopbackImages)
    {
        var text = logoUrl?.Trim();
        if (string.IsNullOrEmpty(text) || !BrandingRules.IsAcceptableLogoUrl(text))
        {
            return null;
        }

        // Acceptable and non-blank means an absolute address with a host: https, or http to a loopback host.
        var uri = new Uri(text, UriKind.Absolute);
        return uri.Scheme == Uri.UriSchemeHttps || allowLoopbackImages ? uri.AbsoluteUri : null;
    }
}
