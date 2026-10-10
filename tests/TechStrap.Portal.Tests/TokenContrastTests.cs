using TechStrap.Contracts.Branding;
using TechStrap.Tests.Shared;

namespace TechStrap.Portal.Tests;

/// <summary>
/// BRAND.md section 24: the Portal meets WCAG 2.2 AA. Every text and background pair of the Portal's own tokens (the neutral ones and the error, success and warning ones of 09d) is checked against the compiled CSS, so a
/// token edit that breaks a pair fails the build; the edge of a form control is held to the 3:1 of a non-text component (1.4.11); and the colors that carry a state sit on the grounds they are used on. (The product's
/// accent is checked per product, including hostile ones, in <c>ProductAccentContrastTests</c>.)
/// </summary>
public sealed class TokenContrastTests
{
    private const double Text = 4.5;
    private const double NonText = 3.0;

    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Portal");

    private static IReadOnlyDictionary<string, string> Tokens => Css.Declarations(":root,[data-bs-theme=light]");

    public static TheoryData<string, string> TextPairs =>
    [
        ("p-ink", "p-bg"), ("p-ink", "p-soft"), ("p-ink2", "p-bg"), ("p-ink2", "p-soft"),
        ("p-error", "p-bg"), ("p-error", "p-error-bg"), ("p-ink", "p-error-bg"),
        ("p-success", "p-bg"), ("p-success", "p-success-bg"), ("p-ink", "p-success-bg"),
        ("p-warn", "p-bg"), ("p-warn", "p-warn-bg"), ("p-ink", "p-warn-bg"),
    ];

    [Theory]
    [MemberData(nameof(TextPairs))]
    public void Text_pair_meets_AA(string foreground, string background)
    {
        ProductAccent.ContrastRatio(Tokens[$"--{foreground}"], Tokens[$"--{background}"]).ShouldBeGreaterThanOrEqualTo(Text, $"--{foreground} on --{background}");
    }

    [Fact]
    public void The_edge_of_a_form_control_is_the_secondary_ink_and_has_3_to_1_against_the_page()
    {
        Css.Declarations(".form-control,.form-select")["border-color"].ShouldBe("var(--p-ink2)");

        ProductAccent.ContrastRatio(Tokens["--p-ink2"], Tokens["--p-bg"]).ShouldBeGreaterThanOrEqualTo(NonText);
    }

    [Fact]
    public void The_decorative_line_colour_is_never_the_only_edge_of_a_control_it_is_too_faint_for_one()
    {
        // --p-line is for rules and card borders. This test records why a control does not use it, so nobody "tidies" the control border back to it.
        ProductAccent.ContrastRatio(Tokens["--p-line"], Tokens["--p-bg"]).ShouldBeLessThan(NonText);
    }

    [Fact]
    public void The_skip_link_and_the_focus_ring_use_the_ink_not_the_product_accent_so_a_hostile_accent_cannot_hide_them()
    {
        Css.Declarations(".ts-skip-link")["color"].ShouldBe("var(--p-ink)");
        Css.Declarations(".ts-skip-link")["background"].ShouldBe("var(--p-bg)");
        Css.OutsideMedia().Declarations(":focus-visible,.btn:focus-visible,.form-control:focus,.form-select:focus,.form-check-input:focus")["outline"].ShouldBe("3px solid var(--ts-focus, var(--p-ink)) !important", "the ring is the skin's focus token, which falls back to the ink");
        ProductAccent.ContrastRatio(Tokens["--p-ink"], Tokens["--p-bg"]).ShouldBeGreaterThanOrEqualTo(7.0, "the ring is ink on the page");
    }

    [Fact]
    public void The_semantic_tokens_are_the_brand_colours_the_admin_already_uses_for_the_same_meanings()
    {
        // BRAND.md: --p-error is --st-spam, --p-success is --st-open, --p-warn is --st-pending (light theme). One red, one green, one amber across both apps.
        var brand = BrandTokenTable.Read();
        string Light(string name) => CssColor.Normalize(brand.Single(t => t.Name == name).Light);

        Light("p-error").ShouldBe(Light("st-spam"));
        Light("p-success").ShouldBe(Light("st-open"));
        Light("p-warn").ShouldBe(Light("st-pending"));
    }
}
