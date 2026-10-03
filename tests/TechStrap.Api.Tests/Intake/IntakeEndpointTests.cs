using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using TechStrap.Api.Startup;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Intake;

namespace TechStrap.Api.Tests.Intake;

public sealed class IntakeEndpointTests(TestPostgres postgres) : IAsyncLifetime
{
    private readonly string _storage = Path.Combine(Path.GetTempPath(), "techstrap-intake-" + Guid.NewGuid().ToString("N"));

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, true);
        }

        return ValueTask.CompletedTask;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string key, HttpContent content, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/intake/tickets") { Content = content };
        request.Headers.Add(HeaderNames.ApiKey, key);
        if (idempotencyKey is not null)
        {
            request.Headers.Add(HeaderNames.IdempotencyKey, idempotencyKey);
        }

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<(ApiFactory Factory, ApiTestDatabase Database, IntakeSeed Seed)> StartAsync(bool kestrel = false)
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings)
        {
            ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test",
            ["Storage:Local:RootPath"] = _storage,
        };
        var factory = new ApiFactory(settings: settings);
        if (kestrel)
        {
            factory.UseKestrel(0); // TestServer has no IHttpMaxRequestBodySizeFeature, so RequestSizeLimit needs the real server.
        }

        var seed = await IntakeTestData.SeedAsync(factory.Services, TestContext.Current.CancellationToken);
        return (factory, database, seed);
    }

    [Fact]
    public async Task A_trusted_key_submission_returns_201_with_the_number_and_view_url_and_no_store()
    {
        var (factory, database, seed) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();
        var request = new SubmitTicketRequest("ada@example.com", "Ada", "Help", "Please help", "u-1", new Dictionary<string, string> { ["plan"] = "pro" });

        using var response = await PostAsync(client, seed.OrbitlyTrusted, JsonContent.Create(request));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var body = (await response.Content.ReadFromJsonAsync<SubmitTicketResponse>(TestContext.Current.CancellationToken))!;
        body.TicketNumber.ShouldBe("ORB-1");
        body.ViewUrl.ShouldStartWith("https://help.test/t/");
        body.Warnings.ShouldBeEmpty();
        (await database.ScalarAsync<bool>("SELECT metadata_trusted FROM tickets WHERE number = 'ORB-1'")).ShouldBeTrue();
        (await database.ScalarAsync<string>("SELECT external_user_ref FROM requesters")).ShouldBe("u-1");
    }

    [Fact]
    public async Task A_public_key_submission_drops_the_external_ref_with_a_warning()
    {
        var (factory, database, seed) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();
        var request = new SubmitTicketRequest("ada@example.com", "Ada", "Help", "Please help", "u-1", new Dictionary<string, string> { ["plan"] = "pro" });

        using var response = await PostAsync(client, seed.OrbitlyPublic, JsonContent.Create(request));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = (await response.Content.ReadFromJsonAsync<SubmitTicketResponse>(TestContext.Current.CancellationToken))!;
        body.Warnings.ShouldBe(["external-user-ref-ignored"]);
        (await database.ScalarAsync<bool>("SELECT metadata_trusted FROM tickets WHERE number = 'ORB-1'")).ShouldBeFalse();
        (await database.ScalarAsync<long>("SELECT count(*) FROM requesters WHERE external_user_ref IS NOT NULL")).ShouldBe(0);
    }

    [Fact]
    public async Task An_invalid_email_is_a_400_with_the_email_target()
    {
        var (factory, _, seed) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();
        var request = new SubmitTicketRequest("not-an-email", "Ada", "Help", "Please help", null, null);

        using var response = await PostAsync(client, seed.OrbitlyTrusted, JsonContent.Create(request));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var problem = System.Text.Json.JsonSerializer.Deserialize<ValidationProblemDetails>(json)!;
        problem.Errors.ShouldContainKey("email");
        json.ShouldContain("email-invalid");
    }

    [Fact]
    public async Task A_repeated_idempotency_key_returns_the_first_response_and_one_ticket()
    {
        var (factory, database, seed) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();
        var request = new SubmitTicketRequest("ada@example.com", "Ada", "Help", "Please help", null, null);

        using var first = await PostAsync(client, seed.OrbitlyTrusted, JsonContent.Create(request), "abc");
        using var second = await PostAsync(client, seed.OrbitlyTrusted, JsonContent.Create(request), "abc");

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        var a = (await first.Content.ReadFromJsonAsync<SubmitTicketResponse>(TestContext.Current.CancellationToken))!;
        var b = (await second.Content.ReadFromJsonAsync<SubmitTicketResponse>(TestContext.Current.CancellationToken))!;
        b.TicketNumber.ShouldBe(a.TicketNumber);
        b.Warnings.ShouldBe(a.Warnings);
        a.ViewUrl.ShouldStartWith("https://help.test/t/");
        b.ViewUrl.ShouldStartWith("https://help.test/t/");
        (await database.ScalarAsync<long>("SELECT count(*) FROM tickets")).ShouldBe(1);
    }

    [Fact]
    public async Task An_oversized_json_body_is_413_problem_details()
    {
        var (factory, _, seed) = await StartAsync(kestrel: true);
        await using var _f = factory;
        using var client = factory.CreateClient();
        var content = new StringContent(new string('x', (int)IntakeRequestLimits.JsonBodyBytes + 1), Encoding.UTF8, "application/json");

        using var response = await PostAsync(client, seed.OrbitlyTrusted, content);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))!;
        problem.Type.ShouldBe("request-too-large");
    }
}
