using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Api.Tests.Auth;

namespace TechStrap.Api.Tests.Kb;

/// <summary>The two PHASE-09c lists sit under the Public rate limit like every other anonymous route: a visitor past the limit gets a 429 that is never cached.</summary>
public sealed class PublicListRateLimitTests(TestPostgres postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/api/public/products")]
    [InlineData("/api/public/kb/orbitly/categories/account/articles")]
    public async Task A_visitor_past_the_public_limit_gets_a_429_on_the_new_lists(string path)
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings)
        {
            ["RateLimiting:Public:PermitLimit"] = "2",
            ["RateLimiting:Public:WindowSeconds"] = "60",
        };
        await using var factory = new ApiFactory(
            settings: settings,
            configureServices: services => services.AddSingleton<IStartupFilter>(new SetRemoteIpAddressStartupFilter(IPAddress.Parse("192.0.2.5"))));
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? last = null;
        for (var i = 0; i < 3; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.10");
            last?.Dispose();
            last = await client.SendAsync(request, Ct);
            statuses.Add(last.StatusCode);
        }

        statuses[2].ShouldBe(HttpStatusCode.TooManyRequests);
        statuses.Take(2).ShouldNotContain(HttpStatusCode.TooManyRequests);
        last!.Headers.CacheControl?.Public.ShouldNotBe(true);
        last.Dispose();
    }
}
