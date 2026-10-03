using System.Net;
using System.Net.Http.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Api.Tests.Tickets;

public sealed class TicketListEndpointTests(TestPostgres postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private async Task<(ApiFactory Factory, TicketSeed Seed)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var factory = new ApiFactory(settings: database.Settings);
        return (factory, await TicketTestData.SeedAsync(factory, Ct));
    }

    [Fact]
    public async Task An_agent_lists_the_unassigned_view_with_names_and_tag_chips()
    {
        var (factory, seed) = await StartAsync();
        await using var _ = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");

        var page = (await sam.GetFromJsonAsync<PagedResponse<TicketSummaryDto>>("/api/tickets?view=unassigned", Ct))!;

        page.TotalCount.ShouldBe(2);
        page.Items.Select(row => row.Number).Order().ShouldBe(["ORB-1", "ORB-2"]);
        var login = page.Items.Single(row => row.Number == "ORB-1");
        login.ShouldSatisfyAllConditions(
            row => row.Subject.ShouldBe("Login broken"),
            row => row.ProductName.ShouldBe("Orbitly"),
            row => row.RequesterName.ShouldBe("Ann"),
            row => row.AssigneeName.ShouldBeNull(),
            row => row.Tags.ShouldHaveSingleItem().ShouldSatisfyAllConditions(tag => tag.Id.ShouldBe(seed.Billing.Id), tag => tag.Name.ShouldBe("Billing")));
    }

    [Fact]
    public async Task Mine_lists_only_the_callers_tickets()
    {
        var (factory, seed) = await StartAsync();
        await using var _ = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");
        using var kim = TicketTestData.AgentClient(factory, "kim");

        var samPage = (await sam.GetFromJsonAsync<PagedResponse<TicketSummaryDto>>("/api/tickets?view=mine", Ct))!;
        var kimPage = (await kim.GetFromJsonAsync<PagedResponse<TicketSummaryDto>>("/api/tickets?view=Mine", Ct))!;

        samPage.Items.Select(row => row.Subject).Order().ShouldBe(["Dark mode", "Export fails"]);
        samPage.Items.ShouldAllBe(row => row.AssigneeId == seed.Sam.Id && row.AssigneeName == "Sam");
        kimPage.Items.ShouldHaveSingleItem().Subject.ShouldBe("Kim's ticket");
    }

    [Fact]
    public async Task Counts_return_a_number_for_every_view()
    {
        var (factory, _) = await StartAsync();
        await using var _ = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");

        var counts = (await sam.GetFromJsonAsync<TicketViewCountsResponse>("/api/tickets/counts", Ct))!;

        counts.ShouldBe(new TicketViewCountsResponse(Unassigned: 2, Mine: 2, Open: 5, Pending: 0, All: 5, Spam: 1));
    }

    [Fact]
    public async Task A_bad_view_is_400_with_the_view_field()
    {
        var (factory, _) = await StartAsync();
        await using var _ = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");

        using var response = await sam.GetAsync("/api/tickets?view=later", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("view");
    }
}
