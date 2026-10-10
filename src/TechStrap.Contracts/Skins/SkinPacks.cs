namespace TechStrap.Contracts.Skins;

/// <summary>The five theme packs. Pure data; every pack passes the skin contrast rules (a test proves it).</summary>
public static class SkinPacks
{
    /// <summary>The key of the pack used when nothing else is chosen.</summary>
    public const string DefaultKey = "classic";

    private static readonly SkinPack[] Packs =
    [
        new("classic", "Classic", SkinValues.Light, new SkinTokens(
            "#FFFFFF", "#FFFFFF", "#1B1B22", "#4A4A57", "#D4D4DC", "#1D4FA8", "#1B1B22", "#1B1B22",
            SkinFonts.PlexSans, SkinFonts.PlexSans, SkinValues.RadiusSoft, 1, SkinValues.ShadowNone, SkinValues.ButtonFlat, SkinValues.HeaderPlain)),
        new("slate", "Slate", SkinValues.Light, new SkinTokens(
            "#F8FAFC", "#FFFFFF", "#0F172A", "#475569", "#CBD5E1", "#334155", "#0F172A", "#0F172A",
            SkinFonts.PlexSans, SkinFonts.PlexSans, SkinValues.RadiusSoft, 1, SkinValues.ShadowSoft, SkinValues.ButtonFlat, SkinValues.HeaderSolid)),
        new("paper", "Paper", SkinValues.Light, new SkinTokens(
            "#FBF7EF", "#FFFDF8", "#2B2118", "#5C4F42", "#DDD0BC", "#8A3B12", "#3B2A1E", "#2B2118",
            SkinFonts.SourceSerif, SkinFonts.Nunito, SkinValues.RadiusSoft, 1, SkinValues.ShadowNone, SkinValues.ButtonFlat, SkinValues.HeaderBand)),
        new("contrast", "Contrast", SkinValues.Light, new SkinTokens(
            "#FFFFFF", "#FFFFFF", "#000000", "#333333", "#000000", "#0033A0", "#000000", "#000000",
            SkinFonts.Atkinson, SkinFonts.Atkinson, SkinValues.RadiusSquare, 3, SkinValues.ShadowNone, SkinValues.ButtonOutline, SkinValues.HeaderSolid)),
        new("midnight", "Midnight", SkinValues.Dark, new SkinTokens(
            "#0F1420", "#181F2E", "#F1F5F9", "#A9B4C6", "#2B364A", "#6EA8FF", "#0A0E17", "#FFD166",
            SkinFonts.PlexSans, SkinFonts.PlexSans, SkinValues.RadiusSoft, 1, SkinValues.ShadowSoft, SkinValues.ButtonFlat, SkinValues.HeaderSolid)),
    ];

    /// <summary>Every pack: classic, slate, paper, contrast, midnight.</summary>
    public static IReadOnlyList<SkinPack> All => Packs;

    /// <summary>The Classic pack: today's look, the baseline every other pack is diffed against.</summary>
    public static SkinPack Classic => Packs[0];

    /// <summary>True when <paramref name="key"/> names a pack (case-sensitive).</summary>
    public static bool IsKnown(string? key) => Find(key) is not null;

    /// <summary>The pack for a key, or null for an unknown or null key.</summary>
    public static SkinPack? Find(string? key)
    {
        if (key is null)
        {
            return null;
        }

        foreach (var pack in Packs)
        {
            if (string.Equals(pack.Key, key, StringComparison.Ordinal))
            {
                return pack;
            }
        }

        return null;
    }
}
