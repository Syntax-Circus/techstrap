using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Content;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Knowledge;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Tests.Knowledge;

public sealed class PublicKbHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly KbFixture _kb = new();
    private readonly IKbContentRenderer _renderer = Substitute.For<IKbContentRenderer>();

    public PublicKbHandlerTests()
    {
        _kb.Products.GetByKeyAsync("orbitly", Arg.Any<CancellationToken>()).Returns(_kb.Orbitly);
        _kb.Products.GetByKeyAsync("paperplane", Arg.Any<CancellationToken>()).Returns(_kb.Paperplane);
        var dormant = Product.Create("dormant", "Dormant", "DOR", null, _kb.Clock).Value;
        dormant.SetActive(false);
        _kb.Products.GetByKeyAsync("dormant", Arg.Any<CancellationToken>()).Returns(dormant);
    }

    private SearchPublicKbArticlesRequestHandler Searcher() => new(_kb.Products, _kb.KnowledgeBase);

    private GetPublishedKbArticleRequestHandler Reader() => new(_kb.Products, _kb.KnowledgeBase, _renderer);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("nobody")]
    [InlineData("dormant")]
    public async Task An_unknown_inactive_or_blank_product_gives_an_empty_page_on_every_public_list_and_never_reaches_the_repository(string? key)
    {
        var search = await Searcher().HandleAsync(key, "router", null, 1, 10, Ct);
        var categories = await new ListPublicKbCategoriesRequestHandler(_kb.Products, _kb.KnowledgeBase).HandleAsync(key, Ct);
        var sitemap = await new GetKbSitemapRequestHandler(_kb.Products, _kb.KnowledgeBase).HandleAsync(key, Ct);

        search.Value.ShouldSatisfyAllConditions(page => page.Items.ShouldBeEmpty(), page => page.TotalCount.ShouldBe(0));
        categories.Value.ShouldBeEmpty();
        sitemap.Value.ShouldBeEmpty();
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().SearchPublicAsync(default!, Ct);
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().ListPublicCategoriesAsync(default, Ct);
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().ListPublicSitemapAsync(default, Ct);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_search_text_gives_an_empty_page_without_a_query(string? text)
    {
        var result = await Searcher().HandleAsync("orbitly", text, null, 1, 10, Ct);

        result.Value.Items.ShouldBeEmpty();
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().SearchPublicAsync(default!, Ct);
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
        _kb.KnowledgeBase.SearchPublicAsync(Arg.Any<PublicKbSearchQuery>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<PublicKbSearchHit>([], 1, used, 0));

        await Searcher().HandleAsync("orbitly", "router", "faq", 1, requested, Ct);

        await _kb.KnowledgeBase.Received(1).SearchPublicAsync(new PublicKbSearchQuery("router", _kb.Orbitly.Id, "faq", 1, used), Ct);
    }

    [Fact]
    public async Task Every_search_field_including_the_snippet_passes_through_as_plain_text()
    {
        _kb.KnowledgeBase.SearchPublicAsync(Arg.Any<PublicKbSearchQuery>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<PublicKbSearchHit>(
            [new PublicKbSearchHit("reset", "Fish & Chips", "Use it & \"quotes\"", "general", "Q&A", null)], 1, 10, 1));

        var result = await Searcher().HandleAsync("orbitly", "reset", null, 1, 10, Ct);

        var hit = result.Value.Items.ShouldHaveSingleItem();
        hit.Snippet.ShouldBe("Use it & \"quotes\"");
        hit.ShouldSatisfyAllConditions(item => item.Title.ShouldBe("Fish & Chips"), item => item.CategoryName.ShouldBe("Q&A"), item => item.ProductKey.ShouldBeNull());
    }

    [Theory]
    [InlineData("ac\0me", "account", "reset-password")]
    [InlineData("orbitly", "acc\0ount", "reset-password")]
    [InlineData("orbitly", "account", "reset\0password")]
    [InlineData("orbitly", "account", "reset-password{HI}")]
    [InlineData("orbitly", "Account", "reset-password")]
    [InlineData("orbitly", "account", "reset password")]
    [InlineData("orbitly", "account", "../etc")]
    public async Task A_key_or_slug_that_is_not_a_slug_is_the_uniform_not_found_with_no_repository_call(string product, string category, string slug)
    {
        slug = slug.Replace("{HI}", "\uD800");
        var result = await Reader().HandleAsync(product, category, slug, Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-article-not-found");
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().GetPublicArticleAsync(default, default!, default!, Ct);
    }

    [Theory]
    [InlineData("ac\0me", null)]
    [InlineData("orbitly", "faq\0")]
    [InlineData("orbitly", "FAQ")]
    [InlineData("orbitly", "a b")]
    public async Task A_malformed_product_key_or_category_slug_gives_an_empty_search_without_a_query(string product, string? category)
    {
        var result = await Searcher().HandleAsync(product, "router", category, 1, 10, Ct);

        result.Value.Items.ShouldBeEmpty();
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().SearchPublicAsync(default!, Ct);
    }

    [Fact]
    public async Task A_malformed_product_key_gives_empty_categories_and_sitemap_without_a_query()
    {
        var categories = await new ListPublicKbCategoriesRequestHandler(_kb.Products, _kb.KnowledgeBase).HandleAsync("ac\0me", Ct);
        var sitemap = await new GetKbSitemapRequestHandler(_kb.Products, _kb.KnowledgeBase).HandleAsync("ac\0me", Ct);

        categories.Value.ShouldBeEmpty();
        sitemap.Value.ShouldBeEmpty();
        await _kb.Products.DidNotReceiveWithAnyArgs().GetByKeyAsync(default!, Ct);
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().ListPublicCategoriesAsync(default, Ct);
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().ListPublicSitemapAsync(default, Ct);
    }

    [Fact]
    public async Task The_article_is_rendered_by_the_shared_renderer_and_carries_no_author_or_id()
    {
        var published = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        var article = _kb.StoredArticle(KbArticleStatus.Published, _kb.Orbitly.Id, Guid.NewGuid(), "reset-password", published);
        _kb.KnowledgeBase.GetPublicArticleAsync(_kb.Orbitly.Id, "account", "reset-password", Ct).Returns(new PublicKbArticleView(article, "account", "Account", "orbitly"));
        _renderer.Render("Steps.").Returns("<p>Steps.</p>\n");

        var result = await Reader().HandleAsync(" orbitly ", " account ", " reset-password ", Ct);

        result.Value.ShouldBe(new PublishedKbArticleDto("orbitly", "account", "Account", "reset-password", "Reset your password", "Short", "<p>Steps.</p>\n", published, article.UpdatedAt));
    }

    [Theory]
    [InlineData("nobody", "account", "reset-password")]
    [InlineData("dormant", "account", "reset-password")]
    [InlineData("orbitly", "account", "missing")]
    [InlineData("orbitly", "", "reset-password")]
    [InlineData("orbitly", "account", " ")]
    [InlineData(null, "account", "reset-password")]
    public async Task Everything_that_is_not_a_visible_published_article_is_the_same_not_found(string? product, string category, string slug)
    {
        var result = await Reader().HandleAsync(product, category, slug, Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.NotFound),
            error => error.Code.ShouldBe("kb-article-not-found"),
            error => error.Message.ShouldBe("That article does not exist."));
        _renderer.DidNotReceiveWithAnyArgs().Render(default!);
    }

    [Fact]
    public async Task An_article_without_a_publish_time_is_not_served()
    {
        var article = _kb.StoredArticle(KbArticleStatus.Published, _kb.Orbitly.Id, Guid.NewGuid(), "reset-password", null);
        _kb.KnowledgeBase.GetPublicArticleAsync(_kb.Orbitly.Id, "account", "reset-password", Ct).Returns(new PublicKbArticleView(article, "account", "Account", "orbitly"));

        var result = await Reader().HandleAsync("orbitly", "account", "reset-password", Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-article-not-found");
    }

    [Fact]
    public async Task Categories_and_sitemap_entries_are_mapped_from_the_repository_rows()
    {
        var category = KbCategory.Restore(Guid.NewGuid(), null, "Account", "account", "Sign-in help", 1, 3);
        _kb.KnowledgeBase.ListPublicCategoriesAsync(_kb.Orbitly.Id, Ct).Returns([new PublicKbCategoryCount(category, 4)]);
        var updated = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        _kb.KnowledgeBase.ListPublicSitemapAsync(_kb.Orbitly.Id, Ct).Returns([new PublicKbSitemapRow(null, "account", "reset-password", updated)]);

        var categories = await new ListPublicKbCategoriesRequestHandler(_kb.Products, _kb.KnowledgeBase).HandleAsync("orbitly", Ct);
        var sitemap = await new GetKbSitemapRequestHandler(_kb.Products, _kb.KnowledgeBase).HandleAsync("orbitly", Ct);

        categories.Value.ShouldBe([new PublicKbCategoryDto("account", "Account", "Sign-in help", 4)]);
        sitemap.Value.ShouldBe([new KbSitemapEntryDto(null, "account", "reset-password", updated)]);
    }
}
