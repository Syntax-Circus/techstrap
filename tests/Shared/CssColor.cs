using System.Text.RegularExpressions;

namespace TechStrap.Tests.Shared;

/// <summary>Compares CSS colors semantically: Sass compressed output rewrites <c>#FFFFFF</c> as <c>#fff</c> and <c>0.14</c> as <c>.14</c>.</summary>
internal static partial class CssColor
{
    [GeneratedRegex(@"^#[0-9A-Fa-f]{3}([0-9A-Fa-f]{3})?$")]
    private static partial Regex HexPattern();

    public static bool IsColour(string value) => HexPattern().IsMatch(value) || value.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string value)
    {
        value = value.Trim();
        if (value.StartsWith('#'))
        {
            var hex = value[1..].ToUpperInvariant();
            return hex.Length == 3 ? "#" + string.Concat(hex.Select(c => new string(c, 2))) : "#" + hex;
        }

        return value.ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("(0.", "(.", StringComparison.Ordinal).Replace(",0.", ",.", StringComparison.Ordinal);
    }
}
