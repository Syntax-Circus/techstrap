using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Intake;

namespace TechStrap.Api.Tests.Intake;

public sealed class IntakeRateLimitTests(TestPostgres postgres) : IAsyncLifetime
{
    private static readonly IPAddress TrustedPeer = IPAddress.Parse("192.0.2.5");
    private static readonly SubmitTicketRequest Sample = new("ada@example.com", "Ada", "Help", "Please help", null, null);

    private readonly string _storage = Path.Combine(Path.GetTempPath(), "techstrap-ratelimit-" + Guid.NewGuid().ToString("N"));

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, true);
        }

        return ValueTask.CompletedTask;
    }

    private async Task<(ApiFactory Factory, IntakeSeed Seed)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings)
        {
            ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test",
            ["Storage:Local:RootPath"] = _storage,
            ["RateLimiting:Intake:WebFormPermitLimit"] = "2",
            ["RateLimiting:Intake:PublicKeyPermitLimit"] = "2",
            ["RateLimiting:Intake:TrustedKeyPermitLimit"] = "3",
        };
        var factory = new ApiFactory(
            settings: settings,
            configureServices: services => services.AddSingleton<IStartupFilter>(new SetRemoteIpAddressStartupFilter(TrustedPeer)));
        var seed = await IntakeTestData.SeedAsync(factory.Services, TestContext.Current.CancellationToken);
        return (factory, seed);
    }

    private static Task<HttpResponseMessage> PostFormAsync(HttpClient client, string ip)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent("ada@example.com"), "email" },
            { new StringContent("Ada"), "name" },
            { new StringContent("Help"), "subject" },
            { new StringContent("Please help"), "body" },
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/public/products/orbitly/tickets") { Content = form };
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", ip);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> PostKeyAsync(HttpClient client, string key, string ip)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/intake/tickets") { Content = JsonContent.Create(Sample) };
        request.Headers.Add(HeaderNames.ApiKey, key);
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", ip);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpStatusCode> StatusAsync(Task<HttpResponseMessage> call)
    {
        using var response = await call;
        return response.StatusCode;
    }

    private static async Task ShouldBeRateLimitedProblemAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("type").GetString()!.ShouldContain("rate-limited");
    }

    [Fact]
    public async Task The_third_form_post_from_one_ip_is_429_problem_details_with_retry_after()
    {
        var (factory, _) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();

        (await StatusAsync(PostFormAsync(client, "203.0.113.10"))).ShouldBe(HttpStatusCode.Created);
        (await StatusAsync(PostFormAsync(client, "203.0.113.10"))).ShouldBe(HttpStatusCode.Created);
        using var third = await PostFormAsync(client, "203.0.113.10");

        await ShouldBeRateLimitedProblemAsync(third);
        third.Headers.Contains("Retry-After").ShouldBeTrue();
    }

    [Fact]
    public async Task Form_posts_from_another_ip_have_their_own_allowance()
    {
        var (factory, _) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();

        (await StatusAsync(PostFormAsync(client, "203.0.113.10"))).ShouldBe(HttpStatusCode.Created);
        (await StatusAsync(PostFormAsync(client, "203.0.113.10"))).ShouldBe(HttpStatusCode.Created);
        (await StatusAsync(PostFormAsync(client, "203.0.113.10"))).ShouldBe(HttpStatusCode.TooManyRequests);

        (await StatusAsync(PostFormAsync(client, "203.0.113.20"))).ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_public_key_is_limited_per_key_and_ip()
    {
        var (factory, seed) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();

        (await StatusAsync(PostKeyAsync(client, seed.OrbitlyPublic, "203.0.113.10"))).ShouldBe(HttpStatusCode.Created);
        (await StatusAsync(PostKeyAsync(client, seed.OrbitlyPublic, "203.0.113.10"))).ShouldBe(HttpStatusCode.Created);
        using var third = await PostKeyAsync(client, seed.OrbitlyPublic, "203.0.113.10");

        await ShouldBeRateLimitedProblemAsync(third);
        (await StatusAsync(PostKeyAsync(client, seed.OrbitlyPublic, "203.0.113.20"))).ShouldNotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task A_trusted_key_is_limited_per_key_regardless_of_ip()
    {
        var (factory, seed) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();

        (await StatusAsync(PostKeyAsync(client, seed.OrbitlyTrusted, "203.0.113.10"))).ShouldBe(HttpStatusCode.Created);
        (await StatusAsync(PostKeyAsync(client, seed.OrbitlyTrusted, "203.0.113.20"))).ShouldBe(HttpStatusCode.Created);
        (await StatusAsync(PostKeyAsync(client, seed.OrbitlyTrusted, "203.0.113.10"))).ShouldBe(HttpStatusCode.Created);
        using var fourth = await PostKeyAsync(client, seed.OrbitlyTrusted, "203.0.113.20");

        await ShouldBeRateLimitedProblemAsync(fourth);
    }

    [Fact]
    public async Task Junk_keys_are_limited_per_ip()
    {
        var (factory, _) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();

        (await StatusAsync(PostKeyAsync(client, "tsp_unknown_key_one_aaaaaaaa", "203.0.113.10"))).ShouldBe(HttpStatusCode.Unauthorized);
        (await StatusAsync(PostKeyAsync(client, "tsp_unknown_key_one_aaaaaaaa", "203.0.113.10"))).ShouldBe(HttpStatusCode.Unauthorized);
        (await StatusAsync(PostKeyAsync(client, "tsp_unknown_key_one_aaaaaaaa", "203.0.113.10"))).ShouldBe(HttpStatusCode.TooManyRequests);
    }
}
