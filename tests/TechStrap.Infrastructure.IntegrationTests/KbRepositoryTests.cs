using Microsoft.Extensions.DependencyInjection;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Knowledge;
using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class KbRepositoryTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static KbArticle Article(TicketScenario scenario, string slug, string title, Guid? productId, Guid? categoryId = null) =>
        KbArticle.Create(productId, categoryId ?? KbTestData.SharedCategoryId, slug, title, "summary", "# body", scenario.Agent.Id, scenario.Host.Clock).Value;

    private static async Task<Result> AddAsync(TicketScenario scenario, params KbArticle[] articles)
    {
        await KbTestData.EnsureSharedCategoryAsync(scenario.Host);
        return await scenario.Host.CommitAsync(sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            foreach (var article in articles)
            {
                kb.AddArticle(article);
            }

            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task An_article_round_trips_and_is_found_by_id_and_by_slug_in_its_own_product_or_the_shared_space()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var productArticle = Article(scenario, "reset-password", "Reset a password", scenario.Acme.Id);
        var sharedArticle = Article(scenario, "reset-password", "Reset (shared)", null);
        (await AddAsync(scenario, productArticle, sharedArticle)).IsSuccess.ShouldBeTrue();

        var kb = (Func<IServiceProvider, IKbRepository>)(sp => sp.GetRequiredService<IKbRepository>());
        var byId = await host.ReadAsync(sp => kb(sp).GetArticleAsync(productArticle.Id, Ct));
        var inProduct = await host.ReadAsync(sp => kb(sp).GetArticleBySlugAsync(scenario.Acme.Id, "reset-password", Ct));
        var shared = await host.ReadAsync(sp => kb(sp).GetArticleBySlugAsync(null, "reset-password", Ct));

        byId!.Title.ShouldBe("Reset a password");
        byId.Status.ShouldBe(KbArticleStatus.Draft);
        byId.BodyMarkdown.ShouldBe("# body");
        inProduct!.Id.ShouldBe(productArticle.Id);
        shared!.Id.ShouldBe(sharedArticle.Id);
        (await host.ReadAsync(sp => kb(sp).GetArticleBySlugAsync(scenario.Orbitly.Id, "reset-password", Ct))).ShouldBeNull();
    }

    [Fact]
    public async Task Editing_publishing_and_archiving_are_persisted()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var article = Article(scenario, "faq", "FAQ", scenario.Acme.Id);
        await AddAsync(scenario, article);
        host.Clock.Advance(TimeSpan.FromHours(1));

        await host.CommitAsync(async sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            var loaded = (await kb.GetArticleAsync(article.Id, Ct))!;
            loaded.Update(KbTestData.SharedCategoryId, "FAQ v2", null, "new body", host.Clock);
            loaded.Publish(host.Clock);
            kb.UpdateArticle(loaded);
        });
        var published = (await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().GetArticleAsync(article.Id, Ct)))!;
        await host.CommitAsync(async sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            var loaded = (await kb.GetArticleAsync(article.Id, Ct))!;
            loaded.Archive(host.Clock);
            kb.UpdateArticle(loaded);
        });
        var archived = (await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().GetArticleAsync(article.Id, Ct)))!;

        published.Title.ShouldBe("FAQ v2");
        published.Summary.ShouldBeNull();
        published.Status.ShouldBe(KbArticleStatus.Published);
        published.PublishedAt.ShouldNotBeNull();
        archived.Status.ShouldBe(KbArticleStatus.Archived);
    }

    [Fact]
    public async Task Two_stale_article_edits_give_one_success_and_one_conflict()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var article = Article(scenario, "faq", "FAQ", scenario.Acme.Id);
        await AddAsync(scenario, article);
        await using var first = host.CreateScope();
        await using var second = host.CreateScope();
        var firstKb = first.ServiceProvider.GetRequiredService<IKbRepository>();
        var secondKb = second.ServiceProvider.GetRequiredService<IKbRepository>();
        var byFirst = (await firstKb.GetArticleAsync(article.Id, Ct))!;
        var bySecond = (await secondKb.GetArticleAsync(article.Id, Ct))!;
        byFirst.Update(null, "First edit", null, "body", host.Clock);
        bySecond.Update(null, "Second edit", null, "body", host.Clock);

        await using var firstWork = await first.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        firstKb.UpdateArticle(byFirst);
        var firstResult = await firstWork.CommitAsync(Ct);
        await using var secondWork = await second.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        secondKb.UpdateArticle(bySecond);
        var secondResult = await secondWork.CommitAsync(Ct);

        firstResult.IsSuccess.ShouldBeTrue();
        secondResult.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
        (await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().GetArticleAsync(article.Id, Ct)))!.Title.ShouldBe("First edit");
    }

    [Fact]
    public async Task Listing_filters_by_product_with_shared_status_and_category_and_pages_newest_first()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var category = KbCategory.Create(scenario.Acme.Id, "account", "Account", 1, host.Clock).Value;
        await host.CommitAsync(sp => { sp.GetRequiredService<IKbRepository>().AddCategory(category); return Task.CompletedTask; });
        var oldest = Article(scenario, "a-oldest", "Oldest", scenario.Acme.Id, category.Id);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var shared = Article(scenario, "b-shared", "Shared", null);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var other = Article(scenario, "c-other", "Other product", scenario.Orbitly.Id);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var newest = Article(scenario, "d-newest", "Newest", scenario.Acme.Id);
        newest.Publish(host.Clock);
        await AddAsync(scenario, oldest, shared, other, newest);

        var kb = (Func<KbArticleQuery, Task<PagedResult<KbArticle>>>)(query => host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().ListArticlesAsync(query, Ct)));

        (await kb(new KbArticleQuery())).Items.Select(a => a.Slug).ShouldBe(["d-newest", "c-other", "b-shared", "a-oldest"]);
        (await kb(new KbArticleQuery(ProductId: scenario.Acme.Id))).Items.Select(a => a.Slug).ShouldBe(["d-newest", "b-shared", "a-oldest"]);
        (await kb(new KbArticleQuery(ProductId: scenario.Acme.Id, IncludeShared: false))).Items.Select(a => a.Slug).ShouldBe(["d-newest", "a-oldest"]);
        (await kb(new KbArticleQuery(Status: KbArticleStatus.Published))).Items.ShouldHaveSingleItem().Slug.ShouldBe("d-newest");
        (await kb(new KbArticleQuery(CategoryId: category.Id))).Items.ShouldHaveSingleItem().Slug.ShouldBe("a-oldest");
        var paged = await kb(new KbArticleQuery(Page: 2, PageSize: 3));
        paged.TotalCount.ShouldBe(4);
        paged.Items.ShouldHaveSingleItem().Slug.ShouldBe("a-oldest");
    }

    [Fact]
    public async Task Categories_are_ordered_by_sort_order_and_can_be_updated()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var late = KbCategory.Create(scenario.Acme.Id, "late", "Late", 20, host.Clock).Value;
        var early = KbCategory.Create(scenario.Acme.Id, "early", "Early", 10, host.Clock).Value;
        var shared = KbCategory.Create(null, "general", "General", 5, host.Clock).Value;
        await host.CommitAsync(sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            kb.AddCategory(late);
            kb.AddCategory(early);
            kb.AddCategory(shared);
            return Task.CompletedTask;
        });
        await host.CommitAsync(async sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            var loaded = (await kb.GetCategoryAsync(late.Id, Ct))!;
            loaded.Update("Later", null, 1);
            kb.UpdateCategory(loaded);
        });

        var withShared = await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().ListCategoriesAsync(scenario.Acme.Id, true, Ct));
        var productOnly = await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().ListCategoriesAsync(scenario.Acme.Id, false, Ct));
        var everything = await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().ListCategoriesAsync(null, true, Ct));

        withShared.Select(c => c.Slug).ShouldBe(["late", "general", "early"]);
        productOnly.Select(c => c.Slug).ShouldBe(["late", "early"]);
        everything.Count.ShouldBe(3);
        withShared[0].Name.ShouldBe("Later");
    }

    [Fact]
    public async Task A_category_holding_articles_cannot_be_removed_but_an_empty_one_can()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var used = KbCategory.Create(scenario.Acme.Id, "used", "Used", 1, host.Clock).Value;
        var empty = KbCategory.Create(scenario.Acme.Id, "empty", "Empty", 2, host.Clock).Value;
        await host.CommitAsync(sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            kb.AddCategory(used);
            kb.AddCategory(empty);
            return Task.CompletedTask;
        });
        await AddAsync(scenario, Article(scenario, "faq", "FAQ", scenario.Acme.Id, used.Id));

        var blocked = await host.CommitAsync(async sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            kb.RemoveCategory((await kb.GetCategoryAsync(used.Id, Ct))!);
        });
        var removed = await host.CommitAsync(async sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            kb.RemoveCategory((await kb.GetCategoryAsync(empty.Id, Ct))!);
        });

        blocked.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ReferenceViolation);
        removed.IsSuccess.ShouldBeTrue();
        (await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().ListCategoriesAsync(null, true, Ct))).Select(c => c.Slug).ShouldBe([KbTestData.SharedCategorySlug, "used"]);
    }

    [Fact]
    public async Task Articles_linked_from_an_agent_reply_are_listed_for_the_ticket()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var zebra = Article(scenario, "zebra", "Zebra guide", scenario.Acme.Id);
        var apple = Article(scenario, "apple", "Apple guide", scenario.Acme.Id);
        await AddAsync(scenario, zebra, apple);
        var ticket = await scenario.CreateTicketAsync();
        Guid replyId = Guid.Empty;

        var result = await host.CommitAsync(async sp =>
        {
            var tickets = sp.GetRequiredService<ITicketRepository>();
            var loaded = (await tickets.GetByIdAsync(ticket.Id, Ct))!;
            replyId = loaded.AddAgentReply(scenario.Agent.Id, "<p>See these</p>", host.Clock).Value.Id;
            tickets.Update(loaded);
            var kb = sp.GetRequiredService<IKbRepository>();
            kb.AddTicketArticle(new TicketArticle(ticket.Id, replyId, zebra.Id));
            kb.AddTicketArticle(new TicketArticle(ticket.Id, replyId, apple.Id));
        });

        result.IsSuccess.ShouldBeTrue();
        var linked = await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().ListLinkedArticlesAsync(ticket.Id, Ct));
        linked.Select(a => a.Slug).ShouldBe(["apple", "zebra"]);
        (await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().ListLinkedArticlesAsync(Guid.NewGuid(), Ct))).ShouldBeEmpty();
    }
}
