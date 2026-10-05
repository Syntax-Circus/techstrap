using Microsoft.Extensions.DependencyInjection;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>D-044: slugs are unique across scopes, and a category carries a description and a concurrency version.</summary>
public sealed class KbScopeRuleTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static KbArticle Article(TicketScenario scenario, string slug, Guid? productId, KbArticleStatus status = KbArticleStatus.Draft)
    {
        var article = KbArticle.Create(productId, KbTestData.SharedCategoryId, slug, "Title", null, "body", scenario.Agent.Id, scenario.Host.Clock).Value;
        if (status == KbArticleStatus.Archived)
        {
            article.Archive(scenario.Host.Clock);
        }

        return article;
    }

    private static async Task AddAsync(TicketScenario scenario, params KbArticle[] articles)
    {
        await KbTestData.EnsureSharedCategoryAsync(scenario.Host);
        (await scenario.Host.CommitAsync(sp =>
        {
            foreach (var article in articles)
            {
                sp.GetRequiredService<IKbRepository>().AddArticle(article);
            }

            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
    }

    private static Task<bool> TakenAsync(TicketScenario scenario, Guid? productId, string slug) =>
        scenario.Host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().ArticleSlugTakenAsync(productId, slug, Ct));

    [Fact]
    public async Task A_product_article_cannot_reuse_a_shared_slug_but_another_products_slug_is_free()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await AddAsync(scenario, Article(scenario, "welcome", null), Article(scenario, "acme-only", scenario.Acme.Id));

        (await TakenAsync(scenario, scenario.Orbitly.Id, "welcome")).ShouldBeTrue();
        (await TakenAsync(scenario, scenario.Acme.Id, "welcome")).ShouldBeTrue();
        (await TakenAsync(scenario, scenario.Acme.Id, "acme-only")).ShouldBeTrue();
        (await TakenAsync(scenario, scenario.Orbitly.Id, "acme-only")).ShouldBeFalse();
        (await TakenAsync(scenario, scenario.Orbitly.Id, "unused")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_shared_article_cannot_reuse_any_products_slug()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await AddAsync(scenario, Article(scenario, "orbitly-guide", scenario.Orbitly.Id));

        (await TakenAsync(scenario, null, "orbitly-guide")).ShouldBeTrue();
        (await TakenAsync(scenario, null, "unused")).ShouldBeFalse();
    }

    [Fact]
    public async Task An_archived_article_still_holds_its_slug()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await AddAsync(scenario, Article(scenario, "old", scenario.Acme.Id, KbArticleStatus.Archived));

        (await TakenAsync(scenario, scenario.Acme.Id, "old")).ShouldBeTrue();
        (await TakenAsync(scenario, null, "old")).ShouldBeTrue();
    }

    [Fact]
    public async Task Category_slugs_follow_the_same_cross_scope_rule()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        (await host.CommitAsync(sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            kb.AddCategory(KbCategory.Create(null, "shared-cat", "Shared", 1, host.Clock).Value);
            kb.AddCategory(KbCategory.Create(scenario.Acme.Id, "acme-cat", "Acme", 2, host.Clock).Value);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        Task<bool> Taken(Guid? productId, string slug) =>
            host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().CategorySlugTakenAsync(productId, slug, Ct));

        (await Taken(scenario.Orbitly.Id, "shared-cat")).ShouldBeTrue();
        (await Taken(null, "acme-cat")).ShouldBeTrue();
        (await Taken(scenario.Acme.Id, "acme-cat")).ShouldBeTrue();
        (await Taken(scenario.Orbitly.Id, "acme-cat")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_category_description_round_trips_and_two_stale_category_edits_give_one_conflict()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var category = KbCategory.Create(scenario.Acme.Id, "faq", "FAQ", 1, host.Clock, "Common questions").Value;
        (await host.CommitAsync(sp => { sp.GetRequiredService<IKbRepository>().AddCategory(category); return Task.CompletedTask; })).IsSuccess.ShouldBeTrue();
        await using var first = host.CreateScope();
        await using var second = host.CreateScope();
        var firstKb = first.ServiceProvider.GetRequiredService<IKbRepository>();
        var secondKb = second.ServiceProvider.GetRequiredService<IKbRepository>();
        var byFirst = (await firstKb.GetCategoryAsync(category.Id, Ct))!;
        var bySecond = (await secondKb.GetCategoryAsync(category.Id, Ct))!;
        byFirst.Description.ShouldBe("Common questions");
        byFirst.Version.ShouldBeGreaterThan(0u);
        byFirst.Update("First", null, 1);
        bySecond.Update("Second", null, 1);

        await using var firstWork = await first.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        firstKb.UpdateCategory(byFirst);
        var firstResult = await firstWork.CommitAsync(Ct);
        await using var secondWork = await second.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        secondKb.UpdateCategory(bySecond);
        var secondResult = await secondWork.CommitAsync(Ct);

        firstResult.IsSuccess.ShouldBeTrue();
        secondResult.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
        var stored = (await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().GetCategoryAsync(category.Id, Ct)))!;
        stored.Name.ShouldBe("First");
        stored.Description.ShouldBeNull();
    }
}
