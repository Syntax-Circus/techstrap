using System.Text.RegularExpressions;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>The build compiles Styles/app.scss (Bootstrap via libman) to wwwroot/css/app.css; it is never committed.</summary>
public sealed class StyleBuildTests
{
    private const string LightScope = ":root,[data-bs-theme=light]";
    private const string DarkScope = "[data-bs-theme=dark]";
    private const string AutoDarkScope = ":root:not([data-bs-theme=light],[data-bs-theme=dark])";

    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    [Fact]
    public void Compiled_css_exists_and_contains_bootstrap()
    {
        Css.Text.ShouldContain("--bs-primary");
    }

    [Fact]
    public void The_token_table_of_BRAND_md_was_read()
    {
        // 18 surface + 5 tint + 6 status + 10 brand-moment tokens have a dark value; the 5 portal tokens do not.
        var tokens = BrandTokenTable.Read();

        tokens.Count(t => t.Dark is not null).ShouldBe(39);
        tokens.Count(t => t.Dark is null).ShouldBe(5);
    }

    [Fact]
    public void Every_brand_token_is_a_custom_property_on_root_with_its_light_value()
    {
        var root = Css.Declarations(LightScope);

        foreach (var token in BrandTokenTable.Read())
        {
            root.ShouldContainKey($"--{token.Name}", token.Name);
            root[$"--{token.Name}"].ShouldBe(CssColor.Normalize(token.Light), token.Name);
        }
    }

    [Fact]
    public void Every_dark_brand_token_is_set_under_data_bs_theme_dark()
    {
        var dark = Css.Declarations(DarkScope);

        foreach (var token in BrandTokenTable.Read().Where(t => t.Dark is not null))
        {
            dark.ShouldContainKey($"--{token.Name}", token.Name);
            dark[$"--{token.Name}"].ShouldBe(CssColor.Normalize(token.Dark!), token.Name);
        }
    }

    [Fact]
    public void Auto_theme_follows_prefers_color_scheme_unless_a_theme_is_chosen()
    {
        Regex.IsMatch(Css.Text, @"@media\s*\(prefers-color-scheme:\s*dark\)\{" + Regex.Escape(AutoDarkScope) + @"\{").ShouldBeTrue();
        var auto = Css.Declarations(AutoDarkScope);

        foreach (var token in BrandTokenTable.Read().Where(t => t.Dark is not null))
        {
            auto[$"--{token.Name}"].ShouldBe(CssColor.Normalize(token.Dark!), token.Name);
        }

        auto["--bs-body-bg"].ShouldBe("#0B1E40");
    }

    [Fact]
    public void Bootstrap_variables_follow_the_brand_mapping()
    {
        var light = Css.Declarations(LightScope);

        light["--bs-primary"].ShouldBe("#1D4FA8");
        light["--bs-body-bg"].ShouldBe("#F7F5EE");
        light["--bs-body-color"].ShouldBe("#14213D");
        light["--bs-border-color"].ShouldBe("#C5D0E6");
        light["--bs-success"].ShouldBe("#14702F");
        light["--bs-info"].ShouldBe("#1D5FB8");
        light["--bs-warning"].ShouldBe("#8A5300");
        light["--bs-danger"].ShouldBe("#B3141C");
        light["--bs-secondary"].ShouldBe("#5B6475");
        Css.Declarations(DarkScope)["--bs-body-bg"].ShouldBe("#0B1E40");
        Css.Declarations(DarkScope)["--bs-body-color"].ShouldBe("#E8EFFF");
    }

    [Fact]
    public void The_portal_accent_properties_have_fallbacks_for_the_style_guide()
    {
        var root = Css.Declarations(LightScope);

        root["--ts-accent"].ShouldBe("#1D4FA8");
        root["--ts-on-accent"].ShouldBe("#FFFFFF");
        root["--ts-accent-ink"].ShouldBe("#1D4FA8");
    }

    [Fact]
    public void Corners_are_square_and_shadows_are_hard_offsets()
    {
        var root = Css.Declarations(LightScope);

        root["--bs-border-radius"].ShouldBe("0");
        root["--bs-box-shadow"].ShouldBe("3px 3px 0 var(--shadow)");
        root["--bs-box-shadow-sm"].ShouldBe("2px 2px 0 var(--shadow)");
    }
}
