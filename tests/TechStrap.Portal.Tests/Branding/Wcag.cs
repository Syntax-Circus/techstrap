namespace TechStrap.Portal.Tests.Branding;

/// <summary>
/// An independent WCAG 2.x implementation for tests, deliberately not shared with the production
/// code so the accent rule is checked against a second implementation of the formula.
/// </summary>
internal static class Wcag
{
    public static double Ratio(string foregroundHex, string backgroundHex)
    {
        var a = Luminance(foregroundHex);
        var b = Luminance(backgroundHex);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(string hex)
    {
        var r = Channel(Convert.ToInt32(hex.Substring(1, 2), 16));
        var g = Channel(Convert.ToInt32(hex.Substring(3, 2), 16));
        var b = Channel(Convert.ToInt32(hex.Substring(5, 2), 16));
        return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
    }

    private static double Channel(int value)
    {
        var s = value / 255.0;
        return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}
