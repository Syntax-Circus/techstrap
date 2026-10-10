namespace TechStrap.Contracts.Skins;

/// <summary>One skin problem: a stable code and the token or pair it names.</summary>
/// <param name="Code"><c>skin-invalid</c> or <c>skin-contrast-invalid</c>.</param>
/// <param name="Target">The camelCase field, or a pair such as <c>ink/background</c>.</param>
public sealed record SkinProblem(string Code, string Target);

/// <summary>The skin grammar: format only (keys, hex, enums, 1 to 4). Contrast is checked by <see cref="SkinResolver"/>.</summary>
public static class SkinRules
{
    /// <summary>The longest stored skin JSON, in characters.</summary>
    public const int MaxJsonLength = 2000;

    /// <summary>The problem code for a malformed or unknown value.</summary>
    public const string InvalidCode = "skin-invalid";

    /// <summary>The problem code for a color pair below its contrast minimum.</summary>
    public const string ContrastInvalidCode = "skin-contrast-invalid";

    private const int MinBorderWidth = 1;
    private const int MaxBorderWidth = 4;
    private const int HexLength = 7;

    /// <summary>Checks every non-null field of <paramref name="skin"/> against the grammar and returns one problem per bad field, in field order.</summary>
    public static IReadOnlyList<SkinProblem> Validate(ProductSkin skin)
    {
        ArgumentNullException.ThrowIfNull(skin);

        var problems = new List<SkinProblem>();
        Check(problems, "pack", skin.Pack, SkinPacks.IsKnown);
        Check(problems, "background", skin.Background, IsHex);
        Check(problems, "surface", skin.Surface, IsHex);
        Check(problems, "ink", skin.Ink, IsHex);
        Check(problems, "muted", skin.Muted, IsHex);
        Check(problems, "border", skin.Border, IsHex);
        Check(problems, "brand", skin.Brand, IsHex);
        Check(problems, "chrome", skin.Chrome, IsHex);
        Check(problems, "focus", skin.Focus, IsHex);
        Check(problems, "headingFont", skin.HeadingFont, SkinFonts.IsKnown);
        Check(problems, "bodyFont", skin.BodyFont, SkinFonts.IsKnown);
        Check(problems, "radius", skin.Radius, SkinValues.IsRadius);
        if (skin.BorderWidth is { } width && (width < MinBorderWidth || width > MaxBorderWidth))
        {
            problems.Add(new SkinProblem(InvalidCode, "borderWidth"));
        }

        Check(problems, "shadow", skin.Shadow, SkinValues.IsShadow);
        Check(problems, "button", skin.Button, SkinValues.IsButton);
        Check(problems, "header", skin.Header, SkinValues.IsHeader);
        return problems;
    }

    /// <summary>True for exactly <c>#RRGGBB</c> with hex digits in either case.</summary>
    internal static bool IsHex(string? value)
    {
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

        return true;
    }

    private static void Check(List<SkinProblem> problems, string field, string? value, Func<string?, bool> isValid)
    {
        if (value is not null && !isValid(value))
        {
            problems.Add(new SkinProblem(InvalidCode, field));
        }
    }
}
