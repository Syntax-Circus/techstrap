using System.Net;
using System.Net.Http.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Api.Tests.Tickets;

public sealed class TicketStatusEndpointTests(TestPostgres postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private async Task<(ApiFactory Factory, ApiTestDatabase Database, TicketSeed Seed)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings) { ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test" };
        var factory = new ApiFactory(settings: settings);
        return (factory, database, await TicketTestData.SeedAsync(factory, Ct));
    }

    private static async Task<uint> VersionAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync($"/api/tickets/{id}", Ct);
        return (await response.Content.ReadFromJsonAsync<TicketDetailDto>(Ct))!.RowVersion;
    }

    [Fact]
    public async Task Solving_returns_200_with_the_new_row_version_and_queues_a_solved_email()
    {
        var (factory, database, seed) = await StartAsync();
        await using var _f = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");
        var ticket = seed.Tickets[0];

        var version = await VersionAsync(sam, ticket.Id);
        using var response = await sam.PutAsJsonAsync($"/api/tickets/{ticket.Id}/status", new ChangeTicketStatusRequest("Solved", version), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<TicketStateDto>(Ct))!;
        body.Status.ShouldBe("Solved");
        body.RowVersion.ShouldNotBe(version);
        (await database.ScalarAsync<long>("SELECT count(*) FROM email_outbox WHERE kind = 'ticket-solved'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_stale_row_version_is_409_problem_details_with_concurrency_conflict()
    {
        var (factory, database, seed) = await StartAsync();
        await using var _f = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");
        var ticket = seed.Tickets[1];

        using var response = await sam.PutAsJsonAsync($"/api/tickets/{ticket.Id}/status", new ChangeTicketStatusRequest("Open", await VersionAsync(sam, ticket.Id) + 100), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("concurrency-conflict");
        (await database.ScalarAsync<long>($"SELECT count(*) FROM tickets WHERE id = '{ticket.Id}' AND status = 'New'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_note_is_201_and_queues_nothing()
    {
        var (factory, database, seed) = await StartAsync();
        await using var _f = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");
        var ticket = seed.Tickets[0];

        using var response = await sam.PostAsJsonAsync($"/api/tickets/{ticket.Id}/notes", new AddInternalNoteRequest("Customer is on the **old** plan", null), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = (await response.Content.ReadFromJsonAsync<AgentMessageResponse>(Ct))!;
        body.Message.ShouldSatisfyAllConditions(
            m => m.Visibility.ShouldBe("Internal"),
            m => m.BodyHtml.ShouldContain("<strong>old</strong>"));
        body.Ticket.Status.ShouldBe("New");
        (await database.ScalarAsync<long>("SELECT count(*) FROM email_outbox")).ShouldBe(0);
    }
}
