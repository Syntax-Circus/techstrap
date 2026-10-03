using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Contracts.Agents;

namespace TechStrap.Api.Tests.Auth;

public sealed class AgentProvisioningTests(TestPostgres postgres)
{
    private static ApiFactory Factory(ApiTestDatabase database) =>
        new(settings: database.Settings, configureServices: services => services.AddControllers().AddApplicationPart(typeof(AuthProbeController).Assembly));

    [Fact]
    public async Task The_first_call_creates_the_agent_and_the_next_call_reuses_it()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = Factory(database);
        using var client = factory.CreateClient().Bearer(TestJwt.Token("sub-1", [TestJwt.AdminGroup], email: "sam@example.com", name: "Sam Whitfield"));

        var first = await client.GetFromJsonAsync<AgentDto>("/api/agents/me", TestContext.Current.CancellationToken);
        var second = await client.GetFromJsonAsync<AgentDto>("/api/agents/me", TestContext.Current.CancellationToken);

        first!.Role.ShouldBe(AgentRoles.Admin);
        second!.Id.ShouldBe(first.Id);
        (await database.ScalarAsync<long>("SELECT count(*) FROM agents")).ShouldBe(1);
    }

    [Fact]
    public async Task Two_concurrent_first_calls_create_one_agent()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = Factory(database);
        var token = TestJwt.Token("sub-race", [TestJwt.AgentGroup], email: "race@example.com");

        async Task<HttpStatusCode> CallAsync()
        {
            using var client = factory.CreateClient().Bearer(token);
            using var response = await client.GetAsync("/api/agents/me", TestContext.Current.CancellationToken);
            return response.StatusCode;
        }

        var statuses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => CallAsync()));

        statuses.ShouldAllBe(status => status == HttpStatusCode.OK);
        (await database.ScalarAsync<long>("SELECT count(*) FROM agents WHERE oidc_subject = 'sub-race'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_deactivated_agent_is_refused_on_every_agent_endpoint()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = Factory(database);
        using var client = factory.CreateClient().Bearer(TestJwt.Token("sub-off", [TestJwt.AdminGroup], email: "off@example.com"));
        (await client.GetAsync("/api/agents/me", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await database.ExecuteAsync("UPDATE agents SET is_active = false WHERE oidc_subject = 'sub-off'");

        foreach (var path in new[] { "/api/agents/me", "/__test/agent", "/__test/admin" })
        {
            using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
            (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("agent-inactive", customMessage: path);
        }
    }
}
