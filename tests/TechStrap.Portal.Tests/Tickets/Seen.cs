using System.Net;
using TechStrap.Portal.Tests.Forms;

namespace TechStrap.Portal.Tests.Tickets;

/// <summary>
/// What a visitor can observe of a response: the status, the body and EVERY header except the few that differ per request by design. A header the Portal adds one day to one kind of 404 and not to another is
/// then caught without anyone remembering to list it.
/// </summary>
internal sealed record Seen(HttpStatusCode Status, string Body, string Headers, int ApiCalls)
{
    /// <summary>
    /// The headers that vary per request by design: the clock, the request's own correlation id and the framing; and the two the framework's antiforgery step adds only to a response that rendered a form (the
    /// antiforgery <c>Set-Cookie</c> and <c>Pragma: no-cache</c>), which a post to a product that vanished after its form was served still carries. Nothing is leaked: a visitor holding that form already knew the product.
    /// </summary>
    private static readonly HashSet<string> PerRequest = new(["Date", "X-Correlation-Id", "Content-Length", "Transfer-Encoding", "Set-Cookie", "Pragma"], StringComparer.OrdinalIgnoreCase);

    private const string AntiforgeryCookiePrefix = ".AspNetCore.Antiforgery.";

    /// <summary>Every cookie except the antiforgery one, by name only (a value may vary): a session or tracking cookie on one kind of 404 and not on another must be seen.</summary>
    private static IEnumerable<string> CookieNames(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.Where(c => !c.StartsWith(AntiforgeryCookiePrefix, StringComparison.Ordinal)).Select(c => "Set-Cookie: " + c.Split('=')[0].Trim())
            : [];

    public static async Task<Seen> OfAsync(HttpResponseMessage response, int apiCalls, CancellationToken cancellationToken)
    {
        var headers = string.Join(
            "\n",
            response.Headers.Concat(response.Content.Headers)
                .Where(h => !PerRequest.Contains(h.Key))
                .OrderBy(h => h.Key, StringComparer.Ordinal)
                .Select(h => $"{h.Key}: {string.Join(",", h.Value)}")
                .Concat(CookieNames(response).Order(StringComparer.Ordinal)));
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
