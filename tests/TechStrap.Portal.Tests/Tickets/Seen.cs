using System.Net;
using TechStrap.Portal.Tests.Forms;

namespace TechStrap.Portal.Tests.Tickets;

/// <summary>What a visitor can observe of a response: the status, the body and the headers that matter to the uniform 404 (everything that could tell one failure from another or carry a product's branding).</summary>
internal sealed record Seen(HttpStatusCode Status, string Body, string Headers, int ApiCalls)
{
    public static async Task<Seen> OfAsync(HttpResponseMessage response, int apiCalls, CancellationToken cancellationToken)
    {
        var headers = string.Join(
            "\n",
            response.Headers.Concat(response.Content.Headers)
                .Where(h => h.Key is "Content-Type" or "Cache-Control" or "Referrer-Policy" or "X-Robots-Tag" or "X-Content-Type-Options" or "X-Frame-Options" or "Content-Security-Policy" or "blazor-enhanced-nav")
                .OrderBy(h => h.Key, StringComparer.Ordinal)
                .Select(h => $"{h.Key}: {string.Join(",", h.Value)}"));
        return new Seen(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken), headers, apiCalls);
    }

    /// <summary>
    /// The neutral 404 a GET to <paramref name="path"/> gets, the page and the headers every other 404 on that kind of path has to equal. The default is a malformed ticket path; a product path is answered
    /// with the API saying the product does not exist.
    /// </summary>
    public static async Task<Seen> NeutralNotFoundAsync(CancellationToken cancellationToken, string path = "/t/x")
    {
        await using var factory = FormTestKit.Factory(product: false);
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/nope", HttpStatusCode.NotFound, "product-not-found", "No such product.");
        using var client = FormTestKit.Client(factory);
        using var response = await client.GetAsync(path, cancellationToken);
        return await OfAsync(response, factory.Api.Requests.Count, cancellationToken);
    }

    /// <summary>The headers without <c>blazor-enhanced-nav</c>, which only a 404 that Blazor itself renders carries (a status code the pass-through endpoint sets is re-executed into the same page without it).</summary>
    public string HeadersWithoutEnhancedNav => string.Join("\n", Headers.Split('\n').Where(line => !line.StartsWith("blazor-enhanced-nav:", StringComparison.Ordinal)));

    /// <summary>What makes a 404 the neutral one: the same status, the same page byte for byte, the same headers, and nothing of a product.</summary>
    public void ShouldBeTheNeutralNotFound(Seen neutral)
    {
        Status.ShouldBe(HttpStatusCode.NotFound);
        Body.ShouldNotContain("--ts-accent");
        Body.ShouldNotContain("ts-product-header");
        Body.ShouldNotContain("ts-product-footer");
        Body.ShouldBe(neutral.Body, "byte for byte");
        Headers.ShouldBe(neutral.Headers);
    }
}
