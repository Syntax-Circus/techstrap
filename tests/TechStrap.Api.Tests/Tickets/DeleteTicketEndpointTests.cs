using System.Net;
using System.Net.Http.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.AdminEvents;
using TechStrap.Contracts.Paging;

namespace TechStrap.Api.Tests.Tickets;

public sealed class DeleteTicketEndpointTests(TestPostgres postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private async Task<(ApiFactory Factory, TicketSeed Seed, HttpClient Admin)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var factory = new ApiFactory(settings: database.Settings);
        var seed = await TicketTestData.SeedAsync(factory, Ct);
        var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com"));
        (await admin.GetAsync("/api/agents/me", Ct)).EnsureSuccessStatusCode();
        return (factory, seed, admin);
    }

    [Fact]
    public async Task An_admin_deletes_a_ticket_and_it_is_gone()
    {
        var (factory, seed, admin) = await StartAsync();
        await using var _f = factory;
        using var _a = admin;
        var ticket = seed.Tickets[1];

        using var deleted = await admin.DeleteAsync($"/api/tickets/{ticket.Id}", Ct);
        using var get = await admin.GetAsync($"/api/tickets/{ticket.Id}", Ct);
        using var again = await admin.DeleteAsync($"/api/tickets/{ticket.Id}", Ct);

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await again.Content.ReadAsStringAsync(Ct)).ShouldContain("ticket-not-found");
        var page = (await admin.GetFromJsonAsync<PagedResponse<AdminEventDto>>("/api/admin-events?subjectType=Ticket", Ct))!;
        var audit = page.Items.ShouldHaveSingleItem();
        audit.Type.ShouldBe("TicketDeleted");
        audit.SubjectId.ShouldBe(ticket.Id);
        audit.Payload.ShouldNotContain(ticket.Subject);
    }

    [Fact]
    public async Task An_agent_who_is_not_an_admin_cannot_delete()
    {
        var (factory, seed, admin) = await StartAsync();
        await using var _f = factory;
        using var _a = admin;
        using var sam = TicketTestData.AgentClient(factory, "sam");

        using var response = await sam.DeleteAsync($"/api/tickets/{seed.Tickets[1].Id}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var still = await admin.GetAsync($"/api/tickets/{seed.Tickets[1].Id}", Ct);
        still.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        var (factory, seed, admin) = await StartAsync();
        await using var _f = factory;
        using var _a = admin;
        using var anonymous = factory.CreateClient();

        using var response = await anonymous.DeleteAsync($"/api/tickets/{seed.Tickets[1].Id}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
