using System.Net;
using System.Net.Http.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.AdminEvents;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Tags;

namespace TechStrap.Api.Tests.AdminEvents;

public sealed class AdminEventEndpointTests(TestPostgres postgres)
{
    private static async Task SignInAsync(HttpClient client) =>
        (await client.GetAsync("/api/agents/me", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

    private async Task<(ApiFactory Factory, HttpClient Admin, HttpClient Agent)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var factory = new ApiFactory(settings: database.Settings);
        var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com", name: "Ada Admin"));
        var agent = factory.CreateClient().Bearer(TestJwt.Token("agent", [TestJwt.AgentGroup], email: "agent@example.com"));
        await SignInAsync(admin);
        await SignInAsync(agent);
        return (factory, admin, agent);
    }

    [Fact]
    public async Task An_admin_change_appears_in_the_audit_log_with_the_actor_label()
    {
        var (factory, admin, agent) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var ___ = agent;
        using var created = await admin.PostAsJsonAsync("/api/tags", new CreateTagRequest("bug", "Bug", "#DC2626"), TestContext.Current.CancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var page = await admin.GetFromJsonAsync<PagedResponse<AdminEventDto>>("/api/admin-events?subjectType=Tag", TestContext.Current.CancellationToken);

        page.ShouldNotBeNull();
        page.Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            item => item.Type.ShouldBe("TagCreated"),
            item => item.ActorLabel.ShouldBe("Ada Admin"));
    }

    [Fact]
    public async Task An_agent_cannot_read_the_audit_log()
    {
        var (factory, admin, agent) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var ___ = agent;

        using var response = await agent.GetAsync("/api/admin-events", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_as_of_time_with_a_non_utc_offset_filters_without_error()
    {
        var (factory, admin, agent) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var ___ = agent;
        using var created = await admin.PostAsJsonAsync("/api/tags", new CreateTagRequest("bug", "Bug", "#DC2626"), TestContext.Current.CancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var future = DateTimeOffset.UtcNow.AddHours(2).ToOffset(TimeSpan.FromHours(1)).ToString("yyyy-MM-ddTHH:mm:ssK").Replace("+", "%2B", StringComparison.Ordinal);

        using var included = await admin.GetAsync($"/api/admin-events?subjectType=Tag&asOf={future}", TestContext.Current.CancellationToken);
        using var excluded = await admin.GetAsync("/api/admin-events?subjectType=Tag&asOf=2020-01-01T00:00:00Z", TestContext.Current.CancellationToken);

        included.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await included.Content.ReadFromJsonAsync<PagedResponse<AdminEventDto>>(TestContext.Current.CancellationToken))!.Items.ShouldHaveSingleItem();
        excluded.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await excluded.Content.ReadFromJsonAsync<PagedResponse<AdminEventDto>>(TestContext.Current.CancellationToken))!.Items.ShouldBeEmpty();
    }
}
