using System.Net;
using System.Net.Http.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Webhooks;
using TechStrap.Api.Tests.Intake;
using TechStrap.Contracts.Intake;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Api.Tests.Settings;

public sealed class WebhookEndpointTests(TestPostgres postgres)
{

    [Fact(Timeout = 120_000)]
    public async Task Admin_saves_masked_settings_tests_destination_and_gets_conflict_for_stale_version()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: database.Settings);
        using var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com"));
        (await admin.GetAsync("/api/agents/me", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        using var saved = await admin.PutAsJsonAsync("/api/settings/webhooks", new UpdateWebhookSettingsRequest("Replace", 0, [new() { Name = "Discord", Url = "https://discord.com/api/webhooks/123/secret" }]), TestContext.Current.CancellationToken);
        saved.EnsureSuccessStatusCode();
        (await saved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain("secret");
        var config = (await saved.Content.ReadFromJsonAsync<WebhookSettingsDto>(TestContext.Current.CancellationToken))!;
        using var stale = await admin.PutAsJsonAsync("/api/settings/webhooks", new UpdateWebhookSettingsRequest("Disabled", 0, []), TestContext.Current.CancellationToken);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var test = await admin.PostAsJsonAsync("/api/settings/webhooks/actions", new WebhookActionRequest("Test", config.Endpoints[0].Settings.Id), TestContext.Current.CancellationToken);
        test.EnsureSuccessStatusCode();
        var history = await admin.GetFromJsonAsync<WebhookDeliveryPageDto>("/api/settings/webhooks/deliveries", TestContext.Current.CancellationToken);
        history!.Items.ShouldHaveSingleItem().Status.ShouldBe("Pending");
        (await database.ScalarAsync<long>("SELECT count(*) FROM admin_events WHERE type = 'WebhookSettingsUpdated'")).ShouldBe(1);
    }

    [Theory(Timeout = 120_000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Anonymous_and_agents_cannot_access_settings_or_delivery_operations(bool agent)
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: database.Settings);
        using var client = factory.CreateClient();
        if (agent) client.Bearer(TestJwt.Token("agent", [TestJwt.AgentGroup], email: "agent@example.com"));
        var expected = agent ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized;
        var routes = new[] { "/api/settings/webhooks", $"/api/products/{Guid.NewGuid()}/webhooks" };
        foreach (var route in routes)
        {
            using var get = await client.GetAsync(route, TestContext.Current.CancellationToken);
            using var put = await client.PutAsJsonAsync(route, new UpdateWebhookSettingsRequest("Replace", 0, []), TestContext.Current.CancellationToken);
            using var list = await client.GetAsync(route + "/deliveries", TestContext.Current.CancellationToken);
            using var action = await client.PostAsJsonAsync(route + "/actions", new WebhookActionRequest("Test", Guid.NewGuid()), TestContext.Current.CancellationToken);
            get.StatusCode.ShouldBe(expected); put.StatusCode.ShouldBe(expected); list.StatusCode.ShouldBe(expected); action.StatusCode.ShouldBe(expected);
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task Intake_replay_does_not_duplicate_and_customer_reply_reopening_queues_selected_status()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: database.Settings);
        var seed = await IntakeTestData.SeedAsync(factory.Services, ct);
        using var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com"));
        (await admin.GetAsync("/api/agents/me", ct)).EnsureSuccessStatusCode();
        using var saved = await admin.PutAsJsonAsync("/api/settings/webhooks", new UpdateWebhookSettingsRequest("Replace", 0, [new() { Url = "https://discord.com/api/webhooks/123/secret", TicketStatuses = ["Solved", "Open"] }]), ct);
        saved.EnsureSuccessStatusCode();
        using var intake = factory.CreateClient();
        SubmitTicketResponse? submitted = null;
        for (var i = 0; i < 2; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/intake/tickets") { Content = JsonContent.Create(new SubmitTicketRequest("customer@example.com", "Customer", "Webhook test", "Private body", null, null)) };
            request.Headers.Add(HeaderNames.ApiKey, seed.OrbitlyTrusted);
            request.Headers.Add(HeaderNames.IdempotencyKey, "webhook-replay");
            using var result = await intake.SendAsync(request, ct);
            result.EnsureSuccessStatusCode();
            submitted = await result.Content.ReadFromJsonAsync<SubmitTicketResponse>(ct);
        }
        (await database.ScalarAsync<long>("SELECT count(*) FROM webhook_deliveries")).ShouldBe(1);
        var ticketId = await database.ScalarAsync<Guid>("SELECT id FROM tickets WHERE number = 'ORB-1'");
        var detail = (await admin.GetFromJsonAsync<TicketDetailDto>($"/api/tickets/{ticketId}", ct))!;
        using var solved = await admin.PutAsJsonAsync($"/api/tickets/{ticketId}/status", new ChangeTicketStatusRequest("Solved", detail.RowVersion), ct);
        solved.EnsureSuccessStatusCode();
        using var reply = new HttpRequestMessage(HttpMethod.Post, "/api/customer/ticket/replies") { Content = new MultipartFormDataContent { { new StringContent("Still broken"), "body" } } };
        reply.Headers.Add(HeaderNames.TicketToken, new Uri(submitted!.ViewUrl!).Segments.Last());
        using var replied = await intake.SendAsync(reply, ct);
        replied.EnsureSuccessStatusCode();
        (await database.ScalarAsync<long>("SELECT count(*) FROM webhook_deliveries")).ShouldBe(3);
        (await database.ScalarAsync<long>("SELECT count(*) FROM webhook_deliveries WHERE payload->'entity'->>'previousStatus' = 'Solved' AND payload->'entity'->>'status' = 'Open'")).ShouldBe(1);
        (await database.ScalarAsync<long>("SELECT count(*) FROM webhook_deliveries WHERE payload::text LIKE '%Private body%' OR payload::text LIKE '%customer@example.com%'")).ShouldBe(0);
    }
}
