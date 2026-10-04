using System.Net;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Http;

namespace TechStrap.Api.Tests.Customer;

/// <summary>Review focus: every failure mode on the customer route is the same 404, byte for byte.</summary>
public sealed class CustomerUniformNotFoundTests(TestPostgres postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_failure_mode_returns_the_same_404_bytes()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: new Dictionary<string, string?>(database.Settings));
        var seed = await CustomerTestData.SeedAsync(factory, Ct);
        using var client = factory.CreateClient();
        var ignored = new[] { "Date", "X-Correlation-Id", "X-Request-Id" };

        var shapes = new List<string>();
        foreach (var token in new string?[]
        {
            null, "", "garbage", new string('x', 200), "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            seed.RevokedToken, seed.ExpiredToken, seed.ErasedRequesterToken,
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/customer/ticket");
            if (token is not null)
            {
                request.Headers.TryAddWithoutValidation(HeaderNames.TicketToken, token);
            }

            using var response = await client.SendAsync(request, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            var headers = response.Headers.Concat(response.Content.Headers)
                .Where(h => !ignored.Contains(h.Key, StringComparer.OrdinalIgnoreCase))
                .OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
                .Select(h => $"{h.Key}={string.Join(",", h.Value)}");
            shapes.Add($"{(int)response.StatusCode}|{await response.Content.ReadAsStringAsync(Ct)}|{string.Join(";", headers)}");
        }

        shapes.Distinct().Count().ShouldBe(1, string.Join("\n", shapes));
        shapes[0].ShouldContain("not-found");
    }
}
