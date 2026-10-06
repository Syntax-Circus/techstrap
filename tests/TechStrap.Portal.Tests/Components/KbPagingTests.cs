using TechStrap.Contracts.Kb;
using TechStrap.Portal.Components.Kb;

namespace TechStrap.Portal.Tests.Components;

/// <summary>The page number and the search text of the help-centre pages are read from text a visitor controls; neither may ever throw or send more than the API takes.</summary>
public sealed class KbPagingTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("2", 2)]
    [InlineData("10", 10)]
    [InlineData("9999", 9999)]
    [InlineData("0002", 2)]
    [InlineData("2147483647", int.MaxValue)]
    public void A_whole_number_of_one_or_more_is_the_page(string text, int expected) => KbPaging.Parse(text).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("+2")]
    [InlineData(" 2")]
    [InlineData("2 ")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("1e3")]
    [InlineData("2147483648")]
    [InlineData("99999999999999999999")]
    [InlineData("1,000")]
    public void Anything_else_is_page_one_and_never_throws(string? text) => KbPaging.Parse(text).ShouldBe(1);

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(-5, 10, 0)]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(25, 10, 3)]
    [InlineData(30, 10, 3)]
    [InlineData(5, 0, 0)]
    [InlineData(5, -1, 0)]
    [InlineData(int.MaxValue, 10, 214748365)]
    public void The_number_of_pages_rounds_up_and_a_nonsense_size_gives_none(int total, int size, int pages) => KbPaging.TotalPages(total, size).ShouldBe(pages);

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("  router  ", "router")]
    public void The_search_text_is_trimmed(string? text, string expected) => KbSearchText.Clean(text).ShouldBe(expected);

    [Fact]
    public void A_longer_text_is_cut_at_the_limit_without_splitting_a_surrogate_pair()
    {
        var plain = new string('a', 300);
        var pair = new string('a', KbLimits.MaxSearchTextChars - 1) + char.ConvertFromUtf32(0x1F600) + "tail";

        KbSearchText.Clean(plain).ShouldBe(new string('a', KbLimits.MaxSearchTextChars));
        KbSearchText.Clean(pair).ShouldBe(new string('a', KbLimits.MaxSearchTextChars - 1), "the cut would have fallen inside the pair, so the half pair is dropped");
        KbSearchText.Clean(new string('b', KbLimits.MaxSearchTextChars)).Length.ShouldBe(KbLimits.MaxSearchTextChars);
    }
}
