using System.Net;
using System.Net.Http.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Api.Tests.Customer;
using TechStrap.Api.Tests.Tickets;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Api.Tests.Requesters;

public sealed class EraseRequesterEndpointTests(TestPostgres postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private async Task<(ApiFactory Factory, CustomerSeed Seed, ApiTestDatabase Database, HttpClient Admin, Guid Ann)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var factory = new ApiFactory(settings: new Dictionary<string, string?>(database.Settings));
        var seed = await CustomerTestData.SeedAsync(factory, Ct);
        var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com"));
        (await admin.GetAsync("/api/agents/me", Ct)).EnsureSuccessStatusCode();
        var ann = await database.ScalarAsync<Guid>("SELECT id FROM requesters WHERE email = 'ann@example.com'");
        return (factory, seed, database, admin, ann);
    }

    private static HttpRequestMessage CustomerGet(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/customer/ticket");
        request.Headers.Add(HeaderNames.TicketToken, token);
        return request;
    }

    [Fact]
    public async Task An_admin_erases_a_requester_and_the_customer_link_stops_working()
    {
        var (factory, seed, _, admin, ann) = await StartAsync();
        await using var _f = factory;
        using var _a = admin;
        using var customer = factory.CreateClient();
        using var before = await customer.SendAsync(CustomerGet(seed.ValidToken), Ct);
        before.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var erased = await admin.PostAsync($"/api/requesters/{ann}/erase", null, Ct);

        erased.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var after = await customer.SendAsync(CustomerGet(seed.ValidToken), Ct);
        after.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await after.Content.ReadAsStringAsync(Ct)).ShouldContain("not-found");
    }

    [Fact]
    public async Task A_search_for_the_old_email_finds_nothing()
    {
        var (factory, seed, database, admin, ann) = await StartAsync();
        await using var _f = factory;
        using var _a = admin;
        using var sam = TicketTestData.AgentClient(factory, "sam");
        await database.ExecuteAsync($"UPDATE messages SET body = '<p>ann@example.com cannot log in</p>' WHERE ticket_id = '{seed.TicketId}' AND id = (SELECT id FROM messages WHERE ticket_id = '{seed.TicketId}' ORDER BY created_at LIMIT 1)");
        var before = (await sam.GetFromJsonAsync<PagedResponse<TicketSummaryDto>>("/api/tickets?search=ann@example.com", Ct))!;
        before.TotalCount.ShouldBeGreaterThan(0, "the canary body must be found before the erase");

        using var erased = await admin.PostAsync($"/api/requesters/{ann}/erase", null, Ct);

        erased.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var after = (await sam.GetFromJsonAsync<PagedResponse<TicketSummaryDto>>("/api/tickets?search=ann@example.com", Ct))!;
        after.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task An_unknown_requester_is_404_requester_not_found()
    {
        var (factory, _, _, admin, _) = await StartAsync();
        await using var _f = factory;
        using var _a = admin;

        using var response = await admin.PostAsync($"/api/requesters/{Guid.NewGuid()}/erase", null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("requester-not-found");
    }

    [Fact]
    public async Task An_agent_who_is_not_an_admin_cannot_erase_and_an_anonymous_caller_is_refused()
    {
        var (factory, _, database, admin, ann) = await StartAsync();
        await using var _f = factory;
        using var _a = admin;
        using var sam = TicketTestData.AgentClient(factory, "sam");
        using var anonymous = factory.CreateClient();

        using var forbidden = await sam.PostAsync($"/api/requesters/{ann}/erase", null, Ct);
        using var unauthorized = await anonymous.PostAsync($"/api/requesters/{ann}/erase", null, Ct);

        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        unauthorized.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await database.ScalarAsync<long>($"SELECT count(*) FROM requesters WHERE id = '{ann}' AND erased_at IS NULL")).ShouldBe(1);
    }

    [Fact]
    public async Task Erasing_twice_returns_204_both_times()
    {
        var (factory, _, _, admin, ann) = await StartAsync();
        await using var _f = factory;
        using var _a = admin;

        using var first = await admin.PostAsync($"/api/requesters/{ann}/erase", null, Ct);
        using var second = await admin.PostAsync($"/api/requesters/{ann}/erase", null, Ct);

        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        second.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
