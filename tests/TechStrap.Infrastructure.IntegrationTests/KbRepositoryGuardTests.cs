using Microsoft.Extensions.DependencyInjection;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Knowledge;
using TechStrap.Domain.Rules;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>Concurrency across scopes (D-026), customer visibility, slug spaces, search limits and the first publication date.</summary>
public sealed class KbRepositoryGuardTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static KbArticle Article(TicketScenario scenario, string slug, string title, Guid? productId, string body = "body") =>
        KbArticle.Create(productId, KbTestData.SharedCategoryId, slug, title, "summary", body, scenario.Agent.Id, scenario.Host.Clock).Value;

    private static async Task<Result> AddAsync(TicketScenario scenario, params KbArticle[] articles)
    {
        await KbTestData.EnsureSharedCategoryAsync(scenario.Host);
        return await scenario.Host.CommitAsync(sp =>
        {
            foreach (var article in articles)
            {
                sp.GetRequiredService<IKbRepository>().AddArticle(article);
            }

            return Task.CompletedTask;
        });
    }

    private static Task<Result> UpdateAsync(PersistenceTestHost host, Guid id, Action<KbArticle> change) =>
        host.CommitAsync(async sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            var loaded = (await kb.GetArticleAsync(id, Ct))!;
            change(loaded);
            kb.UpdateArticle(loaded);
        });

    [Fact]
    public async Task A_stale_article_kept_from_one_scope_and_updated_in_a_third_scope_is_a_concurrency_conflict()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var article = Article(scenario, "faq", "FAQ", scenario.Acme.Id);
        await AddAsync(scenario, article);
        var stale = (await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().GetArticleAsync(article.Id, Ct)))!;
        (await UpdateAsync(host, article.Id, a => a.Update(null, "Winner", null, "body", host.Clock))).IsSuccess.ShouldBeTrue();
        stale.Update(null, "Loser", null, "body", host.Clock);

        var result = await host.CommitAsync(async sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            await kb.GetArticleAsync(article.Id, Ct);
            kb.UpdateArticle(stale);
        });

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
        (await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().GetArticleAsync(article.Id, Ct)))!.Title.ShouldBe("Winner");
    }

    [Fact]
    public async Task An_article_kept_from_one_scope_and_updated_in_another_succeeds_when_nobody_changed_it_meanwhile()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var article = Article(scenario, "faq", "FAQ", scenario.Acme.Id);
        await AddAsync(scenario, article);
        var kept = (await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().GetArticleAsync(article.Id, Ct)))!;
        kept.Update(null, "Edited", null, "body", host.Clock);

        var result = await host.CommitAsync(async sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            await kb.GetArticleAsync(article.Id, Ct);
            kb.UpdateArticle(kept);
        });

        result.IsSuccess.ShouldBeTrue();
        (await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().GetArticleAsync(article.Id, Ct)))!.Title.ShouldBe("Edited");
    }

    [Fact]
    public async Task Customer_methods_never_return_a_draft_an_archived_or_a_shared_draft_article_and_need_no_status()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var published = Article(scenario, "pub", "Router reset", scenario.Acme.Id);
        published.Publish(host.Clock);
        var draft = Article(scenario, "draft", "Router reset draft", scenario.Acme.Id);
        var archived = Article(scenario, "archived", "Router reset archived", scenario.Acme.Id);
        archived.Archive(host.Clock);
        var sharedDraft = Article(scenario, "shared-draft", "Router reset shared draft", null);
        await AddAsync(scenario, published, draft, archived, sharedDraft);

        var list = await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().ListPublishedArticlesAsync(new PublishedKbArticleQuery(ProductId: scenario.Acme.Id), Ct));
        var search = await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().SearchPublishedAsync(new PublishedKbSearchQuery("router", ProductId: scenario.Acme.Id), Ct));
        var byId = await host.ReadAsync(async sp =>
        {
            var repository = sp.GetRequiredService<IKbRepository>();
            return new[]
            {
                await repository.GetPublishedArticleAsync(draft.Id, Ct),
                await repository.GetPublishedArticleAsync(archived.Id, Ct),
                await repository.GetPublishedArticleAsync(sharedDraft.Id, Ct),
            };
        });
        var bySlug = await host.ReadAsync(async sp =>
        {
            var repository = sp.GetRequiredService<IKbRepository>();
            return new[]
            {
                await repository.GetPublishedArticleBySlugAsync(scenario.Acme.Id, "draft", Ct),
                await repository.GetPublishedArticleBySlugAsync(scenario.Acme.Id, "archived", Ct),
                await repository.GetPublishedArticleBySlugAsync(null, "shared-draft", Ct),
            };
        });
        var visibleBySlug = await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().GetPublishedArticleBySlugAsync(scenario.Acme.Id, "pub", Ct));
        var visibleById = await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().GetPublishedArticleAsync(published.Id, Ct));

        list.Items.ShouldHaveSingleItem().Slug.ShouldBe("pub");
        search.Items.ShouldHaveSingleItem().Slug.ShouldBe("pub");
        byId.ShouldAllBe(a => a == null);
        bySlug.ShouldAllBe(a => a == null);
        visibleBySlug!.Slug.ShouldBe("pub");
        visibleById!.Slug.ShouldBe("pub");
    }

    [Fact]
    public async Task Agent_methods_return_articles_of_any_status()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var draft = Article(scenario, "draft", "Router reset draft", scenario.Acme.Id);
        await AddAsync(scenario, draft);

        var agentBySlug = await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().GetArticleBySlugAsync(scenario.Acme.Id, "draft", Ct));
        var agentById = await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().GetArticleAsync(draft.Id, Ct));
        var agentList = await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().ListArticlesAsync(new KbArticleQuery(ProductId: scenario.Acme.Id), Ct));
        var agentSearch = await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().SearchAsync(new KbSearchQuery("router", ProductId: scenario.Acme.Id), Ct));

        agentBySlug.ShouldNotBeNull();
        agentById.ShouldNotBeNull();
        agentList.Items.ShouldHaveSingleItem();
        agentSearch.Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task An_article_added_and_updated_in_one_scope_is_inserted_once_with_the_update_applied()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var article = Article(scenario, "faq", "FAQ", scenario.Acme.Id);

        var result = await host.CommitAsync(sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            kb.AddArticle(article);
            article.Update(null, "Edited", null, "body", host.Clock);
            kb.UpdateArticle(article);
            return Task.CompletedTask;
        });

        result.IsSuccess.ShouldBeTrue();
        (await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().GetArticleAsync(article.Id, Ct)))!.Title.ShouldBe("Edited");
    }

    [Fact]
    public async Task A_duplicate_product_slug_and_a_duplicate_shared_slug_are_duplicate_conflicts_not_exceptions()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        (await AddAsync(scenario, Article(scenario, "faq", "One", scenario.Acme.Id), Article(scenario, "faq", "Shared one", null))).IsSuccess.ShouldBeTrue();

        var sameProduct = await AddAsync(scenario, Article(scenario, "faq", "Two", scenario.Acme.Id));
        var bothShared = await AddAsync(scenario, Article(scenario, "faq", "Shared two", null));
        var otherProduct = await AddAsync(scenario, Article(scenario, "faq", "Other", scenario.Orbitly.Id));

        sameProduct.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.Duplicate);
        bothShared.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.Duplicate);
        otherProduct.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_duplicate_category_slug_in_a_product_or_in_the_shared_space_is_a_duplicate_conflict()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        Task<Result> AddCategory(Guid? productId, string name) => host.CommitAsync(sp =>
        {
            sp.GetRequiredService<IKbRepository>().AddCategory(KbCategory.Create(productId, "general", name, 1, host.Clock).Value);
            return Task.CompletedTask;
        });
        (await AddCategory(scenario.Acme.Id, "One")).IsSuccess.ShouldBeTrue();
        (await AddCategory(null, "Shared one")).IsSuccess.ShouldBeTrue();

        var sameProduct = await AddCategory(scenario.Acme.Id, "Two");
        var bothShared = await AddCategory(null, "Shared two");

        sameProduct.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.Duplicate);
        bothShared.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.Duplicate);
    }

    [Fact]
    public async Task The_first_publication_date_survives_archiving_and_publishing_again()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var article = Article(scenario, "faq", "FAQ", scenario.Acme.Id);
        await AddAsync(scenario, article);
        host.Clock.Advance(TimeSpan.FromHours(1));
        (await UpdateAsync(host, article.Id, a => a.Publish(host.Clock))).IsSuccess.ShouldBeTrue();
        var first = (await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().GetArticleAsync(article.Id, Ct)))!.PublishedAt;
        host.Clock.Advance(TimeSpan.FromHours(1));
        (await UpdateAsync(host, article.Id, a => a.Archive(host.Clock))).IsSuccess.ShouldBeTrue();
        host.Clock.Advance(TimeSpan.FromHours(1));
        (await UpdateAsync(host, article.Id, a => a.Publish(host.Clock))).IsSuccess.ShouldBeTrue();

        var again = (await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().GetArticleAsync(article.Id, Ct)))!;

        first.ShouldNotBeNull();
        again.Status.ShouldBe(KbArticleStatus.Published);
        again.PublishedAt.ShouldBe(first);
        again.UpdatedAt.ShouldBeGreaterThan(first.Value);
    }

    [Fact]
    public async Task Search_text_with_quotes_and_sql_is_only_ever_a_parameter_and_overlong_text_is_truncated()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var article = Article(scenario, "faq", "Printer guide", scenario.Acme.Id);
        article.Publish(host.Clock);
        await AddAsync(scenario, article);
        var kb = (Func<string, Task<PagedResult<KbArticle>>>)(text => host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().SearchAsync(new KbSearchQuery(text), Ct)));

        var injection = await kb("'; DROP TABLE kb_articles; --");
        var overlong = await kb(string.Concat(Enumerable.Repeat("printer ", DomainLimits.SearchTextMaxLength)) + "zzzzqqq");
        var still = await kb("printer");

        injection.Items.ShouldBeEmpty();
        overlong.Items.ShouldHaveSingleItem().Slug.ShouldBe("faq");
        still.Items.ShouldHaveSingleItem().Slug.ShouldBe("faq");
    }

    [Fact]
    public async Task Search_text_cut_through_an_emoji_does_not_throw_and_still_matches()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var article = Article(scenario, "faq", "Printer guide", scenario.Acme.Id);
        article.Publish(host.Clock);
        await AddAsync(scenario, article);
        var text = "printer" + new string(' ', DomainLimits.SearchTextMaxLength - 8) + "\U0001F600";

        var result = await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().SearchAsync(new KbSearchQuery(text), Ct));

        result.Items.ShouldHaveSingleItem().Slug.ShouldBe("faq");
    }

    [Fact]
    public async Task Search_pages_are_clamped_and_equal_scores_page_without_gaps_or_repeats()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var articles = Enumerable.Range(0, 5).Select(i =>
        {
            var article = Article(scenario, $"tie-{i}", "Scanner guide", scenario.Acme.Id);
            article.Publish(host.Clock);
            return article;
        }).ToArray();
        await AddAsync(scenario, articles);
        var kb = (Func<int, int, Task<PagedResult<KbArticle>>>)((page, size) => host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().SearchAsync(new KbSearchQuery("scanner", Page: page, PageSize: size), Ct)));

        var slugs = new List<string>();
        for (var page = 1; page <= 3; page++)
        {
            slugs.AddRange((await kb(page, 2)).Items.Select(a => a.Slug));
        }

        slugs.Count.ShouldBe(5);
        slugs.Distinct().Count().ShouldBe(5);
        (await kb(1, int.MaxValue)).PageSize.ShouldBe(Paging.MaxPageSize);
        (await kb(int.MaxValue, 10)).Items.ShouldBeEmpty();
    }
}
