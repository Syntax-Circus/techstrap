using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Api.Security;

namespace TechStrap.Api.Tests.Auth;

/// <summary>Every api route names exactly one known policy, so no route is left unauthenticated by omission (D-034).</summary>
public sealed class RoutePolicyCoverageTests
{
    private static readonly string[] KnownPolicies =
        [AuthorizationPolicies.Agent, AuthorizationPolicies.Admin, AuthorizationPolicies.ApiKey, AuthorizationPolicies.Public];

    private static List<RouteEndpoint> ApiEndpoints(ApiFactory factory) =>
        [.. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("api/", StringComparison.Ordinal) == true)];

    [Fact]
    public void Every_api_route_declares_exactly_one_known_policy()
    {
        using var factory = new ApiFactory();
        var endpoints = ApiEndpoints(factory);

        endpoints.ShouldNotBeEmpty();
        foreach (var endpoint in endpoints)
        {
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy).ToList();
            var name = endpoint.RoutePattern.RawText;
            policies.Count.ShouldBeGreaterThan(0, name);
            policies.ShouldAllBe(policy => KnownPolicies.Contains(policy), name);

            // Agent routes may stack Admin on top of a controller-level Agent (both are enforced); an ApiKey or Public
            // route stands alone, so a route can never mix a key scheme with an agent policy.
            if (policies.Any(policy => policy is AuthorizationPolicies.ApiKey or AuthorizationPolicies.Public))
            {
                policies.Count.ShouldBe(1, name);
            }

            endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull(name);
        }
    }

    [Fact]
    public void Every_public_and_api_key_route_is_rate_limited()
    {
        using var factory = new ApiFactory();
        var open = ApiEndpoints(factory).Where(endpoint =>
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(data => data.Policy is AuthorizationPolicies.Public or AuthorizationPolicies.ApiKey));

        foreach (var endpoint in open)
        {
            endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>().ShouldNotBeNull(endpoint.RoutePattern.RawText);
        }
    }
}
