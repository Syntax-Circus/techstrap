using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Forms;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>
/// P09-T06 at the host: the received page. The ticket number comes from the protected, 10-minute <c>?ref=</c> value and from nothing else. A value that is missing, expired, tampered with or made by another
/// key ring shows the generic confirmation (never an error, never a number); the number and every other string on the page are encoded; the page is noindex and no-store.
/// </summary>
public sealed class ContactReceivedHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task<(HttpResponseMessage Response, string Html)> GetAsync(PortalFactory factory, string path)
    {
        using var client = FormTestKit.Client(factory);
        var response = await client.GetAsync(path, Ct);
        return (response, await response.Content.ReadAsStringAsync(Ct));
    }

    private static string ReferenceFor(PortalFactory factory, string number)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ReceivedReference>().Protect("paperplane", number);
    }

    [Fact]
    public async Task A_valid_reference_shows_the_number_and_what_happens_next()
    {
        await using var factory = FormTestKit.Factory();
        using var host = FormTestKit.Client(factory);
        var reference = ReferenceFor(factory, "PAP-42");

        var (response, html) = await GetAsync(factory, FormTestKit.ReceivedPath + "?ref=" + Uri.EscapeDataString(reference));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<title>Request received: Paperplane</title>");
        html.ShouldContain("<h1>We have received your request.</h1>");
        html.ShouldContain("Your ticket number is <strong class=\"ts-ticket-number\" id=\"ticket-number\">PAP-42</strong>");
        html.ShouldContain("We have emailed you a link. Use it to follow the conversation and reply.");
        html.ShouldContain("href=\"/p/paperplane/kb\">Browse help articles</a>");
        html.ShouldContain("href=\"/p/paperplane\">Back to Paperplane</a>");
        html.ShouldContain("href=\"/p/paperplane/lost-link\">Can&#x27;t find the email?</a>");
        html.ShouldNotContain("/t/", Case.Sensitive, "the ticket link is never on this page: it is proved by owning the mailbox");
    }

    [Fact]
    public async Task The_page_is_noindex_and_no_store()
    {
        await using var factory = FormTestKit.Factory();

        var (response, _) = await GetAsync(factory, FormTestKit.ReceivedPath);

        response.Headers.GetValues("X-Robots-Tag").Single().ShouldBe("noindex");
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
    }

    [Theory]
    [InlineData("")]
    [InlineData("?ref=")]
    [InlineData("?ref=PAP-42")]
    [InlineData("?ref=CfDJ8NotARealProtectedValue")]
    [InlineData("?ref=%00%00")]
    public async Task A_missing_or_invalid_reference_shows_the_generic_confirmation_and_no_number(string query)
    {
        await using var factory = FormTestKit.Factory();

        var (response, html) = await GetAsync(factory, FormTestKit.ReceivedPath + query);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("We have received your request.");
        html.ShouldContain("We have emailed you a link to follow it. Check your inbox and your spam folder.");
        html.ShouldNotContain("ts-ticket-number");
        html.ShouldNotContain("PAP-42");
    }

    [Fact]
    public async Task A_tampered_reference_shows_the_generic_confirmation()
    {
        await using var factory = FormTestKit.Factory();
        using var host = FormTestKit.Client(factory);
        var reference = ReferenceFor(factory, "PAP-42");
        var tampered = reference[..^4] + (reference[^4] == 'A' ? "B" : "A") + reference[^3..];

        var (response, html) = await GetAsync(factory, FormTestKit.ReceivedPath + "?ref=" + Uri.EscapeDataString(tampered));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldNotContain("ts-ticket-number");
        html.ShouldContain("Check your inbox and your spam folder.");
    }

    [Fact]
    public async Task A_reference_older_than_ten_minutes_shows_the_generic_confirmation()
    {
        // The clock that made the reference ran an hour behind the real one, so the reference expired 50 minutes ago (the unprotect check uses the real clock).
        var past = new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(-1));
        await using var factory = FormTestKit.Factory(configure: services => services.AddSingleton<TimeProvider>(past));
        using var host = FormTestKit.Client(factory);
        var reference = ReferenceFor(factory, "PAP-42");

        var (_, html) = await GetAsync(factory, FormTestKit.ReceivedPath + "?ref=" + Uri.EscapeDataString(reference));

        html.ShouldNotContain("ts-ticket-number");
        html.ShouldContain("Check your inbox and your spam folder.");
    }

    [Fact]
    public async Task A_reference_made_for_one_product_shows_the_generic_confirmation_on_another()
    {
        await using var factory = FormTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/otherproduct", new PublicProductDto("otherproduct", "Otherproduct", null, "#F59E0B", "#000000", "#9D6507"));
        using var host = FormTestKit.Client(factory);
        var reference = ReferenceFor(factory, "PAP-42");

        var (_, controlHtml) = await GetAsync(factory, FormTestKit.ReceivedPath + "?ref=" + Uri.EscapeDataString(reference));
        var (response, html) = await GetAsync(factory, "/p/otherproduct/contact/received?ref=" + Uri.EscapeDataString(reference));

        controlHtml.ShouldContain("PAP-42", Case.Sensitive, "control: its own product shows the number");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldNotContain("ts-ticket-number");
        html.ShouldNotContain("PAP-42");
        html.ShouldContain("Check your inbox and your spam folder.");
    }

    [Fact]
    public async Task The_page_of_an_unknown_product_is_the_uniform_404_even_with_a_good_reference()
    {
        await using var factory = FormTestKit.Factory();
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/nope", HttpStatusCode.NotFound, "product-not-found", "No such product.");
        using var host = FormTestKit.Client(factory);
        var reference = ReferenceFor(factory, "PAP-42");

        var (response, html) = await GetAsync(factory, "/p/nope/contact/received?ref=" + Uri.EscapeDataString(reference));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        html.ShouldNotContain("PAP-42");
        html.ShouldContain("Page not found");
    }

    [Fact]
    public async Task When_the_api_cannot_be_asked_the_page_says_so_calmly()
    {
        await using var factory = FormTestKit.Factory("Production", product: false);
        factory.Api.OnStatus(HttpMethod.Get, "/api/public/products/paperplane", HttpStatusCode.ServiceUnavailable);

        var (response, html) = await GetAsync(factory, FormTestKit.ReceivedPath);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        html.ShouldContain("This page could not be loaded.");
    }
}
