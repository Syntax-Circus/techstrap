using Microsoft.AspNetCore.Components;

namespace TechStrap.Admin.Components.Showcase;

/// <summary>Swatches for one token group. Each swatch paints the live custom property, so it follows the active theme.</summary>
public partial class PaletteSwatches
{
    private static readonly IReadOnlyDictionary<PaletteGroup, string[]> TokensByGroup = new Dictionary<PaletteGroup, string[]>
    {
        [PaletteGroup.Surface] = ["paper", "sheet", "rail", "head", "ink", "ink-2", "ink-3", "rule", "rule-strong", "margin", "hover", "sel", "overlay", "shadow", "scrim", "accent", "on-accent", "focus"],
        [PaletteGroup.Tints] = ["canary", "canary-edge", "pink", "pink-edge", "note-ink"],
        [PaletteGroup.Status] = ["st-new", "st-open", "st-pending", "st-solved", "st-closed", "st-spam"],
        [PaletteGroup.BrandMoment] = ["bm-plate", "bm-edge", "bm-bar", "bm-on-bar", "bm-text", "bm-text2", "bm-crt", "bm-on-crt", "bm-led", "bm-shadow"],
        [PaletteGroup.Portal] = ["p-bg", "p-soft", "p-ink", "p-ink2", "p-line"],
    };

    [Parameter, EditorRequired]
    public PaletteGroup Group { get; set; }

    private string[] Tokens => TokensByGroup[Group];
}
