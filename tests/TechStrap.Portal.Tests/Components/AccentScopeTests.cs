using Bunit;
using TechStrap.Portal.Components.Ui;

namespace TechStrap.Portal.Tests.Components;

public sealed class AccentScopeTests : BunitContext
{
    [Theory]
    [InlineData("#F59E0B", "--ts-accent:#F59E0B;--ts-on-accent:#000000;--ts-accent-ink:#9D6507")]
    [InlineData("#7c3aed", "--ts-accent:#7C3AED;--ts-on-accent:#FFFFFF;--ts-accent-ink:#7C3AED")]
    public void Sets_exactly_the_three_derived_properties(string accent, string expectedStyle)
    {
        var cut = Render<AccentScope>(p => p.Add(s => s.Accent, accent).AddChildContent("<p>x</p>"));

        cut.Find("div.ts-accent-scope").GetAttribute("style").ShouldBe(expectedStyle);
        cut.Find("div.ts-accent-scope p").TextContent.ShouldBe("x");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#123456;background:url(x)")]
    public void A_malformed_accent_sets_no_style_so_the_fallbacks_apply_and_nothing_is_injected(string? accent)
    {
        var cut = Render<AccentScope>(p => p.Add(s => s.Accent, accent).AddChildContent("<p>x</p>"));

        cut.Find("div.ts-accent-scope").HasAttribute("style").ShouldBeFalse();
    }
}
