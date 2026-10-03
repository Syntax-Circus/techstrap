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
}
