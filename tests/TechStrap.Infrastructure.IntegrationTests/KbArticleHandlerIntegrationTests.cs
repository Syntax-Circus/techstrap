using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Agents;
using TechStrap.Application.Knowledge;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Knowledge;
using TechStrap.Infrastructure.Content;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>The real create and update handlers against real Postgres: the cross-scope slug rule, version conflicts and Archived to Draft (Review Focus 4).</summary>
public sealed class KbArticleHandlerIntegrationTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed class StubAgentClaims : ICurrentAgentClaims
    {
        public AgentClaims? Current { get; } = new("oidc|sam", "Sam W.", "sam@example.com", AgentRole.Agent);
    }

    private PersistenceTestHost NewHost() =>
        new(Database, configure: services =>
        {
            services.AddSingleton<ICurrentAgentClaims, StubAgentClaims>();
            services.AddTechStrapContent();
            services.AddScoped<ICreateKbArticleRequestHandler, CreateKbArticleRequestHandler>();
            services.AddScoped<IUpdateKbArticleRequestHandler, UpdateKbArticleRequestHandler>();
            services.AddScoped<IGetKbArticleRequestHandler, GetKbArticleRequestHandler>();
        });

    private static async Task<T> InScopeAsync<T>(PersistenceTestHost host, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = host.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private static Task<SyntaxCircus.Common.Result<KbArticleDto>> CreateAsync(PersistenceTestHost host, CreateKbArticleRequest request) =>
        InScopeAsync(host, sp => sp.GetRequiredService<ICreateKbArticleRequestHandler>().HandleAsync(request, Ct));

    private static Task<SyntaxCircus.Common.Result<KbArticleDto>> UpdateAsync(PersistenceTestHost host, Guid id, UpdateKbArticleRequest request) =>
        InScopeAsync(host, sp => sp.GetRequiredService<IUpdateKbArticleRequestHandler>().HandleAsync(id, request, Ct));

    [Fact]
    public async Task A_slug_is_refused_across_scopes_in_both_directions_but_free_for_another_product()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        await KbTestData.EnsureSharedCategoryAsync(host);
        var category = KbTestData.SharedCategoryId;

        (await CreateAsync(host, new CreateKbArticleRequest(null, category, "welcome", "Welcome", null, "Hello"))).IsSuccess.ShouldBeTrue();
        (await CreateAsync(host, new CreateKbArticleRequest(scenario.Acme.Id, category, "acme-guide", "Acme guide", null, "Hello"))).IsSuccess.ShouldBeTrue();

        var productReusesShared = await CreateAsync(host, new CreateKbArticleRequest(scenario.Orbitly.Id, category, "welcome", "Welcome again", null, "Hello"));
        var sharedReusesProduct = await CreateAsync(host, new CreateKbArticleRequest(null, category, "acme-guide", "Acme guide shared", null, "Hello"));
        var otherProductFree = await CreateAsync(host, new CreateKbArticleRequest(scenario.Orbitly.Id, category, "acme-guide", "Orbitly guide", null, "Hello"));

        productReusesShared.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-slug-taken");
        sharedReusesProduct.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-slug-taken");
        otherProductFree.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Two_edits_from_the_same_version_give_one_success_and_one_conflict_and_the_second_text_is_not_lost_silently()
    {
        await using var host = NewHost();
        await TicketScenario.CreateAsync(host);
        var created = (await CreateAsync(host, new CreateKbArticleRequest(null, null, "faq", "FAQ", null, "Body"))).Value;

        var first = await UpdateAsync(host, created.Id, new UpdateKbArticleRequest(null, "First edit", null, "Body 1", created.Version));
        var second = await UpdateAsync(host, created.Id, new UpdateKbArticleRequest(null, "Second edit", null, "Body 2", created.Version));

        first.IsSuccess.ShouldBeTrue();
        first.Value.Version.ShouldNotBe(created.Version);
        second.Errors.ShouldHaveSingleItem().Code.ShouldBe("concurrency-conflict");
        var stored = (await InScopeAsync(host, sp => sp.GetRequiredService<IGetKbArticleRequestHandler>().HandleAsync(created.Id, Ct))).Value;
        stored.Title.ShouldBe("First edit");
        stored.Version.ShouldBe(first.Value.Version);
    }

    [Fact]
    public async Task Editing_an_archived_article_stores_it_as_a_draft_and_keeps_the_first_publish_time()
    {
        await using var host = NewHost();
        await TicketScenario.CreateAsync(host);
        await KbTestData.EnsureSharedCategoryAsync(host);
        var created = (await CreateAsync(host, new CreateKbArticleRequest(null, KbTestData.SharedCategoryId, "old", "Old", null, "Body"))).Value;
        host.Clock.Advance(TimeSpan.FromHours(1));
        (await host.CommitAsync(async sp =>
        {
            var kb = sp.GetRequiredService<Application.Persistence.IKbRepository>();
            var article = (await kb.GetArticleAsync(created.Id, Ct))!;
            article.Publish(host.Clock).IsSuccess.ShouldBeTrue();
            article.Archive(host.Clock).IsSuccess.ShouldBeTrue();
            kb.UpdateArticle(article);
        })).IsSuccess.ShouldBeTrue();
        var archived = (await InScopeAsync(host, sp => sp.GetRequiredService<IGetKbArticleRequestHandler>().HandleAsync(created.Id, Ct))).Value;
        archived.Status.ShouldBe(KbArticleStatuses.Archived);
        host.Clock.Advance(TimeSpan.FromHours(1));

        var updated = await UpdateAsync(host, created.Id, new UpdateKbArticleRequest(KbTestData.SharedCategoryId, "Old, revised", null, "New body", archived.Version));

        updated.Value.Status.ShouldBe(KbArticleStatuses.Draft);
        updated.Value.PublishedAt.ShouldBe(archived.PublishedAt);
        var stored = (await InScopeAsync(host, sp => sp.GetRequiredService<IGetKbArticleRequestHandler>().HandleAsync(created.Id, Ct))).Value;
        stored.Status.ShouldBe(KbArticleStatuses.Draft);
        stored.Title.ShouldBe("Old, revised");
    }

    private static string TableWithRows(int rows) => "| a | b |\n| - | - |\n" + string.Concat(Enumerable.Repeat("| x | y |\n", rows));

    [Fact]
    public async Task A_body_over_the_element_cap_is_refused_on_create_and_on_update_with_the_real_renderer_and_nothing_is_stored()
    {
        await using var host = NewHost();
        await TicketScenario.CreateAsync(host);
        var created = (await CreateAsync(host, new CreateKbArticleRequest(null, null, "big", "Big", null, "Small"))).Value;

        var createdTooBig = await CreateAsync(host, new CreateKbArticleRequest(null, null, "huge", "Huge", null, TableWithRows(3000)));
        var updatedTooBig = await UpdateAsync(host, created.Id, new UpdateKbArticleRequest(null, "Big v2", null, TableWithRows(3000), created.Version));

        createdTooBig.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Code.ShouldBe("kb-body-too-complex"),
            error => error.Target.ShouldBe("body"));
        updatedTooBig.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-body-too-complex");
        var stored = (await InScopeAsync(host, sp => sp.GetRequiredService<IGetKbArticleRequestHandler>().HandleAsync(created.Id, Ct))).Value;
        stored.Title.ShouldBe("Big");
        stored.BodyMarkdown.ShouldBe("Small");
        stored.Version.ShouldBe(created.Version);
        (await InScopeAsync(host, sp => sp.GetRequiredService<Application.Persistence.IKbRepository>().GetArticleBySlugAsync(null, "huge", Ct))).ShouldBeNull();
    }
}
