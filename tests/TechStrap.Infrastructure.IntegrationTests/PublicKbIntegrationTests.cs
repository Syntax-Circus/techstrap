using Microsoft.Extensions.DependencyInjection;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Application.Products;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Domain.Knowledge;
using TechStrap.Domain.Products;
using TechStrap.Infrastructure.Content;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// Review Focus 3 (unpublished or wrong-product content leaking publicly), against real Postgres: the four public handlers over the real repository and renderer.
/// Drafts and archived articles never appear in search, the article page, the categories or the sitemap; another product's articles never appear; shared ones do.
/// </summary>
public sealed class PublicKbIntegrationTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed record World(
        PersistenceTestHost Host,
        TicketScenario Scenario,
        KbCategory Shared,
        KbCategory AcmeCategory,
        KbCategory OrbitlyCategory,
        Dictionary<string, KbArticle> Articles);

    private static async Task<World> SeedAsync(PersistenceTestHost host)
    {
        var scenario = await TicketScenario.CreateAsync(host);
        var shared = KbCategory.Create(null, "general", "General", 5, host.Clock, "For everyone").Value;
        var acmeCategory = KbCategory.Create(scenario.Acme.Id, "acme-cat", "Acme things", 1, host.Clock).Value;
        var orbitlyCategory = KbCategory.Create(scenario.Orbitly.Id, "orb-cat", "Orbitly things", 1, host.Clock).Value;
        var empty = KbCategory.Create(scenario.Acme.Id, "empty-cat", "Empty", 9, host.Clock).Value;
        var articles = new Dictionary<string, KbArticle>
        {
            ["acme-published"] = Article(scenario, host, scenario.Acme.Id, acmeCategory, "acme-published", "Router reset guide", "How to reset the router", "# Steps\n\n| a | b |\n|---|---|\n| 1 | 2 |"),
            ["acme-draft"] = Article(scenario, host, scenario.Acme.Id, acmeCategory, "acme-draft", "Router draft", "A draft about the router", "body"),
            ["acme-archived"] = Article(scenario, host, scenario.Acme.Id, acmeCategory, "acme-archived", "Router archived", "An archived router note", "body"),
            ["orbitly-published"] = Article(scenario, host, scenario.Orbitly.Id, orbitlyCategory, "orbitly-published", "Router for Orbitly", "Orbitly router help", "body"),
            ["shared-published"] = Article(scenario, host, null, shared, "shared-published", "Shared router tips", "Tips for any router", "body"),
            ["shared-draft"] = Article(scenario, host, null, shared, "shared-draft", "Shared router draft", "A shared router draft", "body"),

            // Data the handlers would refuse to create but a hand edit or an older version could have left: each pairs an article with a category of the other scope.
            ["orbitly-in-acme-category"] = Article(scenario, host, scenario.Orbitly.Id, acmeCategory, "orbitly-in-acme-category", "Router misfiled by Orbitly", "A misfiled router note", "body"),
            ["acme-in-orbitly-category"] = Article(scenario, host, scenario.Acme.Id, orbitlyCategory, "acme-in-orbitly-category", "Router misfiled by Acme", "A misfiled router note", "body"),
        };
        Publish(host, articles["acme-published"], articles["orbitly-published"], articles["shared-published"], articles["orbitly-in-acme-category"], articles["acme-in-orbitly-category"]);
        Publish(host, articles["acme-archived"]);
        articles["acme-archived"].Archive(host.Clock).IsSuccess.ShouldBeTrue();
        (await host.CommitAsync(sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            foreach (var category in new[] { shared, acmeCategory, orbitlyCategory, empty })
            {
                kb.AddCategory(category);
            }

            foreach (var article in articles.Values)
            {
                kb.AddArticle(article);
            }

            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        return new World(host, scenario, shared, acmeCategory, orbitlyCategory, articles);
    }

    private static KbArticle Article(TicketScenario scenario, PersistenceTestHost host, Guid? productId, KbCategory category, string slug, string title, string summary, string body) =>
        KbArticle.Create(productId, category.Id, slug, title, summary, body, scenario.Agent.Id, host.Clock).Value;

    private static void Publish(PersistenceTestHost host, params KbArticle[] articles)
    {
        foreach (var article in articles)
        {
            article.Publish(host.Clock).IsSuccess.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Review_Focus_5_the_link_recheck_query_returns_only_published_articles_visible_to_the_product_with_their_current_category_and_slug()
    {
        await using var host = new PersistenceTestHost(Database);
        var world = await SeedAsync(host);
        var ids = world.Articles.Values.Select(article => article.Id).ToList();

        var targets = await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().ListPublicLinkTargetsAsync(world.Scenario.Acme.Id, ids, Ct));

        // Excluded: the draft, the archived article, Orbitly's article, the shared draft, an Orbitly article in an Acme category and an Acme article in an Orbitly category.
        targets.OrderBy(target => target.Slug).ShouldBe(
        [
            new PublicKbLinkTarget(world.Articles["acme-published"].Id, "acme-cat", "acme-published"),
            new PublicKbLinkTarget(world.Articles["shared-published"].Id, "general", "shared-published"),
        ]);
        (await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().ListPublicLinkTargetsAsync(world.Scenario.Acme.Id, [], Ct))).ShouldBeEmpty();
    }

    private static Task<T> WithHandlersAsync<T>(World world, Func<IServiceProvider, Task<T>> work) => world.Host.ReadAsync(work);

    private static ISearchPublicKbArticlesRequestHandler Search(IServiceProvider sp) =>
        new SearchPublicKbArticlesRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>());

    private static Task<List<string>> SearchSlugsAsync(World world, string productKey, string text, string? category = null) =>
        WithHandlersAsync(world, async sp =>
        {
            var result = await Search(sp).HandleAsync(productKey, text, category, 1, 25, Ct);
            return result.Value.Items.Select(item => item.Slug).ToList();
        });

    [Fact]
    public async Task Search_returns_only_published_articles_of_the_product_and_the_shared_space()
    {
        await using var host = new PersistenceTestHost(Database);
        var world = await SeedAsync(host);

        (await SearchSlugsAsync(world, "acme", "router")).Order().ShouldBe(["acme-published", "shared-published"]);
        (await SearchSlugsAsync(world, "orbitly", "router")).Order().ShouldBe(["orbitly-published", "shared-published"]);
    }

    [Fact]
    public async Task Archiving_a_published_article_takes_it_out_of_search_the_article_page_the_categories_and_the_sitemap()
    {
        await using var host = new PersistenceTestHost(Database);
        var world = await SeedAsync(host);
        var published = world.Articles["acme-published"];
        (await SearchSlugsAsync(world, "acme", "router")).ShouldContain("acme-published");
        (await host.CommitAsync(async sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            var loaded = (await kb.GetArticleAsync(published.Id, Ct))!;
            loaded.Archive(host.Clock).IsSuccess.ShouldBeTrue();
            kb.UpdateArticle(loaded);
        })).IsSuccess.ShouldBeTrue();

        (await SearchSlugsAsync(world, "acme", "router")).ShouldBe(["shared-published"]);
        var page = await WithHandlersAsync(world, sp => new GetPublishedKbArticleRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>(), new KbContentRenderer())
            .HandleAsync("acme", "acme-cat", "acme-published", Ct));
        page.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-article-not-found");
        var sitemap = await WithHandlersAsync(world, sp => new GetKbSitemapRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>()).HandleAsync("acme", Ct));
        sitemap.Value.Select(entry => entry.Slug).ShouldBe(["shared-published"]);
        var categories = await WithHandlersAsync(world, sp => new ListPublicKbCategoriesRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>()).HandleAsync("acme", Ct));
        categories.Value.Select(category => category.Slug).ShouldBe(["general"]);
    }

    [Fact]
    public async Task A_category_filter_narrows_the_search_to_that_category()
    {
        await using var host = new PersistenceTestHost(Database);
        var world = await SeedAsync(host);

        (await SearchSlugsAsync(world, "acme", "router", "general")).ShouldBe(["shared-published"]);
        (await SearchSlugsAsync(world, "acme", "router", "acme-cat")).ShouldBe(["acme-published"]);
        (await SearchSlugsAsync(world, "acme", "router", "orb-cat")).ShouldBeEmpty();
        (await SearchSlugsAsync(world, "acme", "router", "no-such-category")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_summary_snippet_is_plain_text_with_no_highlight_markup_and_is_not_html_encoded()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var category = KbCategory.Create(null, "general", "General", 1, host.Clock).Value;
        var hostile = Article(scenario, host, null, category, "hostile", "Printer help", "Reset the printer <script>alert(1)</script> & <img src=x onerror=alert(2)> now", "body");
        Publish(host, hostile);
        (await host.CommitAsync(sp =>
        {
            sp.GetRequiredService<IKbRepository>().AddCategory(category);
            sp.GetRequiredService<IKbRepository>().AddArticle(hostile);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();

        var result = await host.ReadAsync(sp => Search(sp).HandleAsync("acme", "printer", null, 1, 10, Ct));

        var snippet = result.Value.Items.ShouldHaveSingleItem().Snippet;
        // ts_headline drops <script> but keeps other markup such as <img>: the snippet is plain text, never HTML, and the consumer must encode it.
        snippet.ShouldNotContain("<script");
        snippet.ShouldContain("& <img");
        snippet.ShouldNotContain("<b>");
        snippet.ShouldNotContain("&amp;");
        snippet.ShouldNotContain("&lt;");
    }

    [Fact]
    public async Task The_snippet_comes_from_the_summary_never_the_body()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var category = KbCategory.Create(null, "general", "General", 1, host.Clock).Value;
        var article = Article(scenario, host, null, category, "body-match", "Unrelated title", "A short summary about nothing", "The secret-body-phrase quokka lives here");
        Publish(host, article);
        (await host.CommitAsync(sp =>
        {
            sp.GetRequiredService<IKbRepository>().AddCategory(category);
            sp.GetRequiredService<IKbRepository>().AddArticle(article);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();

        var result = await host.ReadAsync(sp => Search(sp).HandleAsync("acme", "quokka", null, 1, 10, Ct));

        var hit = result.Value.Items.ShouldHaveSingleItem();
        hit.Snippet.ShouldBe("A short summary about nothing");
        hit.Snippet.ShouldNotContain("quokka");
    }

    [Fact]
    public async Task The_article_page_serves_a_published_article_in_scope_and_nothing_else()
    {
        await using var host = new PersistenceTestHost(Database);
        var world = await SeedAsync(host);
        Task<Result<PublishedKbArticleDto>> Get(string product, string category, string slug) =>
            WithHandlersAsync(world, sp => new GetPublishedKbArticleRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>(), new KbContentRenderer())
                .HandleAsync(product, category, slug, Ct));

        var own = await Get("acme", "acme-cat", "acme-published");
        var shared = await Get("orbitly", "general", "shared-published");

        own.Value.ShouldSatisfyAllConditions(
            dto => dto.ProductKey.ShouldBe("acme"),
            dto => dto.CategorySlug.ShouldBe("acme-cat"),
            dto => dto.CategoryName.ShouldBe("Acme things"),
            dto => dto.Title.ShouldBe("Router reset guide"),
            dto => dto.Html.ShouldContain("<table>"),
            dto => dto.PublishedAt.ShouldBe(world.Articles["acme-published"].PublishedAt!.Value));
        shared.Value.ProductKey.ShouldBeNull();
        foreach (var (product, category, slug) in new[]
        {
            ("acme", "acme-cat", "acme-draft"),
            ("acme", "acme-cat", "acme-archived"),
            ("acme", "orb-cat", "orbitly-published"),
            ("acme", "orb-cat", "acme-published"),
            ("orbitly", "acme-cat", "acme-published"),
            ("acme", "general", "acme-published"),
            ("acme", "general", "shared-draft"),
            ("acme", "acme-cat", "no-such-article"),
            ("nope", "acme-cat", "acme-published"),
        })
        {
            (await Get(product, category, slug)).Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-article-not-found", $"{product}/{category}/{slug}");
        }
    }

    [Fact]
    public async Task An_inactive_product_is_indistinguishable_from_an_unknown_one()
    {
        await using var host = new PersistenceTestHost(Database);
        var world = await SeedAsync(host);
        var dormant = Product.Create("dormant", "Dormant", "DOR", null, host.Clock).Value;
        dormant.SetActive(false);
        (await host.CommitAsync(sp => { sp.GetRequiredService<IProductRepository>().Add(dormant); return Task.CompletedTask; })).IsSuccess.ShouldBeTrue();

        var inactive = await SearchSlugsAsync(world, "dormant", "router");
        var unknown = await SearchSlugsAsync(world, "never-heard-of-it", "router");
        var categories = await WithHandlersAsync(world, sp => new ListPublicKbCategoriesRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>()).HandleAsync("dormant", Ct));
        var sitemap = await WithHandlersAsync(world, sp => new GetKbSitemapRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>()).HandleAsync("dormant", Ct));
        var article = await WithHandlersAsync(world, sp => new GetPublishedKbArticleRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>(), new KbContentRenderer())
            .HandleAsync("dormant", "general", "shared-published", Ct));

        inactive.ShouldBeEmpty();
        unknown.ShouldBeEmpty();
        categories.Value.ShouldBeEmpty();
        sitemap.Value.ShouldBeEmpty();
        article.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-article-not-found");
    }

    [Fact]
    public async Task Categories_count_only_published_articles_the_product_can_see_and_leave_empty_ones_out()
    {
        await using var host = new PersistenceTestHost(Database);
        var world = await SeedAsync(host);

        var acme = await WithHandlersAsync(world, sp => new ListPublicKbCategoriesRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>()).HandleAsync("acme", Ct));
        var orbitly = await WithHandlersAsync(world, sp => new ListPublicKbCategoriesRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>()).HandleAsync("orbitly", Ct));

        acme.Value.ShouldBe([new PublicKbCategoryDto("acme-cat", "Acme things", null, 1), new PublicKbCategoryDto("general", "General", "For everyone", 1)]);
        orbitly.Value.ShouldBe([new PublicKbCategoryDto("orb-cat", "Orbitly things", null, 1), new PublicKbCategoryDto("general", "General", "For everyone", 1)]);
    }

    [Fact]
    public async Task The_sitemap_lists_published_articles_in_scope_newest_first_with_their_product_key_and_category()
    {
        await using var host = new PersistenceTestHost(Database);
        var world = await SeedAsync(host);

        var sitemap = await WithHandlersAsync(world, sp => new GetKbSitemapRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>()).HandleAsync("acme", Ct));

        sitemap.Value.Select(entry => (entry.ProductKey, entry.CategorySlug, entry.Slug)).Order().ShouldBe(
            [((string?)null, "general", "shared-published"), ("acme", "acme-cat", "acme-published")]);
    }

    [Theory]
    [InlineData("printer\0")]
    [InlineData("\0")]
    [InlineData("print\0er")]
    [InlineData("printer{HI}")]
    [InlineData("{LO}printer")]
    [InlineData("{HI}{HI}")]
    [InlineData("printer\u0007\u001B")]
    public async Task Search_text_with_a_nul_a_control_character_or_a_lone_surrogate_never_throws(string text)
    {
        await using var host = new PersistenceTestHost(Database);
        var world = await SeedAsync(host);

        text = text.Replace("{HI}", "\uD800").Replace("{LO}", "\uDC00");
        var result = await WithHandlersAsync(world, sp => Search(sp).HandleAsync("acme", text, null, 1, 10, Ct));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.ShouldAllBe(item => item.Slug != "acme-draft");
    }

    [Fact]
    public async Task Control_characters_are_stripped_so_the_remaining_word_still_matches()
    {
        await using var host = new PersistenceTestHost(Database);
        var world = await SeedAsync(host);

        (await SearchSlugsAsync(world, "acme", "router\0")).Order().ShouldBe(["acme-published", "shared-published"]);
    }

    [Theory]
    [InlineData("ac\0me", "acme-cat", "acme-published")]
    [InlineData("acme", "acme-cat\0", "acme-published")]
    [InlineData("acme", "acme-cat", "acme-published\0")]
    [InlineData("acme", "acme-cat", "acme-published{HI}")]
    [InlineData("acme", "ACME-CAT", "acme-published")]
    public async Task A_nul_or_malformed_key_or_slug_is_the_uniform_not_found_and_never_throws(string product, string category, string slug)
    {
        slug = slug.Replace("{HI}", "\uD800");
        await using var host = new PersistenceTestHost(Database);
        var world = await SeedAsync(host);

        var page = await WithHandlersAsync(world, sp => new GetPublishedKbArticleRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>(), new KbContentRenderer())
            .HandleAsync(product, category, slug, Ct));
        var categories = await WithHandlersAsync(world, sp => new ListPublicKbCategoriesRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>()).HandleAsync(product, Ct));
        var sitemap = await WithHandlersAsync(world, sp => new GetKbSitemapRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>()).HandleAsync(product, Ct));
        var search = await WithHandlersAsync(world, sp => Search(sp).HandleAsync(product, "router", category, 1, 10, Ct));

        page.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-article-not-found");
        if (product != "acme" || category != "acme-cat")
        {
            search.Value.Items.ShouldBeEmpty();
        }

        if (product != "acme")
        {
            categories.Value.ShouldBeEmpty();
            sitemap.Value.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task The_sitemap_is_ordered_newest_update_first()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var category = KbCategory.Create(null, "general", "General", 1, host.Clock).Value;
        var articles = new List<KbArticle>();
        foreach (var slug in new[] { "oldest", "middle", "newest" })
        {
            host.Clock.Advance(TimeSpan.FromMinutes(10));
            var article = Article(scenario, host, null, category, slug, slug, "s", "body");
            Publish(host, article);
            articles.Add(article);
        }

        (await host.CommitAsync(sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            kb.AddCategory(category);
            articles.ForEach(kb.AddArticle);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();

        var sitemap = await host.ReadAsync(sp => new GetKbSitemapRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>()).HandleAsync("acme", Ct));

        sitemap.Value.Select(entry => entry.Slug).ShouldBe(["newest", "middle", "oldest"]);
    }

    [Fact]
    public async Task The_sitemap_is_capped_at_the_maximum_number_of_entries_keeping_the_newest()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var category = KbCategory.Create(null, "general", "General", 1, host.Clock).Value;
        var articles = new List<KbArticle>();
        for (var i = 0; i <= KbLimits.MaxSitemapEntries; i++)
        {
            var article = Article(scenario, host, null, category, $"article-{i}", "t", "s", "b");
            Publish(host, article);
            articles.Add(article);
        }

        host.Clock.Advance(TimeSpan.FromHours(1));
        var newest = Article(scenario, host, null, category, "the-newest", "t", "s", "b");
        Publish(host, newest);
        articles.Add(newest);
        (await host.CommitAsync(sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            kb.AddCategory(category);
            articles.ForEach(kb.AddArticle);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();

        var sitemap = await host.ReadAsync(sp => new GetKbSitemapRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>()).HandleAsync("acme", Ct));

        sitemap.Value.Count.ShouldBe(KbLimits.MaxSitemapEntries);
        sitemap.Value[0].Slug.ShouldBe("the-newest");
    }

    [Fact]
    public async Task Search_results_are_ranked_title_above_summary_above_body_and_paged()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var category = KbCategory.Create(null, "general", "General", 1, host.Clock).Value;
        var inBody = Article(scenario, host, null, category, "in-body", "Unrelated guide", "A summary", "Steps to reset your password are below");
        var inSummary = Article(scenario, host, null, category, "in-summary", "Another guide", "How to reset things", "Body text");
        var inTitle = Article(scenario, host, null, category, "in-title", "Reset your password", "A summary", "Body text");
        Publish(host, inBody, inSummary, inTitle);
        (await host.CommitAsync(sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            kb.AddCategory(category);
            kb.AddArticle(inBody);
            kb.AddArticle(inSummary);
            kb.AddArticle(inTitle);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();

        var all = await host.ReadAsync(sp => Search(sp).HandleAsync("acme", "reset", null, 1, 25, Ct));
        var second = await host.ReadAsync(sp => Search(sp).HandleAsync("acme", "reset", null, 2, 2, Ct));

        all.Value.Items.Select(item => item.Slug).ShouldBe(["in-title", "in-summary", "in-body"]);
        second.Value.ShouldSatisfyAllConditions(page => page.TotalCount.ShouldBe(3), page => page.PageSize.ShouldBe(2), page => page.Items.ShouldHaveSingleItem().Slug.ShouldBe("in-body"));
    }

    [Fact]
    public async Task The_preview_and_the_published_page_render_the_same_markdown_to_the_same_html()
    {
        await using var host = new PersistenceTestHost(Database);
        var world = await SeedAsync(host);
        var renderer = new KbContentRenderer();
        const string Markdown = "# Steps\n\n| a | b |\n|---|---|\n| 1 | 2 |";

        var preview = await new RenderKbPreviewRequestHandler(renderer).HandleAsync(new KbPreviewRequest(Markdown), Ct);
        var page = await WithHandlersAsync(world, sp => new GetPublishedKbArticleRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>(), renderer)
            .HandleAsync("acme", "acme-cat", "acme-published", Ct));

        page.Value.Html.ShouldBe(preview.Value.Html);
    }

    private static Task<Result<PagedResponse<PublicKbArticleSummaryDto>>> CategoryArticles(World world, string product, string category, int page = 1, int pageSize = 25) =>
        WithHandlersAsync(world, sp => new ListPublicKbCategoryArticlesRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>())
            .HandleAsync(product, category, page, pageSize, Ct));

    [Fact]
    public async Task A_category_list_holds_only_published_articles_of_the_product_and_the_shared_space_in_a_category_the_product_can_see()
    {
        await using var host = new PersistenceTestHost(Database);
        var world = await SeedAsync(host);

        var acme = await CategoryArticles(world, "acme", "acme-cat");
        var shared = await CategoryArticles(world, "acme", "general");
        var orbitlyShared = await CategoryArticles(world, "orbitly", "general");

        // Left out of acme-cat: the draft, the archived article, Orbitly's article filed there and (not in this category anyway) Acme's article filed in an Orbitly category.
        acme.Value.Items.Select(item => item.Slug).ShouldBe(["acme-published"]);
        acme.Value.Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            item => item.Title.ShouldBe("Router reset guide"),
            item => item.Summary.ShouldBe("How to reset the router"),
            item => item.CategorySlug.ShouldBe("acme-cat"),
            item => item.CategoryName.ShouldBe("Acme things"),
            item => item.ProductKey.ShouldBe("acme"));
        acme.Value.TotalCount.ShouldBe(1);
        shared.Value.Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(item => item.Slug.ShouldBe("shared-published"), item => item.ProductKey.ShouldBeNull());
        orbitlyShared.Value.Items.Select(item => item.Slug).ShouldBe(["shared-published"]);
    }

    [Theory]
    [InlineData("acme", "orb-cat")]
    [InlineData("orbitly", "acme-cat")]
    [InlineData("acme", "empty-cat")]
    [InlineData("acme", "no-such-category")]
    [InlineData("acme", "search")]
    [InlineData("nobody", "general")]
    [InlineData("dormant", "general")]
    [InlineData("acme", "ACME-CAT")]
    [InlineData("acme", "acme-cat\0")]
    public async Task Another_products_category_an_empty_one_an_unknown_one_and_an_inactive_product_are_all_the_same_404(string product, string category)
    {
        await using var host = new PersistenceTestHost(Database);
        var world = await SeedAsync(host);
        var dormant = Product.Create("dormant", "Dormant", "DOR", null, host.Clock).Value;
        dormant.SetActive(false);
        (await host.CommitAsync(sp => { sp.GetRequiredService<IProductRepository>().Add(dormant); return Task.CompletedTask; })).IsSuccess.ShouldBeTrue();

        var result = await CategoryArticles(world, product, category);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe("kb-category-not-found");
    }

    [Fact]
    public async Task Archiving_the_last_published_article_of_a_category_makes_it_a_404_like_the_category_list_does()
    {
        await using var host = new PersistenceTestHost(Database);
        var world = await SeedAsync(host);
        (await CategoryArticles(world, "acme", "acme-cat")).IsSuccess.ShouldBeTrue();

        var published = world.Articles["acme-published"];
        (await host.CommitAsync(async sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            var loaded = (await kb.GetArticleAsync(published.Id, Ct))!;
            loaded.Archive(host.Clock).IsSuccess.ShouldBeTrue();
            kb.UpdateArticle(loaded);
        })).IsSuccess.ShouldBeTrue();

        (await CategoryArticles(world, "acme", "acme-cat")).Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-not-found");
    }

    [Fact]
    public async Task A_category_list_is_newest_update_first_paged_and_a_page_past_the_end_is_empty_with_the_total()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var category = KbCategory.Create(null, "general", "General", 1, host.Clock).Value;
        var articles = new List<KbArticle>();
        foreach (var slug in new[] { "a-oldest", "b-second", "c-third", "d-fourth", "e-newest" })
        {
            host.Clock.Advance(TimeSpan.FromMinutes(10));
            var article = Article(scenario, host, null, category, slug, slug, "s", "body");
            Publish(host, article);
            articles.Add(article);
        }

        (await host.CommitAsync(sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            kb.AddCategory(category);
            articles.ForEach(kb.AddArticle);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        Task<Result<PagedResponse<PublicKbArticleSummaryDto>>> Page(int page, int size) =>
            host.ReadAsync(sp => new ListPublicKbCategoryArticlesRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>()).HandleAsync("acme", "general", page, size, Ct));

        var first = await Page(1, 2);
        var second = await Page(2, 2);
        var last = await Page(3, 2);
        var past = await Page(4, 2);

        first.Value.Items.Select(item => item.Slug).ShouldBe(["e-newest", "d-fourth"]);
        second.Value.Items.Select(item => item.Slug).ShouldBe(["c-third", "b-second"]);
        last.Value.Items.Select(item => item.Slug).ShouldBe(["a-oldest"]);
        past.Value.Items.ShouldBeEmpty();
        new[] { first, second, last, past }.ShouldAllBe(page => page.Value.TotalCount == 5 && page.Value.PageSize == 2);
        past.Value.Page.ShouldBe(4);

        // The repository caps the page size itself, whatever the handler passed.
        var direct = await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().ListPublicCategoryArticlesAsync(scenario.Acme.Id, "general", 1, 1000, Ct));
        direct!.PageSize.ShouldBe(KbLimits.MaxPublicSearchPageSize);
        direct.Items.Count.ShouldBe(5);
    }

    [Fact]
    public async Task Articles_with_the_same_update_time_are_ordered_by_id_descending_so_a_page_boundary_never_repeats_or_drops_one()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var category = KbCategory.Create(null, "general", "General", 1, host.Clock).Value;
        var articles = new List<KbArticle>();
        foreach (var slug in new[] { "a", "b", "c", "d", "e" })
        {
            // The clock is not advanced, so every article has the same UpdatedAt and only the id can order them.
            var article = Article(scenario, host, null, category, slug, slug, "s", "body");
            Publish(host, article);
            articles.Add(article);
        }

        (await host.CommitAsync(sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            kb.AddCategory(category);
            articles.ForEach(kb.AddArticle);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        Task<Result<PagedResponse<PublicKbArticleSummaryDto>>> Page(int page) =>
            host.ReadAsync(sp => new ListPublicKbCategoryArticlesRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>()).HandleAsync("acme", "general", page, 2, Ct));

        var pages = new[] { await Page(1), await Page(2), await Page(3) };

        var served = pages.SelectMany(page => page.Value.Items).ToList();
        served.Select(item => item.UpdatedAt).Distinct().Count().ShouldBe(1, "the scenario really is a tie");
        served.Select(item => item.Slug).ShouldBe([.. articles.OrderByDescending(article => article.Id).Select(article => article.Slug)]);
        pages.Select(page => page.Value.Items.Count).ShouldBe([2, 2, 1]);
    }

    [Fact]
    public async Task The_public_product_list_is_the_active_products_key_and_display_name_only()
    {
        await using var host = new PersistenceTestHost(Database);
        await TicketScenario.CreateAsync(host);
        var dormant = Product.Create("dormant", "Dormant", "DOR", null, host.Clock).Value;
        dormant.SetActive(false);
        (await host.CommitAsync(sp => { sp.GetRequiredService<IProductRepository>().Add(dormant); return Task.CompletedTask; })).IsSuccess.ShouldBeTrue();

        var result = await host.ReadAsync(sp => new ListPublicProductsRequestHandler(sp.GetRequiredService<IProductRepository>(), new NoLogoUrls()).HandleAsync(Ct));

        result.Value.Select(product => product.Key).ShouldBe(["acme", "orbitly"]);
        result.Value.Select(product => product.DisplayName).ShouldBe(["Acme", "Orbitly"]);
    }

    [Fact]
    public void The_public_article_dto_has_no_author_and_no_id()
    {
        var names = typeof(PublishedKbArticleDto).GetProperties().Select(property => property.Name).ToArray();

        names.ShouldNotContain("AuthorAgentId");
        names.ShouldNotContain("Id");
        names.ShouldNotContain("Version");
        names.ShouldNotContain("BodyMarkdown");
        typeof(PublicKbSearchResultDto).GetProperties().Select(property => property.Name).ShouldNotContain("Id");
    }

    private sealed class NoLogoUrls : IProductLogoUrls
    {
        public string? UrlFor(string fileName) => null;
    }
}
