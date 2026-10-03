using System.Text.RegularExpressions;
using TechStrap.Tests.Shared;

namespace TechStrap.Portal.Tests;

/// <summary>The build compiles Styles/app.scss (Bootstrap via libman) to wwwroot/css/app.css; it is never committed.</summary>
public sealed class StyleBuildTests
{
    private const string LightScope = ":root,[data-bs-theme=light]";

    private static readonly string[] AdminOnlyTokens =
        ["--paper", "--sheet", "--rail", "--head", "--rule", "--margin", "--canary", "--pink", "--note-ink", "--st-new", "--st-open", "--st-spam", "--bm-plate", "--bm-edge", "--bm-crt", "--bm-led"];

    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Portal");

    [Fact]
    public void Compiled_css_exists_and_contains_bootstrap()
    {
        Css.Text.ShouldContain("--bs-primary");
    }

    [Fact]
    public void Every_portal_token_of_BRAND_md_is_a_custom_property_on_root()
    {
        var portalTokens = BrandTokenTable.Read().Where(t => t.Name.StartsWith("p-", StringComparison.Ordinal)).ToList();
        portalTokens.Count.ShouldBe(5);
        var root = Css.Declarations(LightScope);

        foreach (var token in portalTokens)
        {
            root[$"--{token.Name}"].ShouldBe(CssColor.Normalize(token.Light), token.Name);
        }
    }

    [Fact]
    public void The_product_accent_properties_have_documented_fallbacks()
    {
        var root = Css.Declarations(LightScope);

        root["--ts-accent"].ShouldBe("#1D4FA8");
        root["--ts-on-accent"].ShouldBe("#FFFFFF");
        root["--ts-accent-ink"].ShouldBe("#1D4FA8");
    }

    [Fact]
    public void Bootstrap_variables_follow_the_portal_mapping()
    {
        var root = Css.Declarations(LightScope);

        root["--bs-body-bg"].ShouldBe("#FFFFFF");
        root["--bs-body-color"].ShouldBe("#1B1B22");
        root["--bs-secondary-color"].ShouldBe("#4A4A57");
        root["--bs-border-color"].ShouldBe("#D4D4DC");
        root["--bs-light"].ShouldBe("#F5F5F7");
    }

    [Fact]
    public void Primary_controls_take_the_runtime_product_accent()
    {
        Regex.IsMatch(Css.Text, @"--bs-btn-bg:\s*var\(--ts-accent\)").ShouldBeTrue();
        Regex.IsMatch(Css.Text, @"--bs-btn-color:\s*var\(--ts-on-accent\)").ShouldBeTrue();
        Regex.IsMatch(Css.Text, @"a\{color:\s*var\(--ts-accent-ink\)").ShouldBeTrue();
    }

    [Fact]
    public void The_portal_is_light_only_and_carries_no_admin_tokens()
    {
        Css.Declarations("[data-bs-theme=dark]").ShouldBeEmpty();
        Css.Text.ShouldNotContain("prefers-color-scheme");
        Css.Text.ShouldNotContain("color-scheme:dark");
        foreach (var token in AdminOnlyTokens)
        {
            Css.Text.ShouldNotContain(token + ":", customMessage: token);
        }
    }
}
