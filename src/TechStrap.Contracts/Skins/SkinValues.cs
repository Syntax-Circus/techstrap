namespace TechStrap.Contracts.Skins;

/// <summary>The fixed vocabulary of the preset skin fields, as string constants (Contracts carries no enums).</summary>
public static class SkinValues
{
    /// <summary>A pack whose page is light.</summary>
    public const string Light = "light";

    /// <summary>A pack whose page is dark.</summary>
    public const string Dark = "dark";

    /// <summary>Square corners.</summary>
    public const string RadiusSquare = "square";

    /// <summary>Slightly rounded corners.</summary>
    public const string RadiusSoft = "soft";

    /// <summary>Strongly rounded corners.</summary>
    public const string RadiusRound = "round";

    /// <summary>No shadows.</summary>
    public const string ShadowNone = "none";

    /// <summary>Soft shadows.</summary>
    public const string ShadowSoft = "soft";

    /// <summary>Hard offset shadows.</summary>
    public const string ShadowHard = "hard";

    /// <summary>Flat buttons.</summary>
    public const string ButtonFlat = "flat";

    /// <summary>Bevelled buttons.</summary>
    public const string ButtonBevel = "bevel";

    /// <summary>Outlined buttons.</summary>
    public const string ButtonOutline = "outline";

    /// <summary>Header on the page background.</summary>
    public const string HeaderPlain = "plain";

    /// <summary>Header filled with the chrome colour.</summary>
    public const string HeaderSolid = "solid";

    /// <summary>Header with a chrome band.</summary>
    public const string HeaderBand = "band";

    /// <summary>True for a known radius preset (case-sensitive).</summary>
    public static bool IsRadius(string? v) => v is RadiusSquare or RadiusSoft or RadiusRound;

    /// <summary>True for a known shadow preset (case-sensitive).</summary>
    public static bool IsShadow(string? v) => v is ShadowNone or ShadowSoft or ShadowHard;

    /// <summary>True for a known button preset (case-sensitive).</summary>
    public static bool IsButton(string? v) => v is ButtonFlat or ButtonBevel or ButtonOutline;

    /// <summary>True for a known header preset (case-sensitive).</summary>
    public static bool IsHeader(string? v) => v is HeaderPlain or HeaderSolid or HeaderBand;

    /// <summary>The CSS length for a radius preset: <c>0</c>, <c>.25rem</c> or <c>.75rem</c>. Throws <see cref="ArgumentOutOfRangeException"/> for an unknown preset.</summary>
    public static string RadiusRem(string radius) => radius switch
    {
        RadiusSquare => "0",
        RadiusSoft => ".25rem",
        RadiusRound => ".75rem",
        _ => throw new ArgumentOutOfRangeException(nameof(radius), radius, "Unknown radius preset."),
    };
}
