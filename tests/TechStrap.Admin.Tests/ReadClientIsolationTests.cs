using System.Net;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

/// <summary>The read client is shared by every agent and every read: one failing endpoint must never lock anyone out (no circuit breaker).</summary>
public sealed class ReadClientIsolationTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_500_on_one_endpoint_does_not_stop_the_next_me_call_of_the_same_or_another_agent()
    {
        await using var factory = new AdminFactory();
        factory.Api.OnStatus(HttpMethod.Get, "/api/tickets/counts", HttpStatusCode.InternalServerError);
        using var agent = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);
        await agent.GetStringAsync("/", Ct);
        var meAfterFirst = factory.Api.Count(HttpMethod.Get, "/api/agents/me");

        using var admin = factory.CreateClient().SignedInAs(AdminTestPrincipal.Admin);
        var html = await admin.GetStringAsync("/", Ct);
        using var again = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);
        await again.GetStringAsync("/", Ct);

        html.ShouldNotContain("could not reach the API");
        factory.Api.Requests.ShouldContain(r => r.Path == "/api/agents/me" && r.Authorization!.EndsWith("admin"));
        factory.Api.Count(HttpMethod.Get, "/api/agents/me").ShouldBeGreaterThan(meAfterFirst + 1);
    }
}
