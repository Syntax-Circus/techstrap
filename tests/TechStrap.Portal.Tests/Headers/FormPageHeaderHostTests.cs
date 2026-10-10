using System.Net;

namespace TechStrap.Portal.Tests.Headers;

/// <summary>
/// D-045 addendum (09b): the four form pages of a product (contact, the received page, lost link and the suggest adapter) are never indexed and never stored, because their address can carry a visitor's name, email
/// address and subject, and the page shows what they typed. The rule is a header rule, so it applies to whatever the Portal answers on those paths: these tests assert the final response whatever its status (an
/// unknown product is a 404 here, and the page tests of 09b assert the same headers on the real 200s).
/// </summary>
public sealed class FormPageHeaderHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static string[] Header(HttpResponseMessage response, string name) => response.Headers.TryGetValues(name, out var values) ? [.. values] : [];

    [Theory]
    [InlineData("/p/probe/contact")]
    [InlineData("/p/probe/contact?subject=Printer&name=Jane&email=jane%40example.com")]
    [InlineData("/p/probe/contact/received?ref=x")]
    [InlineData("/p/probe/lost-link")]
    [InlineData("/p/probe/suggest?q=printer")]
    [InlineData("/P/Probe/CONTACT")]
    public async Task A_form_page_is_noindex_and_no_store_whatever_the_status(string path)
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        Header(response, "X-Robots-Tag").ShouldBe(["noindex"], path);
        Header(response, "Cache-Control").ShouldBe(["no-store"], path);
        Header(response, "Referrer-Policy").ShouldBe(["strict-origin-when-cross-origin"], "only the ticket pages send no referrer");
        Header(response, "X-Content-Type-Options").ShouldBe(["nosniff"]);
    }

    [Theory]
    [InlineData("/p/probe")]
    [InlineData("/p/probe/kb")]
    [InlineData("/not-found")]
    [InlineData("/error")]
    public async Task Other_pages_are_not_marked_noindex_by_the_form_page_rule(string path)
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        Header(response, "X-Robots-Tag").ShouldBeEmpty(path);
        Header(response, "Cache-Control").ShouldNotContain("no-store", path);
        response.StatusCode.ShouldNotBe(HttpStatusCode.InternalServerError, path);
    }

    // PHASE-09c: the help center's search page is never stored by a browser or a cache (any text can be asked and shown), but it is not a form page: it is not marked noindex by a header (the page does that itself when it has a query).
    [Theory]
    [InlineData("/p/probe/kb/search")]
    [InlineData("/p/probe/kb/search?q=x")]
    [InlineData("/P/Probe/KB/SEARCH/")]
    public async Task The_search_page_is_never_stored_but_is_not_marked_noindex_by_a_header_rule(string path)
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        Header(response, "Cache-Control").ShouldBe(["no-store"], path);
        Header(response, "X-Robots-Tag").ShouldBeEmpty(path);
    }
}
