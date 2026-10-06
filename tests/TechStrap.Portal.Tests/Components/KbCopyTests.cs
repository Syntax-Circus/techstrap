using TechStrap.Portal.Components;

namespace TechStrap.Portal.Tests.Components;

/// <summary>The few help-centre sentences that are built from a value (a name, a count, a page, a day). Plain text: the page encodes whatever is put in.</summary>
public sealed class KbCopyTests
{
    [Fact]
    public void The_titles_and_descriptions_name_the_product_and_the_page()
    {
        KbCopy.HomeTitle("Paperplane").ShouldBe("Paperplane Help Centre");
        KbCopy.HomeDescription("Paperplane").ShouldBe("Help articles and answers for Paperplane.");
        KbCopy.CategoryTitle("Accounts", "Paperplane", 1).ShouldBe("Accounts - Paperplane Help Centre");
        KbCopy.CategoryTitle("Accounts", "Paperplane", 0).ShouldBe("Accounts - Paperplane Help Centre");
        KbCopy.CategoryTitle("Accounts", "Paperplane", 3).ShouldBe("Accounts (page 3) - Paperplane Help Centre");
        KbCopy.CategoryDescription("Accounts", "Paperplane").ShouldBe("Help articles about Accounts for Paperplane.");
        KbCopy.SearchTitle("Paperplane").ShouldBe("Search - Paperplane Help Centre");
        KbCopy.SearchDescription("Paperplane").ShouldBe("Search the help articles for Paperplane.");
    }

    [Theory]
    [InlineData(0, "0 articles")]
    [InlineData(1, "1 article")]
    [InlineData(2, "2 articles")]
    [InlineData(1234, "1234 articles")]
    public void A_count_is_singular_only_for_one(int count, string expected) => KbCopy.ArticleCount(count).ShouldBe(expected);

    [Theory]
    [InlineData(0, "0 results")]
    [InlineData(1, "1 result")]
    [InlineData(25, "25 results")]
    public void A_result_count_is_singular_only_for_one(int count, string expected) => KbCopy.ResultCount(count).ShouldBe(expected);

    [Fact]
    public void The_page_of_text_and_the_updated_day_read_the_same_in_every_culture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            KbCopy.PageOf(2, 1234).ShouldBe("Page 2 of 1234");
            KbCopy.UpdatedOn(new DateTimeOffset(2026, 10, 5, 23, 30, 0, TimeSpan.FromHours(-5))).ShouldBe("Updated 6 Oct 2026", "the day is the UTC day, so the page reads the same everywhere");
            KbCopy.UpdatedOn(new DateTimeOffset(2026, 1, 9, 0, 0, 0, TimeSpan.Zero)).ShouldBe("Updated 9 Jan 2026");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }
}
