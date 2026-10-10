using System.Text.RegularExpressions;
using TechStrap.Contracts.Branding;
using TechStrap.Contracts.Skins;
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

    [Theory]
    [InlineData("solid")]
    [InlineData("band")]
    public void The_header_presets_are_full_bleed_through_a_border_image_outset_with_no_markup_change(string preset)
    {
        var rule = Regex.Match(Css.Text, @"\[data-ts-header=" + preset + @"\]\s+\.ts-product-header\s*\{(?<body>[^}]*)\}").Groups["body"].Value;

        rule.ShouldContain("border-image:");
        rule.ShouldContain("0 100vmax 0 100vmax");
    }

    [Fact]
    public void The_portal_stays_light_only_and_adds_no_url_outside_fonts_and_data_images()
    {
        Css.Text.ShouldNotContain("prefers-color-scheme");
        Css.Declarations("[data-bs-theme=dark]").ShouldBeEmpty();
        var targets = Regex.Matches(Css.Text, @"url\(\s*([""']?)(.*?)\1\s*\)", RegexOptions.IgnoreCase).Select(m => m.Groups[2].Value).ToList();
        targets.ShouldAllBe(t => t.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) || t.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase));
    }

    private static string Body(string selectorPattern)
    {
        var match = Regex.Match(Css.Text, selectorPattern + @"\s*\{(?<body>[^{}]*)\}");
        match.Success.ShouldBeTrue(selectorPattern);
        return match.Groups["body"].Value;
    }

    private static string Declared(string body, string property)
    {
        var match = Regex.Match(body, @"(?:^|;)\s*" + Regex.Escape(property) + @":\s*(?<value>[^;]+)");
        match.Success.ShouldBeTrue(property + " in " + body);
        return match.Groups["value"].Value.Trim();
    }

    private const string SolidHeader = @"\.ts-accent-scope\[data-ts-header=solid\] \.ts-product-header";

    [Fact]
    public void Inside_a_solid_header_the_focus_ring_and_halo_are_the_on_chrome_colour()
    {
        var body = Body(SolidHeader);

        Declared(body, "--ts-focus").ShouldContain("var(--ts-on-chrome");
        Declared(body, "--ts-accent").ShouldContain("var(--ts-on-chrome");
    }

    [Fact]
    public void The_classic_chrome_fallbacks_equal_the_classic_pack_chrome_and_its_derived_on_colour()
    {
        var body = Body(SolidHeader);
        var chrome = Regex.Match(Declared(body, "background"), @"var\(--ts-chrome,\s*(?<c>#[0-9a-fA-F]{3,6})\)").Groups["c"].Value;
        var onChrome = Regex.Match(Declared(body, "color"), @"var\(--ts-on-chrome,\s*(?<c>#[0-9a-fA-F]{3,6})\)").Groups["c"].Value;

        static string Full(string c) => c.Length == 4 ? "#" + string.Concat(c.Skip(1).Select(x => new string(x, 2))) : c;
        Full(chrome).ToUpperInvariant().ShouldBe(SkinPacks.Classic.Tokens.Chrome);
        Full(onChrome).ToUpperInvariant().ShouldBe(SkinResolver.Resolve("classic", null, null).Skin.OnChrome);
    }

    [Fact]
    public void Forced_colours_drop_the_header_border_image_and_print_drops_the_fills()
    {
        var forced = Css.Text.IndexOf("@media(forced-colors: active)", StringComparison.Ordinal);
        forced.ShouldBeGreaterThanOrEqualTo(0);
        Regex.IsMatch(Css.Text[forced..], @"\.ts-accent-scope\[data-ts-header\] \.ts-product-header\s*\{[^}]*border-image:\s*none")
            .ShouldBeTrue("the header border image must not paint in forced colors");

        var print = Regex.Match(Css.Text, @"@media print\s*\{\s*\.ts-accent-scope\[data-ts-header\] \.ts-product-header\s*\{(?<body>[^}]*)\}");
        print.Success.ShouldBeTrue();
        print.Groups["body"].Value.ShouldContain("border-image:none");
        print.Groups["body"].Value.ShouldContain("background:none");
    }

    [Fact]
    public void The_dark_scheme_block_keeps_every_text_and_ground_pair_at_4_5_to_1()
    {
        var body = Body(@"\.ts-accent-scope\[data-ts-scheme=dark\]");
        string V(string name) => Declared(body, name);

        foreach (var pack in SkinPacks.All.Where(p => p.Scheme == SkinValues.Dark))
        {
            var t = pack.Tokens;
            (string Text, string Ground, string Label)[] pairs =
            [
                (t.Ink, V("--p-soft"), "ink on soft"),
                (t.Muted, V("--p-soft"), "muted on soft"),
                (V("--p-error"), t.Background, "error on page"),
                (V("--p-error"), V("--p-error-bg"), "error on error ground"),
                (t.Ink, V("--p-error-bg"), "ink on error ground"),
                (V("--p-warn"), t.Background, "warning on page"),
                (V("--p-warn"), V("--p-warn-bg"), "warning on warning ground"),
                (t.Ink, V("--p-warn-bg"), "ink on warning ground"),
                (V("--p-success"), V("--p-success-bg"), "success on success ground"),
                (t.Ink, V("--p-success-bg"), "ink on success ground"),
            ];
            foreach (var (text, ground, label) in pairs)
            {
                ProductAccent.ContrastRatio(text, ground).ShouldBeGreaterThanOrEqualTo(4.5, $"{pack.Key}: {label}");
            }
        }
    }

    [Fact]
    public void The_dark_select_arrow_is_drawn_only_on_a_single_line_select()
    {
        Css.Text.ShouldContain(".form-select:not([multiple]):not([size]){--bs-form-select-bg-img: none");
    }

    [Fact]
    public void A_landing_scope_paints_nothing_behind_its_rounded_card_and_the_file_button_hover_keeps_bootstraps_fill()
    {
        Regex.IsMatch(Css.Text, @"\.ts-landing\s*>\s*\.ts-accent-scope\s*\{background-color:\s*(transparent|rgba\(0,\s*0,\s*0,\s*0\))\}").ShouldBeTrue();
        Css.Text.ShouldContain("var(--ts-surface, var(--bs-secondary-bg))");
    }
}
