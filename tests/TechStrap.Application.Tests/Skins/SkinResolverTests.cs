using TechStrap.Contracts.Branding;
using TechStrap.Contracts.Skins;

namespace TechStrap.Application.Tests.Skins;

public sealed class SkinResolverTests
{
    [Fact]
    public void Precedence_is_classic_then_default_pack_then_product_pack_then_overrides()
    {
        var none = SkinResolver.Resolve(null, null, null).Skin;
        none.Pack.ShouldBe("classic");
        none.Tokens.Background.ShouldBe("#FFFFFF");

        var defaultSlate = SkinResolver.Resolve("slate", null, null).Skin;
        defaultSlate.Tokens.Background.ShouldBe("#F8FAFC");

        var productPaper = SkinResolver.Resolve("slate", new ProductSkin(Pack: "paper"), null).Skin;
        productPaper.Pack.ShouldBe("paper");
        productPaper.Tokens.Background.ShouldBe("#FBF7EF");

        var overridden = SkinResolver.Resolve("slate", new ProductSkin(Pack: "paper", Background: "#fff8e4"), null);
        overridden.Skin.Tokens.Background.ShouldBe("#FFF8E4");
        overridden.Skin.Tokens.Ink.ShouldBe("#2B2118");
        overridden.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void A_product_accent_wins_over_the_pack_brand_and_a_brand_override_wins_over_the_accent()
    {
        SkinResolver.Resolve("slate", null, "#F59E0B").Skin.Tokens.Brand.ShouldBe("#F59E0B");
        SkinResolver.Resolve("slate", new ProductSkin(Brand: "#112233"), "#F59E0B").Skin.Tokens.Brand.ShouldBe("#112233");
        SkinResolver.Resolve("slate", null, null).Skin.BrandIsExplicit.ShouldBeFalse();
        SkinResolver.Resolve("slate", null, "#F59E0B").Skin.BrandIsExplicit.ShouldBeTrue();
    }

    [Fact]
    public void Every_pack_passes_its_own_contrast_rules()
    {
        foreach (var pack in SkinPacks.All)
        {
            var t = pack.Tokens;
            ProductAccent.ContrastRatio(t.Ink, t.Background).ShouldBeGreaterThanOrEqualTo(4.5, pack.Key);
            ProductAccent.ContrastRatio(t.Ink, t.Surface).ShouldBeGreaterThanOrEqualTo(4.5, pack.Key);
            ProductAccent.ContrastRatio(t.Muted, t.Background).ShouldBeGreaterThanOrEqualTo(4.5, pack.Key);
            ProductAccent.ContrastRatio(t.Focus, t.Background).ShouldBeGreaterThanOrEqualTo(3.0, pack.Key);
            SkinResolver.Resolve(pack.Key, null, null).Problems.ShouldBeEmpty(pack.Key);
        }
    }

    [Fact]
    public void A_low_contrast_override_is_reported_and_dropped_to_the_pack_value()
    {
        var result = SkinResolver.Resolve("classic", new ProductSkin(Ink: "#EEEEEE"), null);

        result.Problems.ShouldContain(new SkinProblem("skin-contrast-invalid", "ink/background"));
        result.Skin.Tokens.Ink.ShouldBe("#1B1B22");
    }

    [Fact]
    public void A_hostile_skin_resolves_to_pack_values_and_reports_problems()
    {
        var hostile = new ProductSkin(
            Pack: "<script>", Background: "red;} body{display:none", Ink: "#GGGGGG", HeadingFont: "Comic Sans'; x", Radius: "50%",
            BorderWidth: 99, Shadow: "0 0 9px red", Button: "javascript:alert(1)", Header: "none");

        var result = SkinResolver.Resolve("classic", hostile, null);

        result.Skin.Tokens.ShouldBe(SkinPacks.Classic.Tokens);
        result.Problems.Select(p => p.Code).Distinct().ShouldBe(["skin-invalid"]);
        result.Problems.Count.ShouldBe(9);
    }

    [Fact]
    public void The_derived_colours_follow_the_background_and_the_brand()
    {
        var midnight = SkinResolver.Resolve("midnight", null, null).Skin;
        midnight.OnBrand.ShouldBe("#000000");
        ProductAccent.ContrastRatio(midnight.BrandInk, midnight.Tokens.Background).ShouldBeGreaterThanOrEqualTo(4.5);

        var classic = SkinResolver.Resolve("classic", null, "#F59E0B").Skin;
        ProductAccent.TryDerive("#F59E0B", out var accent).ShouldBeTrue();
        classic.OnBrand.ShouldBe(accent.OnAccent);
        classic.BrandInk.ShouldBe(accent.AccentInk);
    }
}
