using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Admin;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class AdminEventRepositoryTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task<Guid> AddAsync(
        PersistenceTestHost host,
        AdminEventType type,
        AdminSubjectType subject,
        string? payload = null,
        Guid? actorId = null,
        bool advanceClock = true)
    {
        await using var scope = host.CreateScope();
        await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        var adminEvent = AdminEvent.Record(type, actorId ?? Guid.NewGuid(), subject, Guid.NewGuid(), payload, host.Clock).Value;
        scope.ServiceProvider.GetRequiredService<IAdminEventRepository>().Add(adminEvent);
        (await work.CommitAsync(Ct)).IsSuccess.ShouldBeTrue();
        if (advanceClock)
        {
            host.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        return adminEvent.Id;
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

        var firstPage = await repository.ListAsync(new AdminEventFilter(null, null, null), 1, 2, Ct);
        var secondPage = await repository.ListAsync(new AdminEventFilter(null, null, null), 2, 2, Ct);

        firstPage.TotalCount.ShouldBe(3);
        firstPage.Items.Select(e => e.Type).ShouldBe([AdminEventType.RequesterErased, AdminEventType.TagCreated]);
        secondPage.Items.Select(e => e.Type).ShouldBe([AdminEventType.ProductCreated]);
        firstPage.Items[0].PayloadJson.ShouldContain("requesterId");  // jsonb normalizes whitespace, so compare content not text
    }

    [Fact]
    public async Task A_subject_type_filter_narrows_the_list()
    {
        await using var host = new PersistenceTestHost(Database);
        await AddAsync(host, AdminEventType.ProductCreated, AdminSubjectType.Product);
        await AddAsync(host, AdminEventType.TagCreated, AdminSubjectType.Tag);
        await using var scope = host.CreateScope();

        var tags = await scope.ServiceProvider.GetRequiredService<IAdminEventRepository>().ListAsync(new AdminEventFilter(AdminSubjectType.Tag, null, null), 1, 10, Ct);

        tags.Items.ShouldHaveSingleItem().Type.ShouldBe(AdminEventType.TagCreated);
        tags.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Events_can_be_filtered_by_actor()
    {
        await using var host = new PersistenceTestHost(Database);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var firstOlder = await AddAsync(host, AdminEventType.ProductCreated, AdminSubjectType.Product, actorId: first);
        await AddAsync(host, AdminEventType.TagCreated, AdminSubjectType.Tag, actorId: second);
        var firstNewer = await AddAsync(host, AdminEventType.TagCreated, AdminSubjectType.Tag, actorId: first);
        await AddAsync(host, AdminEventType.TagCreated, AdminSubjectType.Tag, actorId: second);
        await using var scope = host.CreateScope();

        var page = await scope.ServiceProvider.GetRequiredService<IAdminEventRepository>().ListAsync(new AdminEventFilter(null, first, null), 1, 10, Ct);

        page.TotalCount.ShouldBe(2);
        page.Items.Select(e => e.Id).ShouldBe([firstNewer, firstOlder]);
    }

    [Fact]
    public async Task An_as_of_time_hides_events_recorded_after_it()
    {
        await using var host = new PersistenceTestHost(Database);
        var asOf = host.Clock.GetUtcNow();
        await AddAsync(host, AdminEventType.ProductCreated, AdminSubjectType.Product, advanceClock: false);
        await AddAsync(host, AdminEventType.TagCreated, AdminSubjectType.Tag, advanceClock: false);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await AddAsync(host, AdminEventType.TagCreated, AdminSubjectType.Tag);
        await using var scope = host.CreateScope();

        var page = await scope.ServiceProvider.GetRequiredService<IAdminEventRepository>().ListAsync(new AdminEventFilter(null, null, asOf), 1, 10, Ct);

        page.Items.Count.ShouldBe(2);
        page.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task Events_with_the_same_time_page_in_a_stable_order()
    {
        await using var host = new PersistenceTestHost(Database);
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add(await AddAsync(host, AdminEventType.TagCreated, AdminSubjectType.Tag, advanceClock: false));
        }

        await using var scope = host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAdminEventRepository>();
        var filter = new AdminEventFilter(null, null, null);

        var firstPage = await repository.ListAsync(filter, 1, 2, Ct);
        var secondPage = await repository.ListAsync(filter, 2, 2, Ct);

        firstPage.Items.Concat(secondPage.Items).Select(e => e.Id).ShouldBe(ids, ignoreOrder: true);

        var firstAgain = await repository.ListAsync(filter, 1, 2, Ct);
        var secondAgain = await repository.ListAsync(filter, 2, 2, Ct);
        firstAgain.Items.Select(e => e.Id).ShouldBe(firstPage.Items.Select(e => e.Id));
        secondAgain.Items.Select(e => e.Id).ShouldBe(secondPage.Items.Select(e => e.Id));
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

        var page = await scope.ServiceProvider.GetRequiredService<IAdminEventRepository>().ListAsync(new AdminEventFilter(null, null, null), 0, 10_000, Ct);

        page.Page.ShouldBe(1);
        page.PageSize.ShouldBe(Paging.MaxPageSize);
    }
}
