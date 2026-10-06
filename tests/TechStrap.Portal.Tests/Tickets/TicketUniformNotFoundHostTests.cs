using System.Net;
using TechStrap.Portal.Tests.Forms;

namespace TechStrap.Portal.Tests.Tickets;

/// <summary>
/// Review Focus 2: a malformed, unknown, expired or revoked token, and a wrong attachment id or a wrong attachment token, all answer the identical 404: the same status, the same neutral page byte for byte and the same
/// headers, so nothing tells a visitor which part failed. A malformed token never reaches the API.
/// </summary>
public sealed class TicketUniformNotFoundHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task<Seen> GetAsync(string path, Action<PortalFactory>? configure = null)
    {
        await using var factory = FormTestKit.Factory(product: false);
        configure?.Invoke(factory);
        using var client = FormTestKit.Client(factory);
        using var response = await client.GetAsync(path, Ct);
        return await Seen.OfAsync(response, factory.Api.Requests.Count, Ct);
    }

    private static Action<PortalFactory> ApiAnswers404(string type, string detail) => factory =>
    {
        factory.Api.OnProblem(HttpMethod.Get, TicketTestKit.TicketApi, HttpStatusCode.NotFound, type, detail);
        factory.Api.OnProblem(HttpMethod.Get, TicketTestKit.AttachmentApi, HttpStatusCode.NotFound, type, detail);
    };

    [Fact]
    public async Task Every_way_to_fail_to_find_a_ticket_is_the_same_page()
    {
        var malformed = await GetAsync("/t/x");
        var tooLong = await GetAsync("/t/" + TicketTestKit.Token + "x");
        var unknown = await GetAsync(TicketTestKit.Path, ApiAnswers404("token-not-found", "No such token."));
        var expired = await GetAsync(TicketTestKit.Path, ApiAnswers404("token-expired", "This token expired yesterday."));
        var revoked = await GetAsync(TicketTestKit.Path, ApiAnswers404("token-revoked", "An agent revoked this token."));
        var unconfigured = await GetAsync(TicketTestKit.Path);

        foreach (var seen in new[] { malformed, tooLong, unknown, expired, revoked, unconfigured })
        {
            seen.Status.ShouldBe(HttpStatusCode.NotFound);
            seen.Body.ShouldContain("Page not found");
            seen.Body.ShouldNotContain("expired");
            seen.Body.ShouldNotContain("revoked");
            seen.Body.ShouldNotContain("token", Case.Insensitive);
            seen.Body.ShouldBe(malformed.Body, "byte for byte");
            seen.Headers.ShouldBe(malformed.Headers);
        }
    }

    [Fact]
    public async Task The_page_shows_no_product_branding_and_no_ticket_text()
    {
        var seen = await GetAsync(TicketTestKit.Path, ApiAnswers404("token-expired", "x"));

        seen.Body.ShouldNotContain("--ts-accent");
        seen.Body.ShouldNotContain("ts-product-header");
        seen.Body.ShouldNotContain("PAP-");
        seen.Headers.ShouldContain("X-Robots-Tag: noindex");
        seen.Headers.ShouldContain("Cache-Control: no-store");
        seen.Headers.ShouldContain("Referrer-Policy: no-referrer");
    }

    [Theory]
    [InlineData("/t/x")]
    [InlineData("/t/%20")]
    [InlineData("/t/AbC-_0123456789AbC-_0123456789AbC-_0123456")]
    [InlineData("/t/AbC-_0123456789AbC-_0123456789AbC-_012345678")]
    [InlineData("/t/AbC-_0123456789AbC-_0123456789AbC-_0123456!")]
    [InlineData("/t/..%2F..%2Fadmin")]
    public async Task A_malformed_token_is_a_404_and_the_api_is_never_asked(string path)
    {
        var seen = await GetAsync(path);

        seen.Status.ShouldBe(HttpStatusCode.NotFound);
        seen.ApiCalls.ShouldBe(0);
    }

    [Fact]
    public async Task A_wrong_attachment_is_the_same_page_as_a_wrong_ticket_whatever_part_was_wrong()
    {
        var wrongTicket = await GetAsync(TicketTestKit.Path, ApiAnswers404("token-not-found", "x"));
        var badToken = await GetAsync($"/t/short/attachments/{TicketTestKit.AttachmentId}");
        var badId = await GetAsync($"/t/{TicketTestKit.Token}/attachments/not-a-guid");
        var badIdTraversal = await GetAsync($"/t/{TicketTestKit.Token}/attachments/..%2F..%2Fx");
        var noDashes = await GetAsync($"/t/{TicketTestKit.Token}/attachments/{TicketTestKit.AttachmentId:N}");
        var wrongId = await GetAsync($"/t/{TicketTestKit.Token}/attachments/{TicketTestKit.AttachmentId}", ApiAnswers404("attachment-not-found", "No such attachment on this ticket."));
        var otherTicket = await GetAsync($"/t/{TicketTestKit.OtherToken}/attachments/{TicketTestKit.AttachmentId}", ApiAnswers404("attachment-of-another-ticket", "Belongs to another ticket."));

        foreach (var (seen, name) in new[] { (badToken, "badToken"), (badId, "badId"), (badIdTraversal, "badIdTraversal"), (noDashes, "noDashes"), (wrongId, "wrongId"), (otherTicket, "otherTicket") })
        {
            seen.Status.ShouldBe(HttpStatusCode.NotFound);
            seen.Body.ShouldBe(wrongTicket.Body, "byte for byte");
            seen.HeadersWithoutEnhancedNav.ShouldBe(wrongTicket.HeadersWithoutEnhancedNav, name);
        }

        badToken.ApiCalls.ShouldBe(0);
        badId.ApiCalls.ShouldBe(0);
        badIdTraversal.ApiCalls.ShouldBe(0);
        noDashes.ApiCalls.ShouldBe(0);
    }
}
