using System.Net;
using System.Net.Http.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Api.Tests.Tickets;

public sealed class TicketDetailEndpointTests(TestPostgres postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private async Task<(ApiFactory Factory, TicketSeed Seed)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var factory = new ApiFactory(settings: database.Settings);
        return (factory, await TicketTestData.SeedAsync(factory, Ct));
    }

    [Fact]
    public async Task An_agent_reads_a_ticket_by_number_and_by_id()
    {
        var (factory, seed) = await StartAsync();
        await using var _ = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");

        var byNumber = (await sam.GetFromJsonAsync<TicketDetailDto>("/api/tickets/orb-1", Ct))!;
        var byId = (await sam.GetFromJsonAsync<TicketDetailDto>($"/api/tickets/{seed.Tickets[0].Id}", Ct))!;

        byNumber.Id.ShouldBe(byId.Id);
        byNumber.ShouldSatisfyAllConditions(
            t => t.Number.ShouldBe("ORB-1"),
            t => t.Subject.ShouldBe("Login broken"),
            t => t.ProductName.ShouldBe("Orbitly"),
            t => t.Requester.Name.ShouldBe("Ann"),
            t => t.Tags.ShouldHaveSingleItem().Name.ShouldBe("Billing"),
            t => t.Messages.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
                m => m.AuthorName.ShouldBe("Ann"), m => m.BodyHtml.ShouldBe("<p>Login broken</p>")),
            t => t.Events.ShouldNotBeEmpty());
    }

    [Fact]
    public async Task The_counts_route_is_not_mistaken_for_a_ticket_reference()
    {
        var (factory, _) = await StartAsync();
        await using var _ = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");

        using var response = await sam.GetAsync("/api/tickets/counts", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<TicketViewCountsResponse>(Ct)).ShouldNotBeNull();
    }

    [Fact]
    public async Task An_unknown_ticket_is_404_problem_details()
    {
        var (factory, _) = await StartAsync();
        await using var _ = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");

        using var response = await sam.GetAsync("/api/tickets/ORB-999", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("ticket-not-found");
    }
}
