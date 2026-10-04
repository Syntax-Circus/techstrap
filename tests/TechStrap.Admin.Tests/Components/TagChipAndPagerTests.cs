using Bunit;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Tests.Support;

namespace TechStrap.Admin.Tests.Components;

public sealed class TagChipAndPagerTests : AdminComponentTest
{
    [Theory]
    [InlineData("#FFFF00", "#000000")]
    [InlineData("#1D4ED8", "#FFFFFF")]
    [InlineData("#dc2626", "#FFFFFF")]
    public void A_tag_chip_derives_a_legible_foreground_from_its_colour(string colour, string expectedForeground)
    {
        var cut = Render<TagChip>(p => p.Add(c => c.Name, "bug").Add(c => c.Colour, colour));

        var chip = cut.Find("span.ts-tag");
        chip.TextContent.ShouldBe("bug");
        chip.GetAttribute("style")!.ShouldContain($"color:{expectedForeground}");
        chip.GetAttribute("style")!.ShouldContain($"background-color:{colour.ToUpperInvariant()}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("url(javascript:alert(1))")]
    public void A_malformed_colour_renders_the_plain_chip_and_never_reaches_a_style_attribute(string? colour)
    {
        var cut = Render<TagChip>(p => p.Add(c => c.Name, "bug").Add(c => c.Colour, colour));

        cut.Find("span.ts-tag").HasAttribute("style").ShouldBeFalse();
        cut.Markup.ShouldNotContain("javascript");
    }

    [Fact]
    public void The_pager_summarises_the_range_and_reports_the_next_page_once()
    {
        var requested = new List<int>();
        var cut = Render<PagerControl>(p => p
            .Add(c => c.Page, 2)
            .Add(c => c.PageSize, 25)
            .Add(c => c.TotalCount, 163)
            .Add(c => c.OnPageChanged, page => requested.Add(page)));

        cut.Find(".ts-pager-summary").TextContent.ShouldBe("26–50 of 163");
        cut.Find(".ts-pager-page").TextContent.ShouldBe("Page 2 of 7");

        cut.FindAll("button")[1].Click();

        requested.ShouldBe([3]);
    }

    [Fact]
    public void Previous_is_disabled_on_the_first_page_and_next_on_the_last()
    {
        var first = Render<PagerControl>(p => p.Add(c => c.Page, 1).Add(c => c.PageSize, 25).Add(c => c.TotalCount, 60));
        var last = Render<PagerControl>(p => p.Add(c => c.Page, 3).Add(c => c.PageSize, 25).Add(c => c.TotalCount, 60));

        first.FindAll("button")[0].HasAttribute("disabled").ShouldBeTrue();
        first.FindAll("button")[1].HasAttribute("disabled").ShouldBeFalse();
        last.FindAll("button")[1].HasAttribute("disabled").ShouldBeTrue();
        last.Find(".ts-pager-summary").TextContent.ShouldBe("51–60 of 60");
    }

    [Fact]
    public void A_single_page_shows_the_summary_without_buttons_and_an_empty_list_shows_nothing()
    {
        var single = Render<PagerControl>(p => p.Add(c => c.Page, 1).Add(c => c.PageSize, 25).Add(c => c.TotalCount, 4));
        var none = Render<PagerControl>(p => p.Add(c => c.Page, 1).Add(c => c.PageSize, 25).Add(c => c.TotalCount, 0));

        single.Find(".ts-pager-summary").TextContent.ShouldBe("1–4 of 4");
        single.FindAll("button").ShouldBeEmpty();
        none.Markup.Trim().ShouldBeEmpty();
    }
}
