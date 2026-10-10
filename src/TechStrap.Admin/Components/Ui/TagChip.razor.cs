using Microsoft.AspNetCore.Components;
using TechStrap.Contracts.Branding;

namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// A tag as a text chip. Tag colors are arbitrary admin input, so the foreground is always derived from the background through the one
/// shared rule (<see cref="ProductAccent"/>, BRAND.md section 22), and a malformed color renders the plain chip. The word is the meaning; the color only decorates.
/// </summary>
public partial class TagChip
{
    [Parameter, EditorRequired]
    public string Name { get; set; } = string.Empty;

    /// <summary>A <c>#RRGGBB</c> color, as stored on the tag.</summary>
    [Parameter]
    public string? Colour { get; set; }

    private string? Style => ProductAccent.TryDerive(Colour, out var colours)
        ? $"background-color:{colours.Accent};border-color:{colours.Accent};color:{colours.OnAccent}"
        : null;
}
