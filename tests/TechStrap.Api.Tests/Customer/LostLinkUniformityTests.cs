using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using TechStrap.Api.Options;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Api.Tests.Customer;

public sealed class LostLinkUniformityTests(TestPostgres postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly HashSet<string> VolatileHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Date", "X-Correlation-Id", "X-Request-Id", "Request-Id", "traceparent", "Server-Timing",
    };

    private async Task<(ApiFactory Factory, ApiTestDatabase Database)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings)
        {
            ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test",
            ["LostLink:PerAddressLimit"] = "20",
            [$"{CustomerRateLimitOptions.SectionName}:LostLinkPermitLimit"] = "1000",
        };
        var factory = new ApiFactory(settings: settings);
        await CustomerTestData.SeedAsync(factory, Ct);
        return (factory, database);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/customer/access-link", new RequestNewAccessLinkRequest(email), Ct);

    private static async Task<(HttpStatusCode Status, byte[] Body, string[] Headers)> SnapshotAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsByteArrayAsync(Ct);
        var headers = response.Headers.Concat(response.Content.Headers)
            .Where(h => !VolatileHeaders.Contains(h.Key))
            .Select(h => $"{h.Key.ToLowerInvariant()}={string.Join(",", h.Value)}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        return (response.StatusCode, body, headers);
    }

    private static async Task<TimeSpan> MedianAsync(HttpClient client, string email)
    {
        var samples = new List<TimeSpan>();
        for (var i = 0; i < 10; i++)
        {
            var watch = Stopwatch.StartNew();
            using var response = await PostAsync(client, email);
            watch.Stop();
            response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            samples.Add(watch.Elapsed);
        }

        return samples.Order().ElementAt(5);
    }

    [Fact]
    public async Task Known_and_unknown_addresses_are_indistinguishable()
    {
        var (factory, database) = await StartAsync();
        await using var owned = factory;
        using var client = factory.CreateClient();

        using var knownResponse = await PostAsync(client, "ann@example.com");
        using var unknownResponse = await PostAsync(client, "nobody@example.com");
        var known = await SnapshotAsync(knownResponse);
        var unknown = await SnapshotAsync(unknownResponse);

        known.Status.ShouldBe(HttpStatusCode.Accepted);
        unknown.Status.ShouldBe(known.Status);
        known.Body.ShouldBeEmpty();
        unknown.Body.ShouldBe(known.Body);
        unknown.Headers.ShouldBe(known.Headers);
        known.Headers.ShouldContain(h => h.StartsWith("cache-control=", StringComparison.Ordinal) && h.Contains("no-store"));

        // Coarse timing parity: documents intent, not a side-channel proof. One warm-up request each, then 10 timed.
        using var warmKnown = await PostAsync(client, "ann@example.com");
        using var warmUnknown = await PostAsync(client, "nobody@example.com");
        var knownMedian = await MedianAsync(client, "ann@example.com");
        var unknownMedian = await MedianAsync(client, "nobody@example.com");
        (knownMedian - unknownMedian).Duration().ShouldBeLessThan(TimeSpan.FromMilliseconds(250));

        // Prove the known path really sent while timing: 1 initial + 1 warm-up + 10 timed = 12 rows, all under the limit of 20.
        (await database.ScalarAsync<long>(
            "SELECT count(*) FROM email_outbox WHERE kind = 'access-links' AND to_address = 'ann@example.com'")).ShouldBe(12);
    }

    [Fact]
    public async Task The_email_goes_only_to_the_requesters_own_address()
    {
        var (factory, database) = await StartAsync();
        await using var owned = factory;
        using var client = factory.CreateClient();

        using (await PostAsync(client, "Ann@Example.com"))
        {
        }

        using (await PostAsync(client, "nobody@example.com"))
        {
        }

        (await database.ScalarAsync<long>("SELECT count(*) FROM email_outbox")).ShouldBe(1);
        (await database.ScalarAsync<long>(
            "SELECT count(*) FROM email_outbox WHERE kind = 'access-links' AND to_address = 'ann@example.com'")).ShouldBe(1);
    }
}
