using System.Text.RegularExpressions;
using TechStrap.Tests.Shared;

namespace TechStrap.Portal.Tests.Skins;

/// <summary>The presets of D-053 are attribute selectors in the compiled stylesheet, every value falls back to what it was before skins, and the Portal stays light-only and url()-free outside its font faces.</summary>
public sealed class PresetStyleTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Portal");

    [Theory]
    [InlineData("[data-ts-button=bevel]")]
    [InlineData("[data-ts-button=outline]")]
    [InlineData("[data-ts-header=solid]")]
    [InlineData("[data-ts-header=band]")]
    [InlineData("[data-ts-shadow=soft]")]
    [InlineData("[data-ts-shadow=hard]")]
    public void Every_preset_has_a_rule(string selector)
    {
        Css.Text.ShouldContain(selector);
    }

    [Fact]
    public void Focus_and_fonts_fall_back_to_what_they_were()
    {
        Regex.IsMatch(Css.Text, @"var\(--ts-focus,\s*var\(--p-ink\)\)").ShouldBeTrue("the focus ring falls back to the ink");
        Css.Text.ShouldContain("var(--ts-font-body");
        Css.Text.ShouldContain("var(--ts-font-heading");
    }

    [Fact]
    public void The_portal_stays_light_only_and_adds_no_url_outside_fonts_and_data_images()
    {
        Css.Text.ShouldNotContain("prefers-color-scheme");
        Css.Declarations("[data-bs-theme=dark]").ShouldBeEmpty();
        var targets = Regex.Matches(Css.Text, @"url\(\s*([""']?)(.*?)\1\s*\)", RegexOptions.IgnoreCase).Select(m => m.Groups[2].Value).ToList();
        targets.ShouldAllBe(t => t.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) || t.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase));
    }
}
