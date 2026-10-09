using System.Net;

namespace TechStrap.Api.Tests.Hosting;

/// <summary>Review SR-07: the Api configures no CORS policy, so a browser on another origin can never read a response. Neither a simple request nor a preflight is answered with a CORS header.</summary>
public sealed class CorsAbsenceHostTests
{
    private static readonly string[] CorsHeaders = ["Access-Control-Allow-Origin", "Access-Control-Allow-Credentials", "Access-Control-Allow-Methods", "Access-Control-Allow-Headers"];

    [Theory(Timeout = 60_000)]
    [InlineData("/health/live")]
    [InlineData("/openapi/v1.json")]
    [InlineData("/api/public/products")]
    [InlineData("/api/agents/me")]
    public async Task No_response_carries_an_Access_Control_Allow_Origin_header(string path)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Options })
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add("Origin", "https://evil.example");
            if (method == HttpMethod.Options)
            {
                request.Headers.Add("Access-Control-Request-Method", "GET");
            }

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            response.StatusCode.ShouldNotBe(HttpStatusCode.InternalServerError, $"{method} {path}");
            foreach (var header in CorsHeaders)
            {
                response.Headers.Contains(header).ShouldBeFalse($"{method} {path} carries {header}");
            }

            response.Headers.Vary.ShouldNotContain("Origin", $"{method} {path} varies on Origin");
        }
    }
}
