using Bunit;
using TechStrap.Admin.Features.Appearance;
using TechStrap.Contracts.Skins;

namespace TechStrap.Admin.Tests.Components;

public sealed class PortalAppearancePreviewTests : BunitContext
{
    [Theory]
    [InlineData("classic")]
    [InlineData("slate")]
    [InlineData("paper")]
    [InlineData("contrast")]
    [InlineData("midnight")]
    public void Pack_preview_uses_the_shared_safe_carrier(string pack)
    {
        var cut = Render<PortalAppearancePreview>(p => p.Add(c => c.DefaultPack, pack));
        var resolution = SkinResolver.Resolve(pack, null, null);
        var scope = cut.Find(".ts-appearance-preview");
        foreach (var property in SkinCss.Properties(resolution.Skin))
        {
            scope.GetAttribute("style")!.ShouldContain($"{property.Key}:{property.Value}");
        }
        foreach (var attribute in SkinCss.Attributes(resolution.Skin))
        {
            scope.GetAttribute(attribute.Key).ShouldBe(attribute.Value);
        }
        cut.FindAll("style, iframe, form, a[href]").ShouldBeEmpty();
        cut.FindAll("button").ShouldNotBeEmpty();
        cut.FindAll("button").ShouldAllBe(b => b.GetAttribute("type") == "button");
        cut.Find("input").HasAttribute("readonly").ShouldBeTrue();
        cut.Find("input[disabled]").ShouldNotBeNull();
        cut.Markup.ShouldContain("Example portal");
    }

    [Fact]
    public void Parameter_changes_replace_previous_properties_and_presets()
    {
        var cut = Render<PortalAppearancePreview>(p => p.Add(c => c.Skin, new ProductSkin(Pack: "midnight")));
        cut.Find(".ts-appearance-preview").GetAttribute("data-ts-header").ShouldBe("solid");
        cut.Render(p => p.Add(c => c.Skin, null));
        cut.Find(".ts-appearance-preview").GetAttribute("style").ShouldBeNullOrEmpty();
        cut.Find(".ts-appearance-preview").HasAttribute("data-ts-header").ShouldBeFalse();
        cut.Find(".ts-appearance-preview").HasAttribute("data-ts-scheme").ShouldBeFalse();
    }

    [Fact]
    public void Invalid_values_fall_back_safely_and_report_problems_as_text()
    {
        var cut = Render<PortalAppearancePreview>(p => p
            .Add(c => c.Skin, new ProductSkin(Brand: "red;display:none", HeadingFont: "url(evil)", Ink: "#FFFFFF"))
            .Add(c => c.DisplayName, "<img src=x onerror=alert(1)>"));
        cut.FindAll("img, script").ShouldBeEmpty();
        cut.Find(".ts-appearance-preview").GetAttribute("style").ShouldBeNullOrEmpty();
        cut.Find("[role=status]").TextContent.ShouldContain("brand");
        cut.Find("[role=status]").TextContent.ShouldContain("ink/background");
        cut.Find(".ts-appearance-preview__name").TextContent.ShouldBe("<img src=x onerror=alert(1)>");
    }

    [Fact]
    public void Accent_is_resolved_and_product_brand_takes_precedence()
    {
        var cut = Render<PortalAppearancePreview>(p => p.Add(c => c.AccentColour, "#334455"));
        cut.Find(".ts-appearance-preview").GetAttribute("style")!.ShouldContain("--ts-accent:#334455");
        cut.Render(p => p.Add(c => c.Skin, new ProductSkin(Brand: "#63371F")));
        cut.Find(".ts-appearance-preview").GetAttribute("style")!.ShouldContain("--ts-accent:#63371F");
    }

    [Fact]
    public void DragonPoop_example_keeps_fonts_and_all_presets()
    {
        var skin = new ProductSkin(Background: "#F6E8C2", Surface: "#FFF8E4", Ink: "#1D120B", Muted: "#4A4640",
            Border: "#1D120B", Brand: "#63371F", Chrome: "#26140C", Focus: "#26140C", HeadingFont: SkinFonts.Pixelify,
            BodyFont: SkinFonts.Nunito, Radius: "square", BorderWidth: 4, Shadow: "hard", Button: "bevel", Header: "solid");
        var cut = Render<PortalAppearancePreview>(p => p.Add(c => c.Skin, skin));
        var scope = cut.Find(".ts-appearance-preview");
        scope.GetAttribute("style")!.ShouldContain("--ts-font-heading:'Pixelify Sans'");
        scope.GetAttribute("style")!.ShouldContain("--ts-font-body:'Nunito'");
        scope.GetAttribute("style")!.ShouldContain("--ts-border-w:4px");
        scope.GetAttribute("data-ts-shadow").ShouldBe("hard");
        scope.GetAttribute("data-ts-button").ShouldBe("bevel");
        cut.FindAll("[role=status]").ShouldBeEmpty();
    }
}
