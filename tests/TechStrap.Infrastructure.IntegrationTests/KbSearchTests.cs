using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>Knowledge-base full-text search (D-011, D-027): title above summary above body.</summary>
public sealed class KbSearchTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static KbArticle Article(TicketScenario scenario, string slug, string title, string summary, string body, Guid? productId, bool publish = true)
    {
        var article = KbArticle.Create(productId, KbTestData.SharedCategoryId, slug, title, summary, body, scenario.Agent.Id, scenario.Host.Clock).Value;
        if (publish)
        {
            article.Publish(scenario.Host.Clock);
        }

        return article;
    }

    private static async Task SaveAsync(TicketScenario scenario, params KbArticle[] articles)
    {
        await KbTestData.EnsureSharedCategoryAsync(scenario.Host);
        var result = await scenario.Host.CommitAsync(sp =>
        {
            foreach (var article in articles)
            {
                sp.GetRequiredService<IKbRepository>().AddArticle(article);
            }

            return Task.CompletedTask;
        });
        result.IsSuccess.ShouldBeTrue();
    }

    private static async Task<List<string>> SearchAsync(PersistenceTestHost host, KbSearchQuery query)
    {
        var page = await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().SearchAsync(query, Ct));
        return [.. page.Items.Select(a => a.Slug)];
    }

    [Fact]
    public async Task A_title_match_ranks_above_a_summary_match_above_a_body_match()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await SaveAsync(
            scenario,
            Article(scenario, "in-body", "Unrelated guide", "A summary", "Steps to reset your password are below", scenario.Acme.Id),
            Article(scenario, "in-summary", "Another guide", "How to reset things", "Body text", scenario.Acme.Id),
            Article(scenario, "in-title", "Reset your password", "A summary", "Body text", scenario.Acme.Id));

        (await SearchAsync(host, new KbSearchQuery("reset"))).ShouldBe(["in-title", "in-summary", "in-body"]);
    }

    [Fact]
    public async Task Search_finds_by_body_and_stems_words()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await SaveAsync(scenario, Article(scenario, "faq", "FAQ", "Questions", "Resetting passwords is easy", scenario.Acme.Id));

        (await SearchAsync(host, new KbSearchQuery("reset password"))).ShouldBe(["faq"]);
        (await SearchAsync(host, new KbSearchQuery("kubernetes"))).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_status_filter_keeps_drafts_and_archived_articles_out_of_public_results()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var archived = Article(scenario, "archived", "Reset archived", "s", "b", scenario.Acme.Id);
        archived.Archive(host.Clock);
        await SaveAsync(
            scenario,
            Article(scenario, "published", "Reset published", "s", "b", scenario.Acme.Id),
            Article(scenario, "draft", "Reset draft", "s", "b", scenario.Acme.Id, publish: false),
            archived);

        (await SearchAsync(host, new KbSearchQuery("reset", Status: KbArticleStatus.Published))).ShouldBe(["published"]);
        (await SearchAsync(host, new KbSearchQuery("reset"))).Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_product_search_includes_shared_articles_unless_told_not_to_and_never_another_products_articles()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await SaveAsync(
            scenario,
            Article(scenario, "acme-guide", "Billing guide acme", "s", "b", scenario.Acme.Id),
            Article(scenario, "orbitly-guide", "Billing guide orbitly", "s", "b", scenario.Orbitly.Id),
            Article(scenario, "shared-guide", "Billing guide shared", "s", "b", null));

        (await SearchAsync(host, new KbSearchQuery("billing", ProductId: scenario.Acme.Id))).Order().ShouldBe(["acme-guide", "shared-guide"]);
        (await SearchAsync(host, new KbSearchQuery("billing", ProductId: scenario.Acme.Id, IncludeShared: false))).ShouldBe(["acme-guide"]);
    }

    [Fact]
    public async Task Blank_search_text_returns_an_empty_page_and_paging_reports_the_total()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await SaveAsync(
            scenario,
            Article(scenario, "one", "Printer one", "s", "b", scenario.Acme.Id),
            Article(scenario, "two", "Printer two", "s", "b", scenario.Acme.Id),
            Article(scenario, "three", "Printer three", "s", "b", scenario.Acme.Id));

        var blank = await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().SearchAsync(new KbSearchQuery("  "), Ct));
        var second = await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().SearchAsync(new KbSearchQuery("printer", Page: 2, PageSize: 2), Ct));

        blank.Items.ShouldBeEmpty();
        blank.TotalCount.ShouldBe(0);
        second.TotalCount.ShouldBe(3);
        second.Items.Count.ShouldBe(1);
    }
}
