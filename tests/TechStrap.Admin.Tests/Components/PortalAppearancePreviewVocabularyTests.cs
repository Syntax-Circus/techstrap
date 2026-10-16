using Bunit;
using TechStrap.Admin.Features.Appearance;
using TechStrap.Contracts.Skins;

namespace TechStrap.Admin.Tests.Components;

public sealed class PortalAppearancePreviewVocabularyTests : BunitContext
{
    [Theory]
    [InlineData(SkinFonts.PlexSans)]
    [InlineData(SkinFonts.Nunito)]
    [InlineData(SkinFonts.Atkinson)]
    [InlineData(SkinFonts.SourceSerif)]
    [InlineData(SkinFonts.Pixelify)]
    public void All_allowed_fonts_flow_through_the_shared_carrier(string font)
    {
        var cut = Render<PortalAppearancePreview>(p => p.Add(c => c.Skin, new ProductSkin(HeadingFont: font, BodyFont: font)));
        var style = cut.Find(".ts-appearance-preview").GetAttribute("style");
        if (font == SkinFonts.PlexSans)
        {
            style.ShouldBeNullOrEmpty();
        }
        else
        {
            style!.ShouldContain("--ts-font-heading:" + SkinFonts.Stack(font));
            style!.ShouldContain("--ts-font-body:" + SkinFonts.Stack(font));
        }
    }

    [Theory]
    [InlineData("none", "flat", "plain", "soft")]
    [InlineData("soft", "outline", "band", "round")]
    [InlineData("hard", "bevel", "solid", "square")]
    public void All_allowed_presets_flow_through_the_shared_carrier(string shadow, string button, string header, string radius)
    {
        var skin = new ProductSkin(Shadow: shadow, Button: button, Header: header, Radius: radius);
        var cut = Render<PortalAppearancePreview>(p => p.Add(c => c.Skin, skin));
        var scope = cut.Find(".ts-appearance-preview");
        foreach (var pair in SkinCss.Attributes(SkinResolver.Resolve("classic", skin, null).Skin))
        {
            scope.GetAttribute(pair.Key).ShouldBe(pair.Value);
        }
        if (radius != SkinValues.RadiusSoft)
        {
            scope.GetAttribute("style")!.ShouldContain("--ts-radius:" + SkinValues.RadiusRem(radius));
        }
    }
}
