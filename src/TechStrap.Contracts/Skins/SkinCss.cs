using System.Globalization;

namespace TechStrap.Contracts.Skins;

/// <summary>
/// The custom properties and preset attributes a resolved skin emits. Only values that differ from Classic are emitted, so an unthemed page
/// carries nothing and stays pixel-identical. The values are validated tokens, never raw CSS.
/// </summary>
public static class SkinCss
{
    /// <summary>
    /// The CSS custom properties: the accent trio first when the brand is explicit (<c>--ts-accent</c>, <c>--ts-on-accent</c>, <c>--ts-accent-ink</c>),
    /// then each token that differs from Classic.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Properties(ResolvedSkin skin)
    {
        ArgumentNullException.ThrowIfNull(skin);

        var t = skin.Tokens;
        var c = SkinPacks.Classic.Tokens;
        var list = new List<KeyValuePair<string, string>>();
        if (skin.BrandIsExplicit)
        {
            list.Add(new("--ts-accent", t.Brand));
            list.Add(new("--ts-on-accent", skin.OnBrand));
            list.Add(new("--ts-accent-ink", skin.BrandInk));
        }

        Add(list, "--p-bg", t.Background, c.Background);
        Add(list, "--ts-surface", t.Surface, c.Surface);
        Add(list, "--p-ink", t.Ink, c.Ink);
        Add(list, "--p-ink2", t.Muted, c.Muted);
        Add(list, "--p-line", t.Border, c.Border);
        if (t.Chrome != c.Chrome)
        {
            list.Add(new("--ts-chrome", t.Chrome));
            list.Add(new("--ts-on-chrome", skin.OnChrome));
        }

        Add(list, "--ts-focus", t.Focus, c.Focus);
        if (t.Radius != c.Radius)
        {
            list.Add(new("--ts-radius", SkinValues.RadiusRem(t.Radius)));
        }

        if (t.BorderWidth != c.BorderWidth)
        {
            list.Add(new("--ts-border-w", t.BorderWidth.ToString(CultureInfo.InvariantCulture) + "px"));
        }

        if (t.HeadingFont != c.HeadingFont)
        {
            list.Add(new("--ts-font-heading", SkinFonts.Stack(t.HeadingFont)));
        }

        if (t.BodyFont != c.BodyFont)
        {
            list.Add(new("--ts-font-body", SkinFonts.Stack(t.BodyFont)));
        }

        return list;
    }

    /// <summary>The preset attributes: <c>data-ts-shadow</c>, <c>data-ts-button</c>, <c>data-ts-header</c>, each only when not the Classic value.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Attributes(ResolvedSkin skin)
    {
        ArgumentNullException.ThrowIfNull(skin);

        var t = skin.Tokens;
        var list = new List<KeyValuePair<string, string>>();
        if (SkinValues.IsShadow(t.Shadow) && t.Shadow != SkinValues.ShadowNone)
        {
            list.Add(new("data-ts-shadow", t.Shadow));
        }

        if (SkinValues.IsButton(t.Button) && t.Button != SkinValues.ButtonFlat)
        {
            list.Add(new("data-ts-button", t.Button));
        }

        if (SkinValues.IsHeader(t.Header) && t.Header != SkinValues.HeaderPlain)
        {
            list.Add(new("data-ts-header", t.Header));
        }

        return list;
    }

    private static void Add(List<KeyValuePair<string, string>> list, string name, string value, string classic)
    {
        if (!string.Equals(value, classic, StringComparison.Ordinal))
        {
            list.Add(new(name, value));
        }
    }
}
