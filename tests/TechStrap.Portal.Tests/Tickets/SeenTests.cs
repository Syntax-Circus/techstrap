using System.Net;

namespace TechStrap.Portal.Tests.Tickets;

/// <summary>
/// <see cref="Seen"/> is the yardstick of every "uniform 404" test, so it is pinned itself: it compares every header (an allow-list would let a new header slip through unseen) and ignores only the ones that
/// differ per request.
/// </summary>
public sealed class SeenTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task<Seen> SeenAsync(Action<HttpResponseMessage> configure)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("<h1>Page not found</h1>") };
        response.Headers.TryAddWithoutValidation("Cache-Control", "no-store");
        configure(response);
        return await Seen.OfAsync(response, 0, Ct);
    }

    [Theory]
    [InlineData("X-Product", "paperplane")]
    [InlineData("Link", "</p/paperplane>; rel=canonical")]
    [InlineData("Vary", "Accept-Encoding")]
    [InlineData("Strict-Transport-Security", "max-age=31536000")]
    [InlineData("Permissions-Policy", "camera=()")]
    public async Task A_header_that_is_not_on_any_list_makes_two_responses_differ(string name, string value)
    {
        var plain = await SeenAsync(_ => { });
        var extra = await SeenAsync(response => response.Headers.TryAddWithoutValidation(name, value));

        extra.Headers.ShouldNotBe(plain.Headers);
        Should.Throw<ShouldAssertException>(() => extra.ShouldBeTheNeutralNotFound(plain));
    }

    [Theory]
    [InlineData("Date", "Tue, 06 Oct 2026 10:00:00 GMT", "Tue, 06 Oct 2026 10:00:09 GMT")]
    [InlineData("X-Correlation-Id", "cid-one", "cid-two")]
    [InlineData("Set-Cookie", ".AspNetCore.Antiforgery.x=one; path=/", ".AspNetCore.Antiforgery.x=two; path=/")]
    [InlineData("Pragma", "no-cache", "no-cache, x")]
    public async Task A_header_that_varies_per_request_does_not(string name, string first, string second)
    {
        var one = await SeenAsync(response => response.Headers.TryAddWithoutValidation(name, first));
        var two = await SeenAsync(response => response.Headers.TryAddWithoutValidation(name, second));

        one.Headers.ShouldBe(two.Headers);
    }

    [Fact]
    public async Task The_framing_headers_do_not_count()
    {
        var plain = await SeenAsync(_ => { });
        var framed = await SeenAsync(response =>
        {
            response.Headers.TransferEncodingChunked = true;
            response.Content.Headers.ContentLength = 23;
        });

        framed.Headers.ShouldBe(plain.Headers);
    }

    [Fact]
    public async Task The_content_type_and_the_enhanced_navigation_marker_are_still_compared()
    {
        var plain = await SeenAsync(_ => { });
        var typed = await SeenAsync(response => response.Content.Headers.ContentType = new("text/plain"));
        var marked = await SeenAsync(response => response.Headers.TryAddWithoutValidation("blazor-enhanced-nav", "allow"));

        typed.Headers.ShouldNotBe(plain.Headers);
        marked.Headers.ShouldNotBe(plain.Headers);
        marked.HeadersWithoutEnhancedNav.ShouldBe(plain.HeadersWithoutEnhancedNav);
    }
}
