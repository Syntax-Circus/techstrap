using System.Net;
using System.Net.Http.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Paging;

namespace TechStrap.Api.Tests.Agents;

public sealed class AgentManagementEndpointTests(TestPostgres postgres)
{
    private static async Task<AgentDto> SignInAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<AgentDto>("/api/agents/me", TestContext.Current.CancellationToken))!;

    [Fact]
    public async Task An_agent_cannot_change_another_agent()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: database.Settings);
        using var agent = factory.CreateClient().Bearer(TestJwt.Token("agent", [TestJwt.AgentGroup], email: "agent@example.com"));
        var me = await SignInAsync(agent);

        using var response = await agent.PutAsJsonAsync($"/api/agents/{me.Id}", new UpdateAgentRequest(false), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_agent_sees_only_active_agents_without_admin_fields()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: database.Settings);
        using var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com"));
        using var agent = factory.CreateClient().Bearer(TestJwt.Token("agent", [TestJwt.AgentGroup], email: "agent@example.com"));
        using var other = factory.CreateClient().Bearer(TestJwt.Token("other", [TestJwt.AgentGroup], email: "other@example.com"));
        await SignInAsync(admin);
        await SignInAsync(agent);
        var otherMe = await SignInAsync(other);
        using var update = await admin.PutAsJsonAsync($"/api/agents/{otherMe.Id}", new UpdateAgentRequest(false), TestContext.Current.CancellationToken);
        update.EnsureSuccessStatusCode();

        var page = (await agent.GetFromJsonAsync<PagedResponse<AgentListItemDto>>("/api/agents", TestContext.Current.CancellationToken))!;

        page.Items.Select(item => item.Id).ShouldNotContain(otherMe.Id);
        page.Items.ShouldAllBe(item => item.Email == null && item.Role == null);
    }

    [Fact]
    public async Task Two_admins_deactivating_each_other_at_once_leave_one_active_admin()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: database.Settings);
        using var first = factory.CreateClient().Bearer(TestJwt.Token("admin-1", [TestJwt.AdminGroup], email: "one@example.com"));
        using var second = factory.CreateClient().Bearer(TestJwt.Token("admin-2", [TestJwt.AdminGroup], email: "two@example.com"));
        var one = await SignInAsync(first);
        var two = await SignInAsync(second);

        var responses = await Task.WhenAll(
            first.PutAsJsonAsync($"/api/agents/{two.Id}", new UpdateAgentRequest(false), TestContext.Current.CancellationToken),
            second.PutAsJsonAsync($"/api/agents/{one.Id}", new UpdateAgentRequest(false), TestContext.Current.CancellationToken));

        try
        {
            responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1);
            responses.Single(response => response.StatusCode != HttpStatusCode.OK).StatusCode
                .ShouldBeOneOf(HttpStatusCode.Conflict, HttpStatusCode.Forbidden);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        (await database.ScalarAsync<long>("SELECT count(*) FROM agents WHERE role = 'Admin' AND is_active")).ShouldBe(1);
    }
}
