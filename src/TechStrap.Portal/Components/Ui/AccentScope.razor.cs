using Microsoft.AspNetCore.Components;
using TechStrap.Contracts.Branding;

namespace TechStrap.Portal.Components.Ui;

/// <summary>
/// Applies a product accent to everything inside it, at runtime, as the three custom properties of docs/BRAND.md section 22.
/// The values come only from <see cref="ProductAccent.TryDerive"/>, which accepts nothing but #RRGGBB, so no other text can reach the
/// style attribute. A missing or malformed accent sets nothing and the stylesheet fallbacks apply. PHASE-09 reuses this per product.
/// </summary>
public partial class AccentScope
{
    private string? _style;

    [Parameter]
    public string? Accent { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected override void OnParametersSet()
    {
        _style = ProductAccent.TryDerive(Accent, out var colors)
            ? $"--ts-accent:{colors.Accent};--ts-on-accent:{colors.OnAccent};--ts-accent-ink:{colors.AccentInk}"
            : null;
    }
}
