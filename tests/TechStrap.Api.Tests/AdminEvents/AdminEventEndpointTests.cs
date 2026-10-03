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
}
