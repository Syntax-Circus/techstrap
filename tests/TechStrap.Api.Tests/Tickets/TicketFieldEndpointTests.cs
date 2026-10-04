using System.Net;
using System.Net.Http.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Api.Tests.Tickets;

public sealed class TicketFieldEndpointTests(TestPostgres postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private async Task<(ApiFactory Factory, ApiTestDatabase Database, TicketSeed Seed)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings) { ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test" };
        var factory = new ApiFactory(settings: settings);
        return (factory, database, await TicketTestData.SeedAsync(factory, Ct));
    }

    [Fact]
    public async Task Assign_then_priority_then_product_chain_row_versions_and_the_number_never_changes()
    {
        var (factory, _, seed) = await StartAsync();
        await using var _f = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");
        var ticket = seed.Tickets[1];
        var number = ticket.Number.ToString();

        var version = await TicketTestData.VersionAsync(sam, ticket.Id);
        using var assigned = await sam.PutAsJsonAsync($"/api/tickets/{ticket.Id}/assignee", new AssignTicketRequest(seed.Kim.Id, version), Ct);
        assigned.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterAssign = (await assigned.Content.ReadFromJsonAsync<TicketStateDto>(Ct))!;
        afterAssign.RowVersion.ShouldNotBe(version);

        using var prioritised = await sam.PutAsJsonAsync($"/api/tickets/{ticket.Id}/priority", new ChangeTicketPriorityRequest("Urgent", afterAssign.RowVersion), Ct);
        prioritised.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterPriority = (await prioritised.Content.ReadFromJsonAsync<TicketStateDto>(Ct))!;
        afterPriority.RowVersion.ShouldNotBe(afterAssign.RowVersion);

        using var moved = await sam.PutAsJsonAsync($"/api/tickets/{ticket.Id}/product", new MoveTicketProductRequest(seed.Paperplane.Id, afterPriority.RowVersion), Ct);
        moved.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterMove = (await moved.Content.ReadFromJsonAsync<TicketStateDto>(Ct))!;
        afterMove.RowVersion.ShouldNotBe(afterPriority.RowVersion);

        var detail = (await sam.GetFromJsonAsync<TicketDetailDto>($"/api/tickets/{ticket.Id}", Ct))!;
        detail.AssigneeId.ShouldBe(seed.Kim.Id);
        detail.Priority.ShouldBe("Urgent");
        detail.ProductId.ShouldBe(seed.Paperplane.Id);
        detail.Number.ShouldBe(number);
    }

    [Fact]
    public async Task Assigning_another_agent_queues_one_ticket_assigned_email_to_them()
    {
        var (factory, database, seed) = await StartAsync();
        await using var _f = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");
        var ticket = seed.Tickets[1];

        var version = await TicketTestData.VersionAsync(sam, ticket.Id);
        using var response = await sam.PutAsJsonAsync($"/api/tickets/{ticket.Id}/assignee", new AssignTicketRequest(seed.Kim.Id, version), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await database.ScalarAsync<long>("SELECT count(*) FROM email_outbox WHERE kind = 'ticket-assigned'")).ShouldBe(1);
        (await database.ScalarAsync<long>("SELECT count(*) FROM email_outbox WHERE kind = 'ticket-assigned' AND to_address = 'kim@example.com'")).ShouldBe(1);
    }
}
