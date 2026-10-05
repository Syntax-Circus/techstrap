using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Application.Tests.Knowledge;

public sealed class ListAndGetKbArticleHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly KbFixture _kb = new();

    private static ListKbArticlesRequest Request(
        Guid? productId = null, bool sharedOnly = false, bool includeShared = true, string? status = null, Guid? categoryId = null, string? text = null, int page = 1, int pageSize = 25) =>
        new(productId, sharedOnly, includeShared, status, categoryId, text, page, pageSize);

    [Fact]
    public async Task A_blank_text_lists_with_every_filter_forwarded()
    {
        var article = _kb.StoredArticle(KbArticleStatus.Published, _kb.Orbitly.Id);
        var categoryId = Guid.NewGuid();
        _kb.KnowledgeBase.ListArticlesAsync(Arg.Any<KbArticleQuery>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<KbArticle>([article], 2, 10, 11));

        var result = await new ListKbArticlesRequestHandler(_kb.KnowledgeBase).HandleAsync(
            Request(_kb.Orbitly.Id, includeShared: false, status: "published", categoryId: categoryId, text: "  ", page: 2, pageSize: 10), Ct);

        await _kb.KnowledgeBase.Received(1).ListArticlesAsync(
            new KbArticleQuery(_kb.Orbitly.Id, false, KbArticleStatus.Published, categoryId, 2, 10, false), Ct);
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().SearchAsync(default!, Ct);
        result.Value.ShouldSatisfyAllConditions(
            page => page.Page.ShouldBe(2),
            page => page.PageSize.ShouldBe(10),
            page => page.TotalCount.ShouldBe(11),
            page => page.Items.ShouldHaveSingleItem().ShouldBe(new KbArticleListItemDto(article.Id, article.ProductId, null, "reset-password", "Reset your password", "Published", article.UpdatedAt)));
    }

    [Fact]
    public async Task A_text_searches_best_match_first_with_the_same_filters()
    {
        _kb.KnowledgeBase.SearchAsync(Arg.Any<KbSearchQuery>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<KbArticle>([], 1, 25, 0));

        await new ListKbArticlesRequestHandler(_kb.KnowledgeBase).HandleAsync(Request(sharedOnly: true, text: "router reset"), Ct);

        await _kb.KnowledgeBase.Received(1).SearchAsync(new KbSearchQuery("router reset", null, true, null, null, 1, 25, true), Ct);
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().ListArticlesAsync(default!, Ct);
    }

    [Fact]
    public async Task A_status_that_is_not_one_of_the_three_is_a_validation_error_on_status()
    {
        var result = await new ListKbArticlesRequestHandler(_kb.KnowledgeBase).HandleAsync(Request(status: "Live"), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Code.ShouldBe("status-invalid"),
            error => error.Target.ShouldBe("status"));
    }

    [Fact]
    public async Task Get_returns_the_article_with_its_markdown_and_version_in_any_status()
    {
        var article = _kb.StoredArticle(KbArticleStatus.Archived);

        var result = await new GetKbArticleRequestHandler(_kb.KnowledgeBase).HandleAsync(article.Id, Ct);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Status.ShouldBe("Archived"),
            dto => dto.BodyMarkdown.ShouldBe("Steps."),
            dto => dto.Version.ShouldBe(5u));
    }

    [Fact]
    public async Task Get_of_an_unknown_article_is_not_found()
    {
        var result = await new GetKbArticleRequestHandler(_kb.KnowledgeBase).HandleAsync(Guid.NewGuid(), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-article-not-found");
    }
}
