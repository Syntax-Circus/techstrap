using Bunit;
using TechStrap.Admin.Features.Kb;
using TechStrap.Contracts.Kb;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The status is always the word itself; the border style only repeats it.</summary>
public sealed class KbStatusBadgeTests : BunitContext
{
    [Theory]
    [InlineData(KbArticleStatuses.Draft, "ts-pill")]
    [InlineData(KbArticleStatuses.Published, "ts-pill ts-pill--on")]
    [InlineData(KbArticleStatuses.Archived, "ts-pill ts-pill--off")]
    public void Each_status_shows_its_word_and_its_own_style(string status, string css)
    {
        var cut = Render<KbStatusBadge>(p => p.Add(b => b.Status, status));

        cut.Find("span").TextContent.ShouldBe(status);
        cut.Find("span").GetAttribute("class").ShouldBe(css);
        cut.Find("span").GetAttribute("data-status").ShouldBe(status);
    }

    [Fact]
    public void A_status_the_admin_does_not_know_is_shown_as_the_api_sent_it_in_the_plain_style()
    {
        var cut = Render<KbStatusBadge>(p => p.Add(b => b.Status, "Review"));

        cut.Find("span").TextContent.ShouldBe("Review");
        cut.Find("span").GetAttribute("class").ShouldBe("ts-pill");
    }
}
