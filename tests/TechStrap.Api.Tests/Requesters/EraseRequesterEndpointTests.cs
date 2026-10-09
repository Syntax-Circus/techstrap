using System.Net;
using System.Net.Http.Json;
using TechStrap.Api.Tests.Auth;
using Serilog.Events;
using TechStrap.Api.Tests.Customer;
using TechStrap.Api.Tests.Intake;
using TechStrap.Api.Tests.Tickets;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Intake;
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

    /// <summary>The problem document carries the error code in its type member.</summary>
    private static string? ProblemCode(string json) => System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("type").GetString();

    private static HttpRequestMessage CustomerGet(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/customer/ticket");
        request.Headers.Add(HeaderNames.TicketToken, token);
        return request;
    }

    private static string Everything(LogEvent e) => string.Join('\n', [e.RenderMessage(), e.Exception?.ToString() ?? string.Empty, .. e.Properties.Values.Select(v => v.ToString())]);

    [Fact(Timeout = 120_000)]
    public async Task Erasing_a_requester_writes_no_email_name_or_token_to_any_log_event()
    {
        const string email = "erase.me.7f3a@example.com";
        const string localPart = "erase.me.7f3a";
        const string name = "Zelda Quillfeather";
        const string subject = "Quill subject 7f3a";
        const string body = "Quill body 7f3a";
        var database = await ApiTestDatabase.CreateAsync(postgres);
        // Every level is captured, as in AdminLeakTests: a value that only shows at Verbose is still a leak.
        var settings = new Dictionary<string, string?>(database.Settings)
        {
            ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test",
            ["Serilog:MinimumLevel:Default"] = "Verbose",
            ["Serilog:MinimumLevel:Override:Microsoft"] = "Verbose",
            ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore"] = "Verbose",
            ["Serilog:MinimumLevel:Override:System"] = "Verbose",
        };
        await using var factory = new ApiFactory(settings: settings);
        var seed = await IntakeTestData.SeedAsync(factory.Services, Xunit.TestContext.Current.CancellationToken);
        using var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com"));
        (await admin.GetAsync("/api/agents/me", Xunit.TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        using var intake = new HttpRequestMessage(HttpMethod.Post, "/api/intake/tickets")
        {
            Content = JsonContent.Create(new SubmitTicketRequest(email, name, subject, body, null, null)),
        };
        intake.Headers.Add(HeaderNames.ApiKey, seed.OrbitlyTrusted);
        using var submitted = await factory.CreateClient().SendAsync(intake, Xunit.TestContext.Current.CancellationToken);
        submitted.StatusCode.ShouldBe(HttpStatusCode.Created);
        var viewUrl = (await submitted.Content.ReadFromJsonAsync<SubmitTicketResponse>(Xunit.TestContext.Current.CancellationToken))!.ViewUrl!;
        var token = viewUrl[(viewUrl.IndexOf("/t/", StringComparison.Ordinal) + 3)..].Split('?', '#', '/')[0];
        token.ShouldNotBeEmpty();
        var requesterId = await database.ScalarAsync<Guid>($"SELECT id FROM requesters WHERE email = '{email}'");

        using var erased = await admin.PostAsync($"/api/requesters/{requesterId}/erase", null, Xunit.TestContext.Current.CancellationToken);

        erased.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var events = factory.LogSink.Events.ToList();
        events.Count.ShouldBeGreaterThan(0, "a silent sink would prove nothing");
        events.ShouldContain(e => e.Level <= LogEventLevel.Debug, "the Verbose setting must have taken effect");
        events.ShouldContain(e => e.RenderMessage().Contains("/erase", StringComparison.Ordinal) && e.RenderMessage().Contains("POST", StringComparison.Ordinal), "the erase request itself must have been logged");
        foreach (var needle in new[] { email, localPart, name, subject, body, token })
        {
            events.Select(Everything).ShouldAllBe(text => !text.Contains(needle, StringComparison.OrdinalIgnoreCase), $"'{needle}' must not appear in any log event");
        }
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
        ProblemCode(await after.Content.ReadAsStringAsync(Ct)).ShouldBe("not-found");
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
        ProblemCode(await response.Content.ReadAsStringAsync(Ct)).ShouldBe("requester-not-found");
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
