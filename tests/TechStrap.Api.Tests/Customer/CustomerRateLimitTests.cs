using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Api.Options;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Tickets;
using TechStrap.Tests.Shared;

namespace TechStrap.Api.Tests.Customer;

public sealed class CustomerRateLimitTests(TestPostgres postgres)
{
    private static readonly IPAddress TrustedPeer = IPAddress.Parse("192.0.2.5");

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string ip)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/customer/ticket");
        request.Headers.Add(HeaderNames.TicketToken, "garbage");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", ip);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string ip)
    {
        using var response = await GetAsync(client, ip);
        return response.StatusCode;
    }

    [Fact]
    public async Task Token_access_is_limited_per_ip()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(
            settings: new Dictionary<string, string?>(database.Settings) { [$"{CustomerRateLimitOptions.SectionName}:TokenAccessPermitLimit"] = "2" },
            configureServices: services => services.AddSingleton<IStartupFilter>(new SetRemoteIpAddressStartupFilter(TrustedPeer)));
        using var client = factory.CreateClient();

        (await StatusAsync(client, "203.0.113.10")).ShouldBe(HttpStatusCode.NotFound);
        (await StatusAsync(client, "203.0.113.10")).ShouldBe(HttpStatusCode.NotFound);
        using var third = await GetAsync(client, "203.0.113.10");

        third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        third.Headers.Contains("Retry-After").ShouldBeTrue();
        third.Headers.CacheControl!.NoStore.ShouldBeTrue();
        using var body = JsonDocument.Parse(await third.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("type").GetString()!.ShouldContain("rate-limited");
        (await StatusAsync(client, "203.0.113.20")).ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Lost_link_requests_are_limited_per_ip()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(
            settings: new Dictionary<string, string?>(database.Settings) { [$"{CustomerRateLimitOptions.SectionName}:LostLinkPermitLimit"] = "2" },
            configureServices: services => services.AddSingleton<IStartupFilter>(new SetRemoteIpAddressStartupFilter(TrustedPeer)));
        using var client = factory.CreateClient();

        async Task<HttpResponseMessage> PostAsync(string ip)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/customer/access-link")
            {
                Content = JsonContent.Create(new RequestNewAccessLinkRequest("nobody@example.com")),
            };
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", ip);
            return await client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        using (var first = await PostAsync("203.0.113.30"))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        using (var second = await PostAsync("203.0.113.30"))
        {
            second.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        using var third = await PostAsync("203.0.113.30");
        third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        third.Headers.Contains("Retry-After").ShouldBeTrue();
        third.Headers.CacheControl!.NoStore.ShouldBeTrue();
        using var other = await PostAsync("203.0.113.40");
        other.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Theory]
    [InlineData("TokenAccessPermitLimit")]
    [InlineData("TokenAccessWindowSeconds")]
    [InlineData("LostLinkPermitLimit")]
    [InlineData("LostLinkWindowSeconds")]
    public async Task Bad_customer_rate_limit_options_fail_startup(string property)
    {
        var key = $"{CustomerRateLimitOptions.SectionName}:{property}";
        await using var factory = new ApiFactory(settings: new Dictionary<string, string?> { [key] = "0" });

        var failure = StartupFailure.Capture(factory, () => factory.LogSink.Events);

        failure.Message.ShouldContain(key);
    }
}
