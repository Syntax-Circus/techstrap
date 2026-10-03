using System.Net;
using System.Net.Http.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Intake;

namespace TechStrap.Api.Tests.Intake;

public sealed class ApiKeyAuthTests(TestPostgres postgres) : IAsyncLifetime
{
    private static readonly SubmitTicketRequest Sample = new("ada@example.com", "Ada", "Help", "Please help", null, null);

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

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/intake/tickets") { Content = JsonContent.Create(Sample) };
        if (key is not null)
        {
            request.Headers.Add(HeaderNames.ApiKey, key);
        }

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<(ApiFactory Factory, ApiTestDatabase Database, IntakeSeed Seed)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings)
        {
            ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test",
            ["Storage:Local:RootPath"] = _storage,
        };
        var factory = new ApiFactory(settings: settings);
        var seed = await IntakeTestData.SeedAsync(factory.Services, TestContext.Current.CancellationToken);
        return (factory, database, seed);
    }

    [Fact]
    public async Task A_request_without_a_key_is_401_with_no_body()
    {
        var (factory, _, _) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_key_is_401_with_no_body()
    {
        var (factory, _, _) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, "tsk_not-a-real-key");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_revoked_key_is_401()
    {
        var (factory, _, seed) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, seed.OrbitlyRevoked);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_key_whose_product_is_deactivated_is_401()
    {
        var (factory, _, seed) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, seed.DormantTrusted);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_agent_bearer_token_is_not_accepted_on_intake()
    {
        var (factory, _, _) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com"));

        using var response = await PostAsync(client, null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_api_key_is_not_accepted_on_agent_routes()
    {
        var (factory, _, seed) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/products");
        request.Headers.Add(HeaderNames.ApiKey, seed.OrbitlyTrusted);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_key_creates_tickets_only_for_its_own_product()
    {
        var (factory, database, seed) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, seed.PaperplaneTrusted);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = (await response.Content.ReadFromJsonAsync<SubmitTicketResponse>(TestContext.Current.CancellationToken))!;
        body.TicketNumber.ShouldStartWith("PPL-");
        (await database.ScalarAsync<Guid>($"SELECT product_id FROM tickets WHERE number = '{body.TicketNumber}'")).ShouldBe(seed.Paperplane.Id);
    }

    [Fact]
    public async Task All_401_responses_are_identical()
    {
        var (factory, _, seed) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();
        var ignored = new[] { "Date", "X-Correlation-Id", "X-Request-Id" };

        var shapes = new List<string>();
        foreach (var key in new string?[] { null, "tsk_not-a-real-key", seed.OrbitlyRevoked })
        {
            using var response = await PostAsync(client, key);
            var headers = response.Headers.Concat(response.Content.Headers)
                .Where(h => !ignored.Contains(h.Key, StringComparer.OrdinalIgnoreCase))
                .OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
                .Select(h => $"{h.Key}={string.Join(",", h.Value)}");
            shapes.Add($"{(int)response.StatusCode}|{await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}|{string.Join(";", headers)}");
        }

        shapes.Distinct().Count().ShouldBe(1, string.Join("\n", shapes));
    }
}
