using TechStrap.Contracts.Products;
using TechStrap.Portal.Products;

namespace TechStrap.Portal.Tests.Products;

/// <summary>
/// Review Focus 5 (untrusted branding): a stored product is data an agent typed, and a logo stored before the Admin validated it was never checked. The view model keeps only what is safe to
/// render: an accent that the one derivation rule accepts (the three colours are derived from it by <c>AccentScope</c>, never taken from the DTO's own strings), and a logo address that is https,
/// or http to loopback in Development only (the same rule as the Content-Security-Policy's <c>img-src</c>).
/// </summary>
public sealed class ProductThemeViewModelTests
{
    private static PublicProductDto Dto(string accent = "#F59E0B", string? logo = "https://cdn.example.com/paperplane.png", string name = "Paperplane", string onAccent = "#000000", string ink = "#9D6507") =>
        new("paperplane", name, logo, accent, onAccent, ink);

    [Fact]
    public void A_product_becomes_its_key_name_normalised_accent_and_logo()
    {
        var theme = ProductThemeViewModel.From(Dto(accent: "#f59e0b"), allowLoopbackImages: false);

        theme.Key.ShouldBe("paperplane");
        theme.DisplayName.ShouldBe("Paperplane");
        theme.Accent.ShouldBe("#F59E0B", "the single derivation rule's own spelling");
        theme.LogoUrl.ShouldBe("https://cdn.example.com/paperplane.png");
    }

    [Theory]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#123456;background:url(//evil.example/x)")]
    [InlineData("#123456\"><script>")]
    [InlineData("expression(alert(1))")]
    public void An_accent_the_rule_rejects_is_not_carried_so_the_stylesheet_fallbacks_apply(string accent)
    {
        ProductThemeViewModel.From(Dto(accent: accent), allowLoopbackImages: false).Accent.ShouldBeNull();
    }

    [Fact]
    public void The_dtos_own_derived_colours_are_never_carried()
    {
        var theme = ProductThemeViewModel.From(Dto(onAccent: "red;x:y", ink: "url(//evil.example)"), allowLoopbackImages: false);

        theme.ToString().ShouldNotContain("evil");
        theme.ToString().ShouldNotContain("red;x:y");
        typeof(ProductThemeViewModel).GetProperties().Select(p => p.Name).ShouldBe(["Key", "DisplayName", "Accent", "LogoUrl"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("https://cdn.example.com/logo.png")]
    [InlineData("https://CDN.EXAMPLE.COM/a/b.svg?v=2")]
    [InlineData("https://localhost/logo.png")]
    [InlineData("https://127.0.0.1:8443/logo.png")]
    public void An_https_logo_is_kept_in_every_environment(string logo)
    {
        ProductThemeViewModel.From(Dto(logo: logo), allowLoopbackImages: false).LogoUrl.ShouldNotBeNull();
        ProductThemeViewModel.From(Dto(logo: logo), allowLoopbackImages: true).LogoUrl.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("http://localhost:5000/logo.png")]
    [InlineData("http://127.0.0.1/logo.png")]
    public void A_loopback_http_logo_is_kept_in_Development_only(string logo)
    {
        ProductThemeViewModel.From(Dto(logo: logo), allowLoopbackImages: true).LogoUrl.ShouldBe(logo);
        ProductThemeViewModel.From(Dto(logo: logo), allowLoopbackImages: false).LogoUrl.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://cdn.example.com/logo.png")]
    [InlineData("http://localhost.evil.example/logo.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("//cdn.example.com/logo.png")]
    [InlineData("/logo.png")]
    [InlineData("logo.png")]
    [InlineData("file:///c:/logo.png")]
    [InlineData("ftp://cdn.example.com/logo.png")]
    [InlineData("https://user:secret@cdn.example.com/logo.png")]
    [InlineData("https:///logo.png")]
    [InlineData("https://cdn.example.com/a b.png")]
    [InlineData("https://cdn.example.com/a\nb.png")]
    public void Any_other_logo_is_omitted_in_every_environment(string? logo)
    {
        ProductThemeViewModel.From(Dto(logo: logo), allowLoopbackImages: false).LogoUrl.ShouldBeNull(logo);
        ProductThemeViewModel.From(Dto(logo: logo), allowLoopbackImages: true).LogoUrl.ShouldBeNull(logo);
    }

    [Fact]
    public void Characters_that_could_break_out_of_an_attribute_are_percent_encoded_in_a_kept_logo()
    {
        var logo = ProductThemeViewModel.From(Dto(logo: "https://cdn.example.com/a\"onerror=\"alert(1)<b>.png"), allowLoopbackImages: false).LogoUrl;

        logo.ShouldNotBeNull();
        logo.ShouldNotContain("\"");
        logo.ShouldNotContain("<");
        logo.ShouldNotContain(">");
    }

    [Fact]
    public void A_logo_over_500_characters_is_omitted()
    {
        var logo = "https://cdn.example.com/" + new string('a', 480) + ".png";

        logo.Length.ShouldBeGreaterThan(500);
        ProductThemeViewModel.From(Dto(logo: logo), allowLoopbackImages: false).LogoUrl.ShouldBeNull();
    }

    [Theory]
    [InlineData("Paperplane", "Paperplane")]
    [InlineData("  Paperplane  ", "Paperplane")]
    [InlineData("", "paperplane")]
    [InlineData("   ", "paperplane")]
    public void A_blank_name_falls_back_to_the_key_and_text_is_otherwise_kept_as_is(string name, string expected)
    {
        ProductThemeViewModel.From(Dto(name: name), allowLoopbackImages: false).DisplayName.ShouldBe(expected);
    }

    [Fact]
    public void Markup_in_the_name_stays_text_because_every_renderer_encodes_it()
    {
        ProductThemeViewModel.From(Dto(name: "<b>Paper</b> & \"plane\""), allowLoopbackImages: false).DisplayName.ShouldBe("<b>Paper</b> & \"plane\"");
    }
}
