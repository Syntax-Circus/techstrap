using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using TechStrap.Api.Tests.Auth;
using TechStrap.Api.Tests.Tickets;

namespace TechStrap.Api.Tests.Intake;

public sealed class MultipartRouteContractTests(TestPostgres postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static HttpRequestMessage Json(string path) =>
        new(HttpMethod.Post, path) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData("/api/customer/ticket/replies")]                 // Public policy: 415, no longer the fallback policy's 401
    [InlineData("/api/public/products/orbitly/tickets")]
    public async Task A_json_body_on_a_public_multipart_route_is_415_problem_json(string path)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var request = Json(path);

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Type.ShouldBe("unsupported-media-type");
    }

    [Fact]
    public async Task The_agent_reply_route_runs_its_policy_before_the_415()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: database.Settings);
        using var anonymous = factory.CreateClient();
        using var agent = TicketTestData.AgentClient(factory, "sam");
        (await agent.GetAsync("/api/agents/me", Ct)).EnsureSuccessStatusCode();
        var path = $"/api/tickets/{Guid.NewGuid()}/replies";

        using var unauthenticated = Json(path);
        using var withAgent = Json(path);

        (await anonymous.SendAsync(unauthenticated, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await agent.SendAsync(withAgent, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
    }
}
