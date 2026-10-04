using Bunit;
using TechStrap.Admin.Components.Ui;
using TechStrap.Contracts.Branding;

namespace TechStrap.Admin.Tests.Components;

public sealed class AccentPreviewTests : BunitContext
{
    [Fact]
    public void It_sets_the_three_accent_properties_the_portal_sets_from_the_one_shared_rule()
    {
        ProductAccent.TryDerive("#1D4ED8", out var expected).ShouldBeTrue();

        var cut = Render<AccentPreview>(p => p.Add(c => c.Accent, "#1D4ED8").Add(c => c.DisplayName, "Orbitly"));

        cut.Find(".ts-accent-preview").GetAttribute("style")
            .ShouldBe($"--ts-accent:{expected.Accent};--ts-on-accent:{expected.OnAccent};--ts-accent-ink:{expected.AccentInk}");
        cut.Find(".ts-accent-preview-name").TextContent.ShouldBe("Orbitly");
        cut.FindAll("[role=note]").ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#1D4ED8FF")]
    public void A_blank_or_malformed_colour_sets_nothing_so_the_stylesheet_fallbacks_apply_and_no_note_is_shown(string? accent)
    {
        var cut = Render<AccentPreview>(p => p.Add(c => c.Accent, accent));

        cut.Find(".ts-accent-preview").HasAttribute("style").ShouldBeFalse();
        cut.FindAll("[role=note]").ShouldBeEmpty();
    }

    [Fact]
    public void A_low_contrast_colour_gets_an_information_note_and_is_never_refused()
    {
        var cut = Render<AccentPreview>(p => p.Add(c => c.Accent, "#FFEB3B"));

        var note = cut.Find("p[role=note]");
        note.TextContent.ShouldContain("This colour has low contrast on white");
        note.TextContent.ShouldContain("TechStrap darkens it wherever it is used for text");
        cut.Find(".ts-accent-preview").HasAttribute("style").ShouldBeTrue();
    }

    [Fact]
    public void The_ratio_in_the_note_is_rounded_down_so_it_never_claims_the_target_was_met()
    {
        ProductAccent.ContrastRatio("#777777", "#FFFFFF").ShouldBeLessThan(ProductAccent.MinimumTextContrast);

        var cut = Render<AccentPreview>(p => p.Add(c => c.Accent, "#777777"));

        cut.Find("p[role=note]").TextContent.ShouldContain("(4.4:1;");
    }

    [Fact]
    public void The_logo_is_drawn_only_when_an_address_is_given_with_no_alt_text_and_no_referrer()
    {
        var without = Render<AccentPreview>(p => p.Add(c => c.Accent, "#1D4ED8"));
        without.FindAll("img").ShouldBeEmpty();

        var with = Render<AccentPreview>(p => p.Add(c => c.LogoUrl, "https://cdn.example.com/logo.png"));
        var logo = with.Find("img.ts-accent-preview-logo");
        logo.GetAttribute("src").ShouldBe("https://cdn.example.com/logo.png");
        logo.GetAttribute("alt").ShouldBe(string.Empty);
        logo.GetAttribute("referrerpolicy").ShouldBe("no-referrer");
    }

    [Fact]
    public void The_display_name_is_encoded_never_rendered_as_markup()
    {
        var cut = Render<AccentPreview>(p => p.Add(c => c.DisplayName, "<b onclick=\"x()\">Orbitly</b>"));

        cut.Markup.ShouldContain("&lt;b onclick=");
        cut.FindAll(".ts-accent-preview-name b").ShouldBeEmpty();
    }
}
