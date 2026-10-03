using System.Globalization;

namespace TechStrap.Contracts.Branding;

/// <summary>The three values derived from one product accent colour (docs/BRAND.md section 22), all as uppercase <c>#RRGGBB</c>.</summary>
/// <param name="Accent">Fills and borders: the product's colour, as entered (normalised to uppercase).</param>
/// <param name="OnAccent">Text on an accent fill: white or black, whichever contrasts more.</param>
/// <param name="AccentInk">The accent as text or an outline on white: the accent itself when it already reaches 4.5:1, otherwise darkened until it does.</param>
public readonly record struct ProductAccentColors(string Accent, string OnAccent, string AccentInk);

/// <summary>
/// The single implementation of the product-accent rule. The portal, the product save validation (PHASE-04) and
/// customer email rendering (PHASE-05) all call this; nothing else may recompute these values.
/// Pure and dependency-free, so it lives in the Contracts leaf that every host already references.
/// </summary>
public static class ProductAccent
{
    /// <summary>WCAG 2.x AA contrast for normal text.</summary>
    public const double MinimumTextContrast = 4.5;

    private const string White = "#FFFFFF";
    private const string Black = "#000000";
    private const int HexLength = 7;
    private const int DarkenDenominator = 25;
    private const int DarkenRoundingOffset = 12;

    /// <summary>
    /// Derives the three accent properties from a <c>#RRGGBB</c> value. Only a malformed value is rejected;
    /// no colour is rejected for low contrast because white-or-black on-accent always reaches at least 4.58:1.
    /// </summary>
    public static bool TryDerive(string? value, out ProductAccentColors colors)
    {
        colors = default;
        if (!TryParse(value, out var rgb))
        {
            return false;
        }

        var onAccent = Contrast(rgb, WhiteRgb) >= Contrast(rgb, BlackRgb) ? White : Black;
        colors = new ProductAccentColors(Format(rgb), onAccent, Format(DarkenUntilReadableOnWhite(rgb)));
        return true;
    }

    /// <summary>WCAG contrast ratio (1 to 21) between two <c>#RRGGBB</c> colours. Throws <see cref="ArgumentException"/> for a malformed colour.</summary>
    public static double ContrastRatio(string foregroundHex, string backgroundHex)
    {
        if (!TryParse(foregroundHex, out var foreground))
        {
            throw new ArgumentException("Not a #RRGGBB colour.", nameof(foregroundHex));
        }

        if (!TryParse(backgroundHex, out var background))
        {
            throw new ArgumentException("Not a #RRGGBB colour.", nameof(backgroundHex));
        }

        return Contrast(foreground, background);
    }

    private static readonly (int R, int G, int B) WhiteRgb = (255, 255, 255);
    private static readonly (int R, int G, int B) BlackRgb = (0, 0, 0);

    // Scale each channel of the ORIGINAL colour by (1 - 0.04 k), k = 1, 2, 3 ..., until the result reaches 4.5:1 on white.
    // Integer arithmetic: round(c * (25 - k) / 25) never lands on a .5 tie, so (c * (25 - k) + 12) / 25 is exact.
    private static (int R, int G, int B) DarkenUntilReadableOnWhite((int R, int G, int B) accent)
    {
        if (Contrast(accent, WhiteRgb) >= MinimumTextContrast)
        {
            return accent;
        }

        for (var k = 1; k < DarkenDenominator; k++)
        {
            var darkened = (Scale(accent.R, k), Scale(accent.G, k), Scale(accent.B, k));
            if (Contrast(darkened, WhiteRgb) >= MinimumTextContrast)
            {
                return darkened;
            }
        }

        return BlackRgb;
    }

    private static int Scale(int channel, int k) => ((channel * (DarkenDenominator - k)) + DarkenRoundingOffset) / DarkenDenominator;

    private static bool TryParse(string? value, out (int R, int G, int B) rgb)
    {
        rgb = default;
        if (value is not { Length: HexLength } || value[0] != '#')
        {
            return false;
        }

        for (var i = 1; i < HexLength; i++)
        {
            if (!char.IsAsciiHexDigit(value[i]))
            {
                return false;
            }
        }

        rgb = (
            int.Parse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        return true;
    }

    private static string Format((int R, int G, int B) rgb) => string.Create(CultureInfo.InvariantCulture, $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}");

    private static double Contrast((int R, int G, int B) a, (int R, int G, int B) b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance((int R, int G, int B) rgb) =>
        (0.2126 * Linear(rgb.R)) + (0.7152 * Linear(rgb.G)) + (0.0722 * Linear(rgb.B));

    private static double Linear(int channel)
    {
        var s = channel / 255.0;
        return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}
