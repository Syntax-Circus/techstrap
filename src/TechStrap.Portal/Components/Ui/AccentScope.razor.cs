using Microsoft.AspNetCore.Components;
using TechStrap.Contracts.Branding;
using TechStrap.Contracts.Skins;

namespace TechStrap.Portal.Components.Ui;

/// <summary>
/// The Portal's one style carrier: it applies a product's look to everything inside it, at runtime, as custom properties on its <c>style</c> attribute and preset attributes (<c>data-ts-shadow</c>, <c>data-ts-button</c>,
/// <c>data-ts-header</c>), so the policy's <c>style-src 'self'</c> (no inline <c>&lt;style&gt;</c>) is never touched (D-053).
/// <list type="bullet">
/// <item><see cref="Skin"/> (when set it wins): the properties and attributes come only from <see cref="SkinCss"/>, which emits validated tokens of a <see cref="ResolvedSkin"/> and only those that differ from Classic, so a page
/// with no skin carries no <c>style</c> attribute at all and is byte for byte what it was.</item>
/// <item><see cref="Accent"/> (the original accent-only use, kept): the three properties of docs/BRAND.md section 22, derived by <see cref="ProductAccent.TryDerive"/>, which accepts nothing but #RRGGBB.</item>
/// </list>
/// No value is ever taken from a raw string. A missing or malformed accent sets nothing and the stylesheet fallbacks apply.
/// </summary>
public partial class AccentScope
{
    private string? _style;
    private Dictionary<string, object>? _attributes;

    [Parameter]
    public string? Accent { get; set; }

    /// <summary>The resolved skin to apply; when set, <see cref="Accent"/> is ignored (the resolver already folded the accent in).</summary>
    [Parameter]
    public ResolvedSkin? Skin { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected override void OnParametersSet()
    {
        if (Skin is not null)
        {
            var properties = SkinCss.Properties(Skin);
            _style = properties.Count == 0 ? null : string.Join(';', properties.Select(p => $"{p.Key}:{p.Value}"));
            var attributes = SkinCss.Attributes(Skin);
            _attributes = attributes.Count == 0 ? null : attributes.ToDictionary(a => a.Key, a => (object)a.Value);
            return;
        }

        _style = ProductAccent.TryDerive(Accent, out var colors)
            ? $"--ts-accent:{colors.Accent};--ts-on-accent:{colors.OnAccent};--ts-accent-ink:{colors.AccentInk}"
            : null;
        _attributes = null;
    }
}
