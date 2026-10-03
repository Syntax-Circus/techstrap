using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Admin;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class AdminEventRepositoryTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task AddAsync(PersistenceTestHost host, AdminEventType type, AdminSubjectType subject, string? payload = null)
    {
        await using var scope = host.CreateScope();
        await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        scope.ServiceProvider.GetRequiredService<IAdminEventRepository>().Add(
            AdminEvent.Record(type, Guid.NewGuid(), subject, Guid.NewGuid(), payload, host.Clock).Value);
        (await work.CommitAsync(Ct)).IsSuccess.ShouldBeTrue();
        host.Clock.Advance(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Events_are_listed_newest_first_with_paging_and_a_total()
    {
        await using var host = new PersistenceTestHost(Database);
        await AddAsync(host, AdminEventType.ProductCreated, AdminSubjectType.Product);
        await AddAsync(host, AdminEventType.TagCreated, AdminSubjectType.Tag);
        await AddAsync(host, AdminEventType.RequesterErased, AdminSubjectType.Requester, "{\"requesterId\":\"x\"}");
        await using var scope = host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAdminEventRepository>();

        var firstPage = await repository.ListAsync(null, 1, 2, Ct);
        var secondPage = await repository.ListAsync(null, 2, 2, Ct);

        firstPage.TotalCount.ShouldBe(3);
        firstPage.Items.Select(e => e.Type).ShouldBe([AdminEventType.RequesterErased, AdminEventType.TagCreated]);
        secondPage.Items.Select(e => e.Type).ShouldBe([AdminEventType.ProductCreated]);
        firstPage.Items[0].PayloadJson.ShouldContain("requesterId");  // jsonb normalises whitespace, so compare content not text
    }

    [Fact]
    public async Task A_subject_type_filter_narrows_the_list()
    {
        await using var host = new PersistenceTestHost(Database);
        await AddAsync(host, AdminEventType.ProductCreated, AdminSubjectType.Product);
        await AddAsync(host, AdminEventType.TagCreated, AdminSubjectType.Tag);
        await using var scope = host.CreateScope();

        var tags = await scope.ServiceProvider.GetRequiredService<IAdminEventRepository>().ListAsync(AdminSubjectType.Tag, 1, 10, Ct);

        tags.Items.ShouldHaveSingleItem().Type.ShouldBe(AdminEventType.TagCreated);
        tags.TotalCount.ShouldBe(1);
    }

    [Fact]
    public void The_repository_offers_no_way_to_update_or_delete_an_event()
    {
        typeof(IAdminEventRepository).GetMethods().Select(m => m.Name).Order().ShouldBe(["Add", "ListAsync"]);
    }

    [Fact]
    public async Task An_out_of_range_page_size_is_clamped_to_the_maximum()
    {
        await using var host = new PersistenceTestHost(Database);
        await AddAsync(host, AdminEventType.ProductCreated, AdminSubjectType.Product);
        await using var scope = host.CreateScope();

        var page = await scope.ServiceProvider.GetRequiredService<IAdminEventRepository>().ListAsync(null, 0, 10_000, Ct);

        page.Page.ShouldBe(1);
        page.PageSize.ShouldBe(Paging.MaxPageSize);
    }
}
