using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace TechStrap.Api.Tests;

/// <summary>
/// Forwarded-header and per-client-IP rate-limit behaviour, per _template CLIENT_IP_RATE_LIMITING.md.
/// These use the checked-in Development default TrustedProxy network (192.0.2.0/24) and an
/// IStartupFilter for the peer address, because TrustedProxy is bound eagerly and cannot be
/// overridden through the factory's configuration.
/// </summary>
public sealed class PublicApiHardeningTests
{
    private const string PublicEndpoint = "/openapi/v1.json";
    private static readonly IPAddress TrustedPeer = IPAddress.Parse("192.0.2.5");
    private static readonly IPAddress UntrustedPeer = IPAddress.Parse("198.51.100.9");

    private static ApiFactory Factory(IPAddress peer, int permitLimit = 1) =>
        new(
            settings: new Dictionary<string, string?>
            {
                ["RateLimiting:Public:PermitLimit"] = permitLimit.ToString(),
                ["RateLimiting:Public:WindowSeconds"] = "60",
            },
            configureServices: services => services.AddSingleton<IStartupFilter>(new SetRemoteIpAddressStartupFilter(peer)));

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, PublicEndpoint);
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_visitor_past_the_limit_gets_429()
    {
        await using var factory = Factory(TrustedPeer, permitLimit: 2);
        using var client = factory.CreateClient();

        var first = await GetAsync(client, "203.0.113.10");
        var second = await GetAsync(client, "203.0.113.10");
        var third = await GetAsync(client, "203.0.113.10");

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Behind_a_trusted_proxy_each_forwarded_visitor_has_their_own_limit()
    {
        await using var factory = Factory(TrustedPeer);
        using var client = factory.CreateClient();

        var visitorOneFirst = await GetAsync(client, "203.0.113.10");
        var visitorOneSecond = await GetAsync(client, "203.0.113.10");
        var visitorTwo = await GetAsync(client, "203.0.113.20");

        visitorOneFirst.StatusCode.ShouldBe(HttpStatusCode.OK);
        visitorOneSecond.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        visitorTwo.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_spoofed_forwarded_for_from_an_untrusted_peer_is_ignored()
    {
        await using var factory = Factory(UntrustedPeer);
        using var client = factory.CreateClient();

        var first = await GetAsync(client, "203.0.113.10");
        var second = await GetAsync(client, "203.0.113.20");

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("")]
    [InlineData("203.0.113.10, , garbage, 2001:db8::1")]
    public async Task A_malformed_forwarded_for_from_a_trusted_proxy_never_causes_a_server_error(string forwardedFor)
    {
        await using var factory = Factory(TrustedPeer, permitLimit: 5);
        using var client = factory.CreateClient();

        var response = await GetAsync(client, forwardedFor);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_endpoints_are_anonymous_and_not_rate_limited()
    {
        await using var factory = Factory(TrustedPeer);
        using var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task An_unknown_route_is_denied_with_401_not_a_server_error()
    {
        // Default-deny covers every request without an AllowAnonymous endpoint, unknown routes included.
        // Without an authentication scheme to challenge with, this used to surface as a 500.
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/no/such/route", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_fallback_authorization_policy_denies_anonymous_callers()
    {
        await using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();

        var fallback = await provider.GetFallbackPolicyAsync();

        fallback.ShouldNotBeNull();
        fallback.Requirements.ShouldContain(requirement => requirement is DenyAnonymousAuthorizationRequirement);
    }
}
