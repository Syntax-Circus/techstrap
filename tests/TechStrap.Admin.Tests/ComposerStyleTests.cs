using TechStrap.Contracts.Branding;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>The composer wears the same tint code as the timeline: canary with a solid edge for a public reply, pink with a dashed edge and a notched corner for a note.</summary>
public sealed class ComposerStyleTests
{
    private const string LightScope = ":root,[data-bs-theme=light]";
    private const string DarkScope = "[data-bs-theme=dark]";
    private const double Aa = 4.5;

    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    [Fact]
    public void A_public_reply_is_canary_with_a_solid_edge()
    {
        var reply = Css.Declarations(".ts-composer--public");

        reply["background"].ShouldBe("var(--canary)");
        reply["border-color"].ShouldBe("var(--canary-edge)");
    }

    [Fact]
    public void An_internal_note_is_pink_dashed_notched_and_flat_like_its_timeline_entry()
    {
        var note = Css.Declarations(".ts-composer--note");

        note["background"].ShouldBe("var(--pink)");
        note["border"].ShouldBe("2px dashed var(--pink-edge)");
        note["clip-path"].ShouldBe("polygon(0 0, calc(100% - 18px) 0, 100% 18px, 100% 100%, 0 100%)");
        note["box-shadow"].ShouldBe("none");
        Css.Declarations(".ts-composer-warning--internal")["color"].ShouldBe("var(--note-ink)");
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void Composer_text_and_the_internal_warning_are_readable_in_both_themes(string theme)
    {
        var tokens = Css.Declarations(theme == "light" ? LightScope : DarkScope);
        var ink = Resolve(tokens, Css.Declarations(".ts-composer")["color"]);

        ProductAccent.ContrastRatio(ink, Resolve(tokens, Css.Declarations(".ts-composer--public")["background"])).ShouldBeGreaterThanOrEqualTo(Aa);
        ProductAccent.ContrastRatio(ink, Resolve(tokens, Css.Declarations(".ts-composer--note")["background"])).ShouldBeGreaterThanOrEqualTo(Aa);
        ProductAccent.ContrastRatio(
                Resolve(tokens, Css.Declarations(".ts-composer-warning--internal")["color"]),
                Resolve(tokens, Css.Declarations(".ts-composer--note")["background"]))
            .ShouldBeGreaterThanOrEqualTo(Aa);
    }

    private static string Resolve(IReadOnlyDictionary<string, string> tokens, string value) =>
        value.StartsWith("var(--", StringComparison.Ordinal) ? tokens[value[4..^1]] : value;
}
