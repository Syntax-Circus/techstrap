using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Tests.Knowledge;

/// <summary>The recent-articles list: the same neutral 404 for an unknown or inactive product as the category list, and a clamped count.</summary>
public sealed class ListRecentPublicKbArticlesHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Updated = new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);

    private readonly KbFixture _kb = new();

    public ListRecentPublicKbArticlesHandlerTests()
    {
        _kb.Products.GetByKeyAsync("orbitly", Arg.Any<CancellationToken>()).Returns(_kb.Orbitly);
        var dormant = Product.Create("dormant", "Dormant", "DOR", null, _kb.Clock).Value;
        dormant.SetActive(false);
        _kb.Products.GetByKeyAsync("dormant", Arg.Any<CancellationToken>()).Returns(dormant);
    }

    private ListRecentPublicKbArticlesRequestHandler Handler() => new(_kb.Products, _kb.KnowledgeBase);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("nobody")]
    [InlineData("dormant")]
    public async Task An_unknown_or_inactive_product_is_the_neutral_404_and_never_reaches_the_repository(string? key)
    {
        var result = await Handler().HandleAsync(key, 8, Ct);

        result.IsSuccess.ShouldBeFalse();
        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe("kb-category-not-found");
        error.Kind.ShouldBe(ResultErrorKind.NotFound);
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().ListPublicRecentArticlesAsync(default, default, Ct);
    }

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(8, 8)]
    [InlineData(20, 20)]
    [InlineData(21, 20)]
    [InlineData(1000, 20)]
    public async Task The_count_is_clamped_to_between_one_and_twenty(int requested, int used)
    {
        _kb.KnowledgeBase.ListPublicRecentArticlesAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        await Handler().HandleAsync("orbitly", requested, Ct);

        await _kb.KnowledgeBase.Received(1).ListPublicRecentArticlesAsync(_kb.Orbitly.Id, used, Ct);
    }

    [Fact]
    public async Task The_rows_become_plain_summary_dtos_in_the_order_the_repository_gives_them_and_none_is_a_200_with_an_empty_list()
    {
        _kb.KnowledgeBase.ListPublicRecentArticlesAsync(_kb.Orbitly.Id, 8, Arg.Any<CancellationToken>()).Returns(
        [
            new PublicKbCategoryArticle("reset-password", "Reset your password", "How to reset", "account", "Account", "orbitly", Updated),
            new PublicKbCategoryArticle("shared-tips", "Shared tips", null, "general", "General", null, Updated.AddDays(-1)),
        ]);

        var result = await Handler().HandleAsync(" orbitly ", 8, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(
        [
            new PublicKbArticleSummaryDto("reset-password", "Reset your password", "How to reset", "account", "Account", "orbitly", Updated),
            new PublicKbArticleSummaryDto("shared-tips", "Shared tips", null, "general", "General", null, Updated.AddDays(-1)),
        ]);

        _kb.KnowledgeBase.ListPublicRecentArticlesAsync(_kb.Orbitly.Id, 3, Arg.Any<CancellationToken>()).Returns([]);
        var empty = await Handler().HandleAsync("orbitly", 3, Ct);
        empty.IsSuccess.ShouldBeTrue();
        empty.Value.ShouldBeEmpty();
    }
}
