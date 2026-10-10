namespace TechStrap.Contracts.Skins;

/// <summary>The font keys a skin may name, and the CSS stack each maps to. A skin never carries a raw font-family.</summary>
public static class SkinFonts
{
    /// <summary>IBM Plex Sans, the default.</summary>
    public const string PlexSans = "plex-sans";

    /// <summary>Nunito, rounded and friendly.</summary>
    public const string Nunito = "nunito";

    /// <summary>Atkinson Hyperlegible, for maximum legibility.</summary>
    public const string Atkinson = "atkinson";

    /// <summary>Source Serif 4.</summary>
    public const string SourceSerif = "source-serif";

    /// <summary>Pixelify Sans, a pixel face.</summary>
    public const string Pixelify = "pixelify";

    private const string SansFallback = "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif";

    private static readonly string[] Keys = [PlexSans, Nunito, Atkinson, SourceSerif, Pixelify];

    /// <summary>Every known font key.</summary>
    public static IReadOnlyList<string> All => Keys;

    /// <summary>True when <paramref name="key"/> is a known font key (case-sensitive).</summary>
    public static bool IsKnown(string? key) => key is PlexSans or Nunito or Atkinson or SourceSerif or Pixelify;

    /// <summary>The CSS <c>font-family</c> value for a key: single-quoted family names and a generic fallback. Throws <see cref="ArgumentOutOfRangeException"/> for an unknown key.</summary>
    public static string Stack(string key) => key switch
    {
        PlexSans => "'IBM Plex Sans', " + SansFallback,
        Nunito => "'Nunito', " + SansFallback,
        Atkinson => "'Atkinson Hyperlegible', " + SansFallback,
        SourceSerif => "'Source Serif 4', Georgia, 'Times New Roman', serif",
        Pixelify => "'Pixelify Sans', " + SansFallback,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown font key."),
    };
}
