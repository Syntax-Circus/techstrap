using System.Globalization;
using Microsoft.AspNetCore.Components;
using TechStrap.Contracts.Branding;

namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// A small preview of a product's branding: its name and logo on a bar, a button in the accent and a link in the readable accent ink. It sets the same three custom properties as the portal's
/// accent scope (<c>--ts-accent</c>, <c>--ts-on-accent</c>, <c>--ts-accent-ink</c>), derived with the one shared rule (<see cref="ProductAccent"/>). A malformed or blank color sets nothing and the stylesheet's
/// fallbacks apply. A low-contrast color only produces an information note: TechStrap darkens it where it is used as text, so it is never refused. The caller passes only a logo address that
/// passed the logo rule; this component renders whatever it is given.
/// </summary>
public partial class AccentPreview
{
    private string? _style;
    private string? _note;

    /// <summary>A <c>#RRGGBB</c> value as typed. Blank or malformed shows the default look.</summary>
    [Parameter]
    public string? Accent { get; set; }

    [Parameter]
    public string? DisplayName { get; set; }

    /// <summary>An already-validated image address, or null for no logo.</summary>
    [Parameter]
    public string? LogoUrl { get; set; }

    protected override void OnParametersSet()
    {
        if (!ProductAccent.TryDerive(Accent?.Trim(), out var colors))
        {
            _style = null;
            _note = null;
            return;
        }

        _style = $"--ts-accent:{colors.Accent};--ts-on-accent:{colors.OnAccent};--ts-accent-ink:{colors.AccentInk}";
        var ratio = ProductAccent.ContrastRatio(colors.Accent, "#FFFFFF");
        _note = ratio < ProductAccent.MinimumTextContrast ? AccentPreviewCopy.LowContrast(Floor(ratio)) : null;
    }

    // Rounded down, so "4.5:1" is never shown for a color that is below 4.5.
    private static string Floor(double ratio) => (Math.Floor(ratio * 10) / 10).ToString("0.0", CultureInfo.InvariantCulture);
}

public static class AccentPreviewCopy
{
    public const string SampleButton = "Button";
    public const string SampleLink = "Link";

    public static string LowContrast(string ratio) =>
        $"This color has low contrast on white ({ratio}:1; 4.5:1 is the aim for text). TechStrap darkens it wherever it is used for text, so it stays readable. You can keep it.";
}
