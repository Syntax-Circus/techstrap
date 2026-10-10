using TechStrap.Contracts.Branding;

namespace TechStrap.Portal.Tests.Branding;

/// <summary>
/// The product-accent rule of docs/BRAND.md section 22: on-accent is white or black (whichever contrasts
/// more), accent-ink is the accent darkened until it reaches 4.5:1 on white, and only a malformed value is rejected.
/// </summary>
public sealed class ProductAccentContrastTests
{
    private const string White = "#FFFFFF";
    private const double Aa = 4.5;
    private const int SweepStep = 5;

    // The vectors of BRAND.md section 22, plus the edge cases: pure white, pure black, the mid-gray that just
    // misses AA on white (#777777) and the gray that just meets it (#767676).
    public static TheoryData<string, string, string> Vectors() => new()
    {
        { "#7C3AED", "#FFFFFF", "#7C3AED" },
        { "#F59E0B", "#000000", "#9D6507" },
        { "#0F3D2E", "#FFFFFF", "#0F3D2E" },
        { "#2E9AFF", "#000000", "#2375C2" },
        { "#4B7D87", "#FFFFFF", "#4B7D87" },
        { "#FFFFFF", "#000000", "#707070" },
        { "#000000", "#FFFFFF", "#000000" },
        { "#777777", "#000000", "#727272" },
        { "#767676", "#000000", "#767676" },
    };

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Derives_the_documented_on_accent_and_ink(string accent, string onAccent, string ink)
    {
        ProductAccent.TryDerive(accent, out var colors).ShouldBeTrue();

        colors.Accent.ShouldBe(accent);
        colors.OnAccent.ShouldBe(onAccent);
        colors.AccentInk.ShouldBe(ink);
    }

    [Fact]
    public void The_worst_case_accent_still_reaches_4_58_with_white()
    {
        ProductAccent.TryDerive("#4B7D87", out var colors).ShouldBeTrue();

        Wcag.Ratio(colors.OnAccent, colors.Accent).ShouldBeGreaterThanOrEqualTo(4.58);
    }

    [Fact]
    public void Every_sampled_colour_keeps_on_accent_and_ink_at_or_above_AA()
    {
        var checkedColours = 0;
        for (var r = 0; r <= 255; r += SweepStep)
        {
            for (var g = 0; g <= 255; g += SweepStep)
            {
                for (var b = 0; b <= 255; b += SweepStep)
                {
                    var accent = $"#{r:X2}{g:X2}{b:X2}";

                    ProductAccent.TryDerive(accent, out var colors).ShouldBeTrue(accent);
                    Wcag.Ratio(colors.OnAccent, accent).ShouldBeGreaterThanOrEqualTo(Aa, $"on-accent for {accent}");
                    Wcag.Ratio(colors.AccentInk, White).ShouldBeGreaterThanOrEqualTo(Aa, $"ink for {accent}");
                    checkedColours++;
                }
            }
        }

        checkedColours.ShouldBe(52 * 52 * 52);
    }

    [Fact]
    public void Ink_is_unchanged_when_the_accent_already_meets_AA_on_white()
    {
        ProductAccent.TryDerive("#1D4FA8", out var colors).ShouldBeTrue();

        colors.AccentInk.ShouldBe("#1D4FA8");
    }

    [Fact]
    public void Lowercase_input_is_accepted_and_normalised_to_uppercase()
    {
        ProductAccent.TryDerive("#7c3aed", out var colors).ShouldBeTrue();

        colors.Accent.ShouldBe("#7C3AED");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#12345678")]
    [InlineData("#FFF")]
    [InlineData("123456")]
    [InlineData("red")]
    [InlineData("#GGGGGG")]
    [InlineData(" #123456")]
    [InlineData("#123456 ")]
    [InlineData("#123456;background:url(x)")]
    public void Rejects_anything_that_is_not_a_six_digit_hex_colour(string? value)
    {
        ProductAccent.TryDerive(value, out var colors).ShouldBeFalse();

        colors.ShouldBe(default);
    }

    [Fact]
    public void ContrastRatio_matches_the_independent_implementation()
    {
        ProductAccent.ContrastRatio("#FFFFFF", "#000000").ShouldBe(21.0, 0.001);
        ProductAccent.ContrastRatio("#777777", "#FFFFFF").ShouldBe(Wcag.Ratio("#777777", "#FFFFFF"), 0.0001);
    }

    [Fact]
    public void ContrastRatio_rejects_malformed_colours()
    {
        Should.Throw<ArgumentException>(() => ProductAccent.ContrastRatio("red", "#FFFFFF"));
    }
}
