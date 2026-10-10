using TechStrap.Contracts.Skins;

namespace TechStrap.Application.Tests.Skins;

public sealed class SkinCssTests
{
    [Fact]
    public void Classic_with_an_accent_emits_exactly_the_three_accent_properties()
    {
        var skin = SkinResolver.Resolve("classic", null, "#F59E0B").Skin;

        SkinCss.Properties(skin).ShouldBe(
        [
            new KeyValuePair<string, string>("--ts-accent", "#F59E0B"),
            new KeyValuePair<string, string>("--ts-on-accent", "#000000"),
            new KeyValuePair<string, string>("--ts-accent-ink", "#9D6507"),
        ]);
        SkinCss.Attributes(skin).ShouldBeEmpty();
    }

    [Fact]
    public void Classic_with_nothing_emits_nothing()
    {
        var skin = SkinResolver.Resolve("classic", null, null).Skin;

        SkinCss.Properties(skin).ShouldBeEmpty();
        SkinCss.Attributes(skin).ShouldBeEmpty();
    }

    [Fact]
    public void Midnight_emits_its_variables_and_presets()
    {
        var skin = SkinResolver.Resolve("midnight", null, null).Skin;

        var properties = SkinCss.Properties(skin).ToDictionary(p => p.Key, p => p.Value);
        properties["--p-bg"].ShouldBe("#0F1420");
        properties["--ts-chrome"].ShouldBe("#0A0E17");
        properties["--ts-focus"].ShouldBe("#FFD166");
        properties["--ts-accent"].ShouldBe("#6EA8FF");
        properties["--ts-on-accent"].ShouldBe("#000000");
        properties["--ts-accent-ink"].ShouldBe("#6EA8FF");

        var attributes = SkinCss.Attributes(skin).ToDictionary(p => p.Key, p => p.Value);
        attributes["data-ts-shadow"].ShouldBe("soft");
        attributes["data-ts-header"].ShouldBe("solid");
    }

    [Fact]
    public void Slate_emits_the_trio_built_from_its_own_brand_first_and_in_order()
    {
        var skin = SkinResolver.Resolve("slate", null, null).Skin;

        var properties = SkinCss.Properties(skin);
        properties.Take(3).Select(p => p.Key).ShouldBe(["--ts-accent", "--ts-on-accent", "--ts-accent-ink"]);
        properties[0].Value.ShouldBe("#334155");
        properties[2].Value.ShouldBe(skin.BrandInk);
    }

    [Fact]
    public void A_product_accent_on_midnight_emits_its_trio_with_ink_readable_on_the_dark_page()
    {
        var skin = SkinResolver.Resolve("midnight", null, "#F59E0B").Skin;

        var properties = SkinCss.Properties(skin).ToDictionary(p => p.Key, p => p.Value);
        properties["--ts-accent"].ShouldBe("#F59E0B");
        TechStrap.Contracts.Branding.ProductAccent.ContrastRatio(properties["--ts-accent-ink"], "#0F1420").ShouldBeGreaterThanOrEqualTo(4.5);
    }

    [Fact]
    public void A_dark_pack_emits_its_scheme_and_a_light_pack_does_not()
    {
        SkinCss.Attributes(SkinResolver.Resolve("midnight", null, null).Skin).ShouldContain(new KeyValuePair<string, string>("data-ts-scheme", "dark"));
        foreach (var pack in SkinPacks.All.Where(p => p.Scheme == SkinValues.Light))
        {
            SkinCss.Attributes(SkinResolver.Resolve(pack.Key, null, null).Skin).ShouldNotContain(a => a.Key == "data-ts-scheme", pack.Key);
        }
    }

    [Fact]
    public void Text_on_the_chrome_fill_is_at_least_3_to_1_in_every_pack_so_the_header_focus_ring_can_use_it()
    {
        foreach (var pack in SkinPacks.All)
        {
            var skin = SkinResolver.Resolve(pack.Key, null, null).Skin;
            TechStrap.Contracts.Branding.ProductAccent.ContrastRatio(skin.OnChrome, skin.Tokens.Chrome).ShouldBeGreaterThanOrEqualTo(3.0, pack.Key);
        }
    }

    [Fact]
    public void A_font_and_radius_override_emit_their_variables()
    {
        var skin = SkinResolver.Resolve("classic", new ProductSkin(Radius: "square", HeadingFont: "source-serif"), null).Skin;

        var properties = SkinCss.Properties(skin).ToDictionary(p => p.Key, p => p.Value);
        properties["--ts-radius"].ShouldBe("0");
        properties["--ts-font-heading"].ShouldBe(SkinFonts.Stack("source-serif"));
    }

    [Fact]
    public void A_hand_built_skin_with_a_raw_preset_emits_no_attribute()
    {
        var tokens = SkinPacks.Classic.Tokens with { Shadow = "0 0 9px red", Button = "javascript:x", Header = "none" };
        var skin = new ResolvedSkin("classic", SkinValues.Light, tokens, false, "#FFFFFF", "#1D4FA8", "#FFFFFF");

        SkinCss.Attributes(skin).ShouldBeEmpty();
    }
}
