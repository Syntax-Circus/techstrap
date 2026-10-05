using TechStrap.Application.Knowledge;
using Microsoft.Extensions.Time.Testing;
using TechStrap.Domain.Knowledge;
using TechStrap.Domain.Products;
using TechStrap.Domain.Rules;

namespace TechStrap.Application.Tests.Knowledge;

/// <summary>
/// <see cref="PublicProductScope.IsSlug"/> repeats the slug shape of the domain's slug guard because anonymous route and query values must be checked before a query and the guard builds an error each time.
/// The guard is internal to the Domain, so it is reached through what uses it: <see cref="Product.Create"/> (key, <see cref="DomainLimits.SlugMaxLength"/>) and <see cref="KbArticle.Create"/> (slug,
/// <see cref="DomainLimits.KbSlugMaxLength"/>). This keeps the two equal on one corpus: an edit to either regex that the other does not follow fails here.
/// </summary>
public sealed class PublicProductScopeParityTests
{
    public static TheoryData<string> Corpus() => new()
    {
        null!,
        "",
        " ",
        "a",
        "orbitly",
        "reset-password",
        "  padded  ",
        "two--hyphens",
        "-leading",
        "trailing-",
        "-",
        "UPPER",
        "Mixed-Case",
        "with space",
        "under_score",
        "dot.dot",
        "slash/slash",
        "café",
        "ab\u0000cd",
        "ab\ud800cd",
        "tab\tinside",
        "line\nbreak",
        "digits-123-456",
        "123",
        new string('a', 40),
        new string('a', 41),
        new string('a', 80),
        new string('a', 81),
        "a-" + new string('b', 38),
        "a-" + new string('b', 39),
    };

    private static readonly FakeTimeProvider Clock = new();

    private static bool GuardAcceptsProductKey(string? value) => Product.Create(value, "Name", "ABC", null, Clock).IsSuccess;

    private static bool GuardAcceptsArticleSlug(string? value) => KbArticle.Create(null, null, value, "Title", "Summary", "Body", Guid.NewGuid(), Clock).IsSuccess;

    [Theory]
    [MemberData(nameof(Corpus))]
    public void The_public_slug_check_agrees_with_the_domain_guard_at_both_limits(string? value)
    {
        PublicProductScope.IsSlug(value, DomainLimits.SlugMaxLength).ShouldBe(GuardAcceptsProductKey(value), $"product key {value}");
        PublicProductScope.IsSlug(value, DomainLimits.KbSlugMaxLength).ShouldBe(GuardAcceptsArticleSlug(value), $"article slug {value}");
    }
}
