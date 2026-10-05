using Microsoft.Extensions.DependencyInjection;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;
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
    public async Task A_summary_snippet_is_plain_text_and_every_html_character_in_it_is_encoded()
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
        snippet.ShouldNotContain("<");
        snippet.ShouldNotContain(">");
        snippet.ShouldContain("&amp;");
        snippet.ShouldContain("&lt;img");
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
}
