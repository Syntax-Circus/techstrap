using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Tests.Knowledge;

/// <summary>PHASE-09c Review Focus 2 (content leaks): the category list is the same neutral 404 for everything that is not a visible category with a published article.</summary>
public sealed class ListPublicKbCategoryArticlesHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Updated = new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);

    private readonly KbFixture _kb = new();

    public ListPublicKbCategoryArticlesHandlerTests()
    {
        _kb.Products.GetByKeyAsync("orbitly", Arg.Any<CancellationToken>()).Returns(_kb.Orbitly);
        var dormant = Product.Create("dormant", "Dormant", "DOR", null, _kb.Clock).Value;
        dormant.SetActive(false);
        _kb.Products.GetByKeyAsync("dormant", Arg.Any<CancellationToken>()).Returns(dormant);
    }

    private ListPublicKbCategoryArticlesRequestHandler Handler() => new(_kb.Products, _kb.KnowledgeBase);

    [Theory]
    [InlineData(null, "account")]
    [InlineData("", "account")]
    [InlineData("  ", "account")]
    [InlineData("nobody", "account")]
    [InlineData("dormant", "account")]
    [InlineData("orbitly", null)]
    [InlineData("orbitly", "")]
    [InlineData("orbitly", "   ")]
    [InlineData("orbitly", "ACCOUNT")]
    [InlineData("orbitly", "ac\0count")]
    [InlineData("orbitly", "ac\ud800count")]
    [InlineData("orbitly", "two--hyphens")]
    [InlineData("orbitly", "-leading")]
    public async Task An_unknown_inactive_or_malformed_product_or_slug_is_the_category_404_and_never_reaches_the_repository(string? key, string? category)
    {
        var result = await Handler().HandleAsync(key, category, 1, 10, Ct);

        result.IsSuccess.ShouldBeFalse();
        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe("kb-category-not-found");
        error.Kind.ShouldBe(ResultErrorKind.NotFound);
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().ListPublicCategoryArticlesAsync(default, default!, default, default, Ct);
    }

    [Fact]
    public async Task A_slug_longer_than_the_article_slug_limit_is_the_category_404()
    {
        var result = await Handler().HandleAsync("orbitly", new string('a', 81), 1, 10, Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-not-found");
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().ListPublicCategoryArticlesAsync(default, default!, default, default, Ct);
    }

    [Fact]
    public async Task A_category_the_product_cannot_see_or_that_is_empty_is_the_same_404()
    {
        _kb.KnowledgeBase.ListPublicCategoryArticlesAsync(_kb.Orbitly.Id, "account", 1, 10, Arg.Any<CancellationToken>()).Returns((PagedResult<PublicKbCategoryArticle>?)null);

        var result = await Handler().HandleAsync("orbitly", "account", 1, 10, Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-not-found");
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-5, 10)]
    [InlineData(10, 10)]
    [InlineData(25, 25)]
    [InlineData(26, 25)]
    [InlineData(1000, 25)]
    public async Task The_page_size_defaults_to_ten_and_is_capped_at_twenty_five(int requested, int used)
    {
        _kb.KnowledgeBase.ListPublicCategoryArticlesAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<PublicKbCategoryArticle>([], 1, used, 1));

        await Handler().HandleAsync("orbitly", "account", 1, requested, Ct);

        await _kb.KnowledgeBase.Received(1).ListPublicCategoryArticlesAsync(_kb.Orbitly.Id, "account", 1, used, Ct);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public async Task The_page_number_is_normalised_before_the_query(int requested, int used)
    {
        _kb.KnowledgeBase.ListPublicCategoryArticlesAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<PublicKbCategoryArticle>([], used, 10, 100));

        await Handler().HandleAsync("orbitly", "account", requested, 10, Ct);

        await _kb.KnowledgeBase.Received(1).ListPublicCategoryArticlesAsync(_kb.Orbitly.Id, "account", used, 10, Ct);
    }

    [Fact]
    public async Task A_padded_slug_is_trimmed_and_the_rows_become_plain_summary_dtos_with_the_paging_the_repository_reports()
    {
        _kb.KnowledgeBase.ListPublicCategoryArticlesAsync(_kb.Orbitly.Id, "account", 2, 10, Arg.Any<CancellationToken>()).Returns(new PagedResult<PublicKbCategoryArticle>(
        [
            new PublicKbCategoryArticle("reset-password", "Reset your password", "How to reset", "account", "Account", "orbitly", Updated),
            new PublicKbCategoryArticle("shared-tips", "Shared tips", null, "account", "Account", null, Updated.AddDays(-1)),
        ], 2, 10, 12));

        var result = await Handler().HandleAsync(" orbitly ", " account ", 2, 10, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldSatisfyAllConditions(
            page => page.Page.ShouldBe(2),
            page => page.PageSize.ShouldBe(10),
            page => page.TotalCount.ShouldBe(12));
        result.Value.Items.ShouldBe(
        [
            new PublicKbArticleSummaryDto("reset-password", "Reset your password", "How to reset", "account", "Account", "orbitly", Updated),
            new PublicKbArticleSummaryDto("shared-tips", "Shared tips", null, "account", "Account", null, Updated.AddDays(-1)),
        ]);
    }

    [Fact]
    public void The_summary_dto_has_no_body_no_author_no_id_and_no_status()
    {
        typeof(PublicKbArticleSummaryDto).GetProperties().Select(property => property.Name).Order().ShouldBe(
            ["CategoryName", "CategorySlug", "ProductKey", "Slug", "Summary", "Title", "UpdatedAt"]);
    }
}
