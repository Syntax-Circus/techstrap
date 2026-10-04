using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>The settings pages (PHASE-07b): outside text wraps inside the page, and the branding preview wears the same three accent properties the portal sets.</summary>
public sealed class SettingsStyleTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    [Theory]
    [InlineData(".ts-settings-table td")]
    [InlineData(".ts-readonly dd")]
    [InlineData(".ts-accent-preview-name")]
    public void Long_unbroken_text_wraps_anywhere(string selector)
    {
        Css.Declarations(selector)["overflow-wrap"].ShouldBe("anywhere");
    }

    [Fact]
    public void The_preview_bar_and_button_use_the_accent_and_its_on_colour_and_the_link_uses_the_ink()
    {
        Css.Declarations(".ts-accent-preview-bar")["background"].ShouldBe("var(--ts-accent)");
        Css.Declarations(".ts-accent-preview-bar")["color"].ShouldBe("var(--ts-on-accent)");
        Css.Declarations(".ts-accent-preview-button")["background"].ShouldBe("var(--ts-accent)");
        Css.Declarations(".ts-accent-preview-button")["color"].ShouldBe("var(--ts-on-accent)");
        Css.Declarations(".ts-accent-preview-link")["color"].ShouldBe("var(--ts-accent-ink)");
    }

    [Fact]
    public void A_logo_can_never_stretch_the_preview()
    {
        var logo = Css.Declarations(".ts-accent-preview-logo");

        logo["max-width"].ShouldBe("160px");
        logo["max-height"].ShouldBe("32px");
    }
}
