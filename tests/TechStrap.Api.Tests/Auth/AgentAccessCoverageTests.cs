using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Contracts.Agents;

namespace TechStrap.Api.Tests.Auth;

/// <summary>Every controller route refuses anonymous callers, callers outside the groups, and deactivated agents (D-029).</summary>
public sealed class AgentAccessCoverageTests(TestPostgres postgres)
{
    private static IReadOnlyList<(string Method, string Path)> Routes(ApiFactory factory) =>
        [.. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("api/", StringComparison.Ordinal) == true)
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => (method, "/" + string.Join('/', endpoint.RoutePattern.PathSegments.Select(segment =>
                    segment.IsSimple && segment.Parts[0] is Microsoft.AspNetCore.Routing.Patterns.RoutePatternLiteralPart literal
                        ? literal.Content
                        : Guid.CreateVersion7().ToString())))))];

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path) =>
        await client.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), path) { Content = method is "GET" or "DELETE" ? null : new StringContent("{}", Encoding.UTF8, "application/json") },
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_deactivated_agent_is_refused_on_every_agent_endpoint()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: database.Settings);
        using var client = factory.CreateClient().Bearer(TestJwt.Token("off", [TestJwt.AdminGroup], email: "off@example.com"));
        (await client.GetFromJsonAsync<AgentDto>("/api/agents/me", TestContext.Current.CancellationToken)).ShouldNotBeNull();
        await database.ExecuteAsync("UPDATE agents SET is_active = false WHERE oidc_subject = 'off'");
        var routes = Routes(factory);

        routes.Count.ShouldBe(Controllers.ControllerActions.All().Count());
        foreach (var (method, path) in routes)
        {
            using var response = await SendAsync(client, method, path);
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {path}");
            (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("agent-inactive", customMessage: $"{method} {path}");
        }
    }

    [Fact]
    public async Task Anonymous_callers_get_401_and_callers_outside_the_groups_get_403_everywhere()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: database.Settings);
        using var anonymous = factory.CreateClient();
        using var outsider = factory.CreateClient().Bearer(TestJwt.Token("outsider", ["someone-else"]));

        foreach (var (method, path) in Routes(factory))
        {
            using var anonymousResponse = await SendAsync(anonymous, method, path);
            using var outsiderResponse = await SendAsync(outsider, method, path);
            anonymousResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {path}");
            outsiderResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {path}");
        }
    }

    [Fact]
    public async Task An_agent_group_member_is_refused_on_every_admin_only_route()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: database.Settings);
        using var client = factory.CreateClient().Bearer(TestJwt.Token("plain-agent", [TestJwt.AgentGroup], email: "agent@example.com"));
        (await client.GetFromJsonAsync<AgentDto>("/api/agents/me", TestContext.Current.CancellationToken)).ShouldNotBeNull();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("api/", StringComparison.Ordinal) == true)
            .Select(endpoint => (Endpoint: endpoint, Action: endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!))
            .ToList();
        var adminOnly = endpoints.Where(item => IsAdminOnly(item.Action.MethodInfo)).ToList();

        adminOnly.Count.ShouldBeGreaterThanOrEqualTo(7);
        foreach (var (endpoint, _) in adminOnly)
        {
            var path = "/" + string.Join('/', endpoint.RoutePattern.PathSegments.Select(segment =>
                segment.IsSimple && segment.Parts[0] is Microsoft.AspNetCore.Routing.Patterns.RoutePatternLiteralPart literal
                    ? literal.Content
                    : Guid.CreateVersion7().ToString()));
            var method = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods[0] ?? "GET";
            using var response = await SendAsync(client, method, path);
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {path}");
        }

        using var allowed = await SendAsync(client, "GET", "/api/tags");
        allowed.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);
        allowed.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
    }

    private static bool IsAdminOnly(MethodInfo action) =>
        (action.GetCustomAttribute<AuthorizeAttribute>() ?? action.DeclaringType!.GetCustomAttribute<AuthorizeAttribute>())?.Policy == TechStrap.Api.Security.AuthorizationPolicies.Admin;
}
