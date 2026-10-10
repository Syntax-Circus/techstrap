using TechStrap.Contracts.Branding;

namespace TechStrap.Contracts.Skins;

/// <summary>A skin after resolution: the pack, its scheme, the complete tokens and the derived colours.</summary>
/// <param name="Pack">The pack key that supplied the base tokens.</param>
/// <param name="Scheme"><see cref="SkinValues.Light"/> or <see cref="SkinValues.Dark"/>.</param>
/// <param name="Tokens">The complete, validated tokens.</param>
/// <param name="BrandIsExplicit">True when the brand came from the product skin or the product accent rather than the pack. A pack brand that differs from Classic's is emitted too (<see cref="SkinCss.Properties"/>).</param>
/// <param name="OnBrand">Text on a brand fill: white or black.</param>
/// <param name="BrandInk">The brand as text or an outline on the background.</param>
/// <param name="OnChrome">Text on the chrome fill: white or black.</param>
public sealed record ResolvedSkin(string Pack, string Scheme, SkinTokens Tokens, bool BrandIsExplicit, string OnBrand, string BrandInk, string OnChrome);

/// <summary>The outcome of resolving: a skin that is always usable, plus what was dropped and why.</summary>
/// <param name="Skin">The resolved skin.</param>
/// <param name="Problems">Every override that was ignored.</param>
public sealed record SkinResolution(ResolvedSkin Skin, IReadOnlyList<SkinProblem> Problems);

/// <summary>
/// The single resolution of Classic, the deployment default pack, a product pack and field overrides into complete tokens.
/// Invalid or low-contrast overrides fall back to the pack value and are reported, never thrown.
/// </summary>
public static class SkinResolver
{
    private const double InkContrast = 4.5;
    private const double FocusContrast = 3.0;

    /// <summary>Resolves a skin. Never throws for bad input.</summary>
    /// <param name="deploymentDefaultPack">The site's default pack key; unknown or null means Classic.</param>
    /// <param name="productSkin">The product's skin, or null.</param>
    /// <param name="productAccent">The product's accent colour, or null.</param>
    public static SkinResolution Resolve(string? deploymentDefaultPack, ProductSkin? productSkin, string? productAccent)
    {
        var problems = new List<SkinProblem>();
        var defaultPack = SkinPacks.Find(deploymentDefaultPack) ?? SkinPacks.Classic;
        var skin = productSkin ?? new ProductSkin();

        var invalid = new HashSet<string>(StringComparer.Ordinal);
        foreach (var problem in SkinRules.Validate(skin))
        {
            invalid.Add(problem.Target);
            problems.Add(problem);
        }

        var pack = (skin.Pack is not null && !invalid.Contains("pack") ? SkinPacks.Find(skin.Pack) : null) ?? defaultPack;
        var baseTokens = pack.Tokens;

        string? Colour(string field, string? value) => value is not null && !invalid.Contains(field) ? value.ToUpperInvariant() : null;
        string? Pick(string field, string? value) => value is not null && !invalid.Contains(field) ? value : null;

        var overrides = new Overrides
        {
            Background = Colour("background", skin.Background),
            Surface = Colour("surface", skin.Surface),
            Ink = Colour("ink", skin.Ink),
            Muted = Colour("muted", skin.Muted),
            Border = Colour("border", skin.Border),
            Chrome = Colour("chrome", skin.Chrome),
            Focus = Colour("focus", skin.Focus),
        };

        // Candidate tokens, then each contrast pair in turn; a failing pair reverts its overridden members to the pack value.
        var tokens = baseTokens with
        {
            Background = overrides.Background ?? baseTokens.Background,
            Surface = overrides.Surface ?? baseTokens.Surface,
            Ink = overrides.Ink ?? baseTokens.Ink,
            Muted = overrides.Muted ?? baseTokens.Muted,
            Border = overrides.Border ?? baseTokens.Border,
            Chrome = overrides.Chrome ?? baseTokens.Chrome,
            Focus = overrides.Focus ?? baseTokens.Focus,
            HeadingFont = Pick("headingFont", skin.HeadingFont) ?? baseTokens.HeadingFont,
            BodyFont = Pick("bodyFont", skin.BodyFont) ?? baseTokens.BodyFont,
            Radius = Pick("radius", skin.Radius) ?? baseTokens.Radius,
            BorderWidth = skin.BorderWidth is { } width && !invalid.Contains("borderWidth") ? width : baseTokens.BorderWidth,
            Shadow = Pick("shadow", skin.Shadow) ?? baseTokens.Shadow,
            Button = Pick("button", skin.Button) ?? baseTokens.Button,
            Header = Pick("header", skin.Header) ?? baseTokens.Header,
        };

        tokens = EnforceContrast(tokens, baseTokens, overrides, problems);

        var brandOverride = Colour("brand", skin.Brand);
        var accent = productAccent is not null && SkinRules.IsHex(productAccent) ? productAccent.ToUpperInvariant() : null;
        var brand = brandOverride ?? accent;
        tokens = tokens with { Brand = brand ?? baseTokens.Brand };

        var brandColours = Derive(tokens.Brand, baseTokens.Brand);
        var chromeColours = Derive(tokens.Chrome, baseTokens.Chrome);
        var resolved = new ResolvedSkin(
            pack.Key,
            pack.Scheme,
            tokens,
            brand is not null,
            brandColours.OnAccent,
            ProductAccent.ReadableOn(tokens.Brand, tokens.Background),
            chromeColours.OnAccent);
        return new SkinResolution(resolved, problems);
    }

    private static readonly (string A, string B, double Minimum)[] Pairs =
    [
        ("ink", "background", InkContrast),
        ("ink", "surface", InkContrast),
        ("muted", "background", InkContrast),
        ("focus", "background", FocusContrast),
    ];

    private static readonly string[] ColourGroup = ["background", "surface", "ink", "muted", "focus"];

    // Every pair is evaluated on the candidate in one pass; every overridden member of every failing pair reverts together,
    // then all pairs are re-checked. A pair still failing can only involve pack values (a test proves packs pass), so the
    // whole colour group goes back to the pack as a last resort. Each failing pair is reported once.
    private static SkinTokens EnforceContrast(SkinTokens tokens, SkinTokens pack, Overrides overrides, List<SkinProblem> problems)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var failing = FailingPairs(tokens);
        if (failing.Count == 0)
        {
            return tokens;
        }

        foreach (var (a, b, _) in failing)
        {
            Report(a, b);
            foreach (var member in new[] { a, b })
            {
                if (overrides.Has(member))
                {
                    tokens = Set(tokens, member, Get(pack, member));
                }
            }
        }

        failing = FailingPairs(tokens);
        if (failing.Count == 0)
        {
            return tokens;
        }

        foreach (var (a, b, _) in failing)
        {
            Report(a, b);
        }

        foreach (var member in ColourGroup)
        {
            tokens = Set(tokens, member, Get(pack, member));
        }

        return tokens;

        void Report(string a, string b)
        {
            if (reported.Add(a + "/" + b))
            {
                problems.Add(new SkinProblem(SkinRules.ContrastInvalidCode, a + "/" + b));
            }
        }
    }

    private static List<(string A, string B, double Minimum)> FailingPairs(SkinTokens tokens) =>
        Pairs.Where(p => ProductAccent.ContrastRatio(Get(tokens, p.A), Get(tokens, p.B)) < p.Minimum).ToList();

    private static ProductAccentColors Derive(string value, string fallback) =>
        ProductAccent.TryDerive(value, out var colours) ? colours
        : ProductAccent.TryDerive(fallback, out var fallbackColours) ? fallbackColours
        : new ProductAccentColors(fallback, "#000000", "#000000");

    private static string Get(SkinTokens t, string name) => name switch
    {
        "background" => t.Background,
        "surface" => t.Surface,
        "ink" => t.Ink,
        "muted" => t.Muted,
        "focus" => t.Focus,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Not a contrast member."),
    };

    private static SkinTokens Set(SkinTokens t, string name, string value) => name switch
    {
        "background" => t with { Background = value },
        "surface" => t with { Surface = value },
        "ink" => t with { Ink = value },
        "muted" => t with { Muted = value },
        "focus" => t with { Focus = value },
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Not a contrast member."),
    };

    private sealed class Overrides
    {
        public string? Background { get; init; }

        public string? Surface { get; init; }

        public string? Ink { get; init; }

        public string? Muted { get; init; }

        public string? Border { get; init; }

        public string? Chrome { get; init; }

        public string? Focus { get; init; }

        public bool Has(string name) => name switch
        {
            "background" => Background is not null,
            "surface" => Surface is not null,
            "ink" => Ink is not null,
            "muted" => Muted is not null,
            "focus" => Focus is not null,
            _ => false,
        };
    }
}
