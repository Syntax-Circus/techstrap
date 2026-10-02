using System.Net;
using System.Text.Json;

namespace TechStrap.Api.Tests;

public sealed class HealthEndpointTests(TestPostgres postgres)
{
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2;Pooling=false";

    private static IReadOnlyDictionary<string, string?> ConnectionString(string value) =>
        new Dictionary<string, string?> { ["ConnectionStrings:TechStrap"] = value };

    private static async Task<string> ReadStatusAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("status").GetString()!;
    }

    [Fact]
    public async Task Live_returns_200_without_touching_the_database()
    {
        await using var factory = new ApiFactory(settings: ConnectionString(UnreachableDatabase));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadStatusAsync(response)).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Ready_returns_503_when_the_database_is_unreachable()
    {
        await using var factory = new ApiFactory(settings: ConnectionString(UnreachableDatabase));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await ReadStatusAsync(response)).ShouldBe("Unhealthy");
    }

    [Fact]
    public async Task Ready_returns_200_when_postgres_is_reachable()
    {
        await using var factory = new ApiFactory(settings: ConnectionString(await postgres.CreateDatabaseAsync()));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadStatusAsync(response)).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Responses_echo_the_correlation_id_header()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", "corr-from-caller");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.GetValues("X-Correlation-Id").ShouldBe(["corr-from-caller"]);
    }

    [Fact]
    public async Task The_request_log_line_carries_the_correlation_id()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", "corr-in-log");

        await client.SendAsync(request, TestContext.Current.CancellationToken);

        factory.LogSink.Events.ShouldContain(logEvent =>
            logEvent.MessageTemplate.Text.Contains("HTTP", StringComparison.Ordinal)
            && logEvent.Properties.ContainsKey("CorrelationId")
            && logEvent.Properties["CorrelationId"].ToString().Contains("corr-in-log", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_oversized_caller_supplied_correlation_id_does_not_break_the_request()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", new string('x', 4000));

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("X-Correlation-Id").ShouldBeTrue();
    }

    [Fact]
    public async Task Responses_carry_security_headers()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
    }

    [Fact]
    public async Task The_OpenAPI_document_is_served_anonymously()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("\"openapi\"");
    }
}
