using Microsoft.Extensions.DependencyInjection;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Knowledge;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Agents;
using TechStrap.Infrastructure.Content;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>Publish, archive and the category handlers against real Postgres.</summary>
public sealed class KbLifecycleIntegrationTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
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
            services.AddScoped<IPublishKbArticleRequestHandler, PublishKbArticleRequestHandler>();
            services.AddScoped<IArchiveKbArticleRequestHandler, ArchiveKbArticleRequestHandler>();
            services.AddScoped<ICreateKbCategoryRequestHandler, CreateKbCategoryRequestHandler>();
            services.AddScoped<IUpdateKbCategoryRequestHandler, UpdateKbCategoryRequestHandler>();
            services.AddScoped<IDeleteKbCategoryRequestHandler, DeleteKbCategoryRequestHandler>();
        });

    private static async Task<T> RunAsync<THandler, T>(PersistenceTestHost host, Func<THandler, Task<T>> call)
        where THandler : notnull
    {
        await using var scope = host.CreateScope();
        return await call(scope.ServiceProvider.GetRequiredService<THandler>());
    }

    private static Task<Result<KbCategoryDto>> CreateCategoryAsync(PersistenceTestHost host, Guid? productId, string slug) =>
        RunAsync<ICreateKbCategoryRequestHandler, Result<KbCategoryDto>>(host, h => h.HandleAsync(new CreateKbCategoryRequest(productId, slug, slug, "About " + slug, 1), Ct));

    private static Task<Result<KbArticleDto>> CreateArticleAsync(PersistenceTestHost host, Guid? productId, Guid? categoryId, string slug) =>
        RunAsync<ICreateKbArticleRequestHandler, Result<KbArticleDto>>(host, h => h.HandleAsync(new CreateKbArticleRequest(productId, categoryId, slug, "Title " + slug, null, "Body"), Ct));

    [Fact]
    public async Task A_draft_without_a_category_cannot_be_published_and_one_with_a_category_can_be_published_archived_and_published_again()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var category = (await CreateCategoryAsync(host, scenario.Acme.Id, "getting-started")).Value;
        var bare = (await CreateArticleAsync(host, scenario.Acme.Id, null, "bare")).Value;
        var complete = (await CreateArticleAsync(host, scenario.Acme.Id, category.Id, "complete")).Value;

        var incomplete = await RunAsync<IPublishKbArticleRequestHandler, Result<KbArticleDto>>(host, h => h.HandleAsync(bare.Id, bare.Version, Ct));
        host.Clock.Advance(TimeSpan.FromHours(1));
        var published = await RunAsync<IPublishKbArticleRequestHandler, Result<KbArticleDto>>(host, h => h.HandleAsync(complete.Id, complete.Version, Ct));
        host.Clock.Advance(TimeSpan.FromHours(1));
        var archived = await RunAsync<IArchiveKbArticleRequestHandler, Result<KbArticleDto>>(host, h => h.HandleAsync(complete.Id, published.Value.Version, Ct));
        host.Clock.Advance(TimeSpan.FromHours(1));
        var again = await RunAsync<IPublishKbArticleRequestHandler, Result<KbArticleDto>>(host, h => h.HandleAsync(complete.Id, archived.Value.Version, Ct));

        incomplete.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(error => error.Code.ShouldBe("kb-publish-incomplete"), error => error.Target.ShouldBe("category"));
        published.Value.Status.ShouldBe(KbArticleStatuses.Published);
        published.Value.Version.ShouldNotBe(complete.Version);
        archived.Value.Status.ShouldBe(KbArticleStatuses.Archived);
        again.Value.Status.ShouldBe(KbArticleStatuses.Published);
        again.Value.PublishedAt.ShouldBe(published.Value.PublishedAt);
    }

    [Fact]
    public async Task A_stale_version_on_publish_is_a_conflict_and_changes_nothing()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var category = (await CreateCategoryAsync(host, scenario.Acme.Id, "faq")).Value;
        var article = (await CreateArticleAsync(host, scenario.Acme.Id, category.Id, "faq-one")).Value;
        var edited = (await RunAsync<IUpdateKbArticleRequestHandler, Result<KbArticleDto>>(host, h => h.HandleAsync(article.Id, new UpdateKbArticleRequest(category.Id, "Edited", null, "Body", article.Version), Ct))).Value;

        var stale = await RunAsync<IPublishKbArticleRequestHandler, Result<KbArticleDto>>(host, h => h.HandleAsync(article.Id, article.Version, Ct));

        stale.Errors.ShouldHaveSingleItem().Code.ShouldBe("concurrency-conflict");
        (await RunAsync<IPublishKbArticleRequestHandler, Result<KbArticleDto>>(host, h => h.HandleAsync(article.Id, edited.Version, Ct))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_category_that_holds_an_article_of_any_status_cannot_be_deleted_until_the_article_moves()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var used = (await CreateCategoryAsync(host, scenario.Acme.Id, "used")).Value;
        var other = (await CreateCategoryAsync(host, scenario.Acme.Id, "other")).Value;
        var article = (await CreateArticleAsync(host, scenario.Acme.Id, used.Id, "holder")).Value;
        var archived = (await RunAsync<IArchiveKbArticleRequestHandler, Result<KbArticleDto>>(host, h => h.HandleAsync(article.Id, article.Version, Ct))).Value;

        var blocked = await RunAsync<IDeleteKbCategoryRequestHandler, Result>(host, h => h.HandleAsync(used.Id, Ct));
        await RunAsync<IUpdateKbArticleRequestHandler, Result<KbArticleDto>>(host, h => h.HandleAsync(article.Id, new UpdateKbArticleRequest(other.Id, "Moved", null, "Body", archived.Version), Ct));
        var deleted = await RunAsync<IDeleteKbCategoryRequestHandler, Result>(host, h => h.HandleAsync(used.Id, Ct));

        blocked.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-in-use");
        deleted.IsSuccess.ShouldBeTrue();
        (await RunAsync<IDeleteKbCategoryRequestHandler, Result>(host, h => h.HandleAsync(used.Id, Ct))).Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-not-found");
    }

    [Fact]
    public async Task Category_slugs_are_refused_across_scopes_and_the_search_slug_is_reserved()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        (await CreateCategoryAsync(host, null, "shared-cat")).IsSuccess.ShouldBeTrue();
        (await CreateCategoryAsync(host, scenario.Acme.Id, "acme-cat")).IsSuccess.ShouldBeTrue();

        (await CreateCategoryAsync(host, scenario.Orbitly.Id, "shared-cat")).Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-slug-taken");
        (await CreateCategoryAsync(host, null, "acme-cat")).Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-slug-taken");
        (await CreateCategoryAsync(host, scenario.Orbitly.Id, "acme-cat")).IsSuccess.ShouldBeTrue();
        (await CreateCategoryAsync(host, scenario.Orbitly.Id, "search")).Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-reserved-slug");
    }

    [Fact]
    public async Task Two_category_edits_from_the_same_version_give_one_conflict_and_the_description_is_stored()
    {
        await using var host = NewHost();
        await TicketScenario.CreateAsync(host);
        var category = (await CreateCategoryAsync(host, null, "faq")).Value;

        var first = await RunAsync<IUpdateKbCategoryRequestHandler, Result<KbCategoryDto>>(host, h => h.HandleAsync(category.Id, new UpdateKbCategoryRequest("First", "Described", 2, category.Version), Ct));
        var second = await RunAsync<IUpdateKbCategoryRequestHandler, Result<KbCategoryDto>>(host, h => h.HandleAsync(category.Id, new UpdateKbCategoryRequest("Second", null, 3, category.Version), Ct));

        first.Value.ShouldSatisfyAllConditions(dto => dto.Name.ShouldBe("First"), dto => dto.Description.ShouldBe("Described"), dto => dto.Version.ShouldNotBe(category.Version));
        second.Errors.ShouldHaveSingleItem().Code.ShouldBe("concurrency-conflict");
    }
}
