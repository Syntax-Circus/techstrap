using TechStrap.Contracts.Branding;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>
/// The carbon tint code is a hard rule (BRAND.md section 12): the tints keep their meaning, carry non-colour cues, stay readable in
/// both themes, and are never reused for decoration.
/// </summary>
public sealed class TintStyleTests
{
    private const string LightScope = ":root,[data-bs-theme=light]";
    private const string DarkScope = "[data-bs-theme=dark]";
    private const double Aa = 4.5;

    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    // Styles that may paint with the carbon tints. PHASE-07 adds the reply composer, the avatar fill and the status-bar message
    // here when those components land; the list is deliberately short so a new use is a conscious decision.
    private static readonly string[] TintFiles = ["_tinted-entry.scss"];
    private static readonly string[] BrandMomentFiles = ["_brand-window.scss"];

    [Fact]
    public void Customer_is_white_with_a_grey_bar_public_is_canary_with_a_canary_bar()
    {
        var customer = Css.Declarations(".ts-entry--customer");
        var reply = Css.Declarations(".ts-entry--public");

        customer["background"].ShouldBe("var(--sheet)");
        customer["border-left"].ShouldBe("6px solid var(--ink-3)");
        reply["background"].ShouldBe("var(--canary)");
        reply["border-left"].ShouldBe("6px solid var(--canary-edge)");
    }

    [Fact]
    public void Internal_note_is_pink_dashed_notched_and_flat()
    {
        var note = Css.Declarations(".ts-entry--note");

        note["background"].ShouldBe("var(--pink)");
        note["border"].ShouldBe("2px dashed var(--pink-edge)");
        note["clip-path"].ShouldBe("polygon(0 0, calc(100% - 18px) 0, 100% 18px, 100% 100%, 0 100%)");
        note["box-shadow"].ShouldBe("none");
        Css.Declarations(".ts-entry--note .ts-entry-head")["color"].ShouldBe("var(--note-ink)");
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void Internal_note_text_is_readable_in_both_themes(string theme)
    {
        var tokens = Css.Declarations(theme == "light" ? LightScope : DarkScope);
        var background = Resolve(tokens, Css.Declarations(".ts-entry--note")["background"]);

        // Body text inherits ink from the entry; the head and label use note-ink.
        var body = Resolve(tokens, Css.Declarations(".ts-entry")["color"]);
        var head = Resolve(tokens, Css.Declarations(".ts-entry--note .ts-entry-head")["color"]);

        ProductAccent.ContrastRatio(body, background).ShouldBeGreaterThanOrEqualTo(Aa, $"note body in {theme}");
        ProductAccent.ContrastRatio(head, background).ShouldBeGreaterThanOrEqualTo(Aa, $"note head in {theme}");
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void Public_reply_text_is_readable_in_both_themes(string theme)
    {
        var tokens = Css.Declarations(theme == "light" ? LightScope : DarkScope);

        ProductAccent.ContrastRatio(
                Resolve(tokens, Css.Declarations(".ts-entry")["color"]),
                Resolve(tokens, Css.Declarations(".ts-entry--public")["background"]))
            .ShouldBeGreaterThanOrEqualTo(Aa);
    }

    [Fact]
    public void Keycaps_are_square_with_a_thicker_bottom_edge_and_sit_two_pixels_apart_in_a_chord()
    {
        var kbd = Css.Declarations(".ts-kbd");

        kbd["border-radius"].ShouldBe("0");
        kbd["border"].ShouldBe("1px solid var(--rule-strong)");
        kbd["border-bottom-width"].ShouldBe("2px");
        Css.Declarations(".ts-kbd+.ts-kbd")["margin-left"].ShouldBe("2px");
    }

    [Fact]
    public void Tint_tokens_are_used_only_by_the_files_allowed_to_paint_them()
    {
        foreach (var (file, text) in StyleSources())
        {
            var usesTint = new[] { "var(--canary", "var(--pink", "var(--note-ink" }.Any(text.Contains);

            (!usesTint || TintFiles.Contains(file)).ShouldBeTrue($"{file} paints with a carbon tint token; only {string.Join(", ", TintFiles)} may (BRAND.md: the tint code is never reused)");
        }
    }

    [Fact]
    public void Brand_moment_tokens_are_used_only_by_the_retro_window()
    {
        foreach (var (file, text) in StyleSources())
        {
            (!text.Contains("var(--bm-") || BrandMomentFiles.Contains(file)).ShouldBeTrue($"{file} uses a --bm-* token; only {string.Join(", ", BrandMomentFiles)} may");
        }
    }

    private static IEnumerable<(string File, string Text)> StyleSources() =>
        Directory.GetFiles(RepositoryRoot.Combine("src", "TechStrap.Admin", "Styles"), "*.scss")
            .Select(path => (Path.GetFileName(path), File.ReadAllText(path)));

    /// <summary>Turns <c>var(--ink)</c> into the theme's value for that token.</summary>
    private static string Resolve(IReadOnlyDictionary<string, string> tokens, string value) =>
        value.StartsWith("var(--", StringComparison.Ordinal) ? tokens[value[4..^1]] : value;
}
