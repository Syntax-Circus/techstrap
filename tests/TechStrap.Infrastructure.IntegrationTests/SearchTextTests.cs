using TechStrap.Domain.Rules;
using TechStrap.Infrastructure.Persistence;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class SearchTextTests
{
    private const int Max = DomainLimits.SearchTextMaxLength;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t ")]
    public void Blank_text_normalizes_to_nothing(string? raw) => string.IsNullOrEmpty(SearchText.Normalize(raw)).ShouldBeTrue();

    [Fact]
    public void Text_is_trimmed() => SearchText.Normalize("  printer  ").ShouldBe("printer");

    [Fact]
    public void Text_exactly_at_the_limit_is_kept_and_longer_text_is_cut_to_it()
    {
        SearchText.Normalize(new string('a', Max)).ShouldBe(new string('a', Max));
        SearchText.Normalize(new string('a', Max + 50)).ShouldBe(new string('a', Max));
    }

    [Fact]
    public void A_cut_through_a_surrogate_pair_drops_the_orphaned_high_surrogate()
    {
        var split = new string('a', Max - 1) + "\U0001F600";

        var result = SearchText.Normalize(split)!;

        result.ShouldBe(new string('a', Max - 1));
        char.IsHighSurrogate(result[^1]).ShouldBeFalse();
    }

    [Fact]
    public void A_surrogate_pair_that_fits_entirely_is_kept()
    {
        var fits = new string('a', Max - 2) + "\U0001F600";

        SearchText.Normalize(fits).ShouldBe(fits);
    }

    [Theory]
    [InlineData("print\0er", "printer")]
    [InlineData("\0", "")]
    [InlineData("a\u0007b\u001Bc", "abc")]
    [InlineData("printer{HI}", "printer")]
    [InlineData("{LO}printer", "printer")]
    [InlineData("a{HI}{HI}b", "ab")]
    [InlineData("a\U0001F600b", "a\U0001F600b")]
    [InlineData("  \0 printer \0 ", "printer")]
    public void Control_characters_and_unpaired_surrogates_are_removed_and_valid_pairs_kept(string raw, string expected) =>
        SearchText.Normalize(raw.Replace("{HI}", "\uD800").Replace("{LO}", "\uDC00")).ShouldBe(expected);

    [Fact]
    public void Removal_happens_before_the_length_cap_so_stripped_characters_do_not_use_the_budget()
    {
        var raw = new string('\0', 50) + new string('a', Max);

        SearchText.Normalize(raw).ShouldBe(new string('a', Max));
    }
}
