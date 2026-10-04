using System.Globalization;

namespace TechStrap.Admin.Features.Settings;

/// <summary>Makes a value that came from outside (a payload, an email kind) safe to print: control and format characters (such as a right-to-left override) become spaces, and the text is cut to 60 characters.</summary>
internal static class SafeText
{
    public const int MaxLength = 60;

    public static string Clip(string? value)
    {
        var clean = new string([.. (value ?? string.Empty).Select(c => char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format ? ' ' : c)]).Trim();
        return clean.Length <= MaxLength ? clean : string.Concat(clean.AsSpan(0, MaxLength - 1), "\u2026");
    }
}
