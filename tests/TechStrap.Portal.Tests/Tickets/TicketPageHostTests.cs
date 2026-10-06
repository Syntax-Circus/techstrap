using System.Net;
using System.Text.RegularExpressions;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;
using TechStrap.Portal.Tests.Forms;

namespace TechStrap.Portal.Tests.Tickets;

/// <summary>
/// P09-T08 and T23 at the host: the ticket page. A valid token shows the public conversation, themed with the ticket's own product (loaded from the ticket's <c>ProductKey</c>) or, when that product is inactive
/// or unknown, in the neutral theme: never a 404. The status is in the customer's words, a Closed ticket carries the follow-up notice, an agent shows as the API named them and the customer as "You", the message
/// bodies are the API's sanitised HTML rendered as it came and every other string is encoded. Review Focus 1: the token is a header on the API call and in the page's own links and nowhere else.
/// </summary>
public sealed class TicketPageHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string Visitor = FormTestKit.Visitor;

    private static async Task<(HttpResponseMessage Response, string Html)> GetAsync(PortalFactory factory, string? path = null)
    {
        using var client = FormTestKit.Client(factory);
        var response = await client.GetAsync(path ?? TicketTestKit.Path, Ct);
        return (response, await response.Content.ReadAsStringAsync(Ct));
    }

    private static PortalFactory Host() => TicketTestKit.Factory();

    [Fact]
    public async Task A_valid_token_shows_the_ticket_in_its_products_theme()
    {
        await using var factory = Host();

        var (response, html) = await GetAsync(factory);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<title>Ticket PAP-42</title>");
        html.ShouldContain("--ts-accent:#F59E0B");
        html.ShouldContain("class=\"ts-product-name\"");
        html.ShouldContain(">Paperplane</a>");
        html.ShouldContain("<span class=\"ts-ticket-number\">PAP-42</span>");
        html.ShouldContain("<span class=\"ts-ticket-subject\">Printer jam</span>");
        html.ShouldContain("<strong>In progress</strong>");
        html.ShouldContain("href=\"/p/paperplane/lost-link\"");
    }

    [Fact]
    public async Task The_api_is_called_with_the_token_as_a_header_only_and_the_visitors_address_and_then_asked_for_the_products_theme()
    {
        await using var factory = Host();

        await GetAsync(factory);

        factory.Api.Requests.Select(r => r.Path).ShouldBe([TicketTestKit.TicketApi, "/api/public/products/paperplane"]);
        var ticket = factory.Api.Requests[0];
        ticket.TicketToken.ShouldBe(TicketTestKit.Token);
        ticket.Client.ShouldBe(ApiClientNames.Read);
        ticket.Query.ShouldBeEmpty();
        ticket.Path.ShouldNotContain(TicketTestKit.Token);
        factory.Api.Requests[1].TicketToken.ShouldBeNull("the product call is anonymous");
        factory.Api.AssertEveryCallBore(Visitor);
    }

    [Fact]
    public async Task The_conversation_shows_each_author_each_time_and_each_message_in_order()
    {
        await using var factory = Host();

        var (_, html) = await GetAsync(factory);

        html.ShouldContain("<strong class=\"ts-message-author\">You</strong>");
        html.ShouldContain("<strong class=\"ts-message-author\">Sam from Paperplane Support</strong>");
        html.ShouldContain("<strong class=\"ts-message-author\">Update</strong>");
        html.ShouldContain("<time datetime=\"2026-10-01T09:00:00Z\">1 Oct 2026 09:00 UTC</time>");
        html.ShouldContain("<time datetime=\"2026-10-01T10:30:00Z\">1 Oct 2026 10:30 UTC</time>");
        var order = new[] { "It jams every time.", "Try <b>this</b> first.", "Status changed." }.Select(text => html.IndexOf(text, StringComparison.Ordinal)).ToArray();
        order.ShouldAllBe(i => i > 0);
        order.ShouldBe([.. order.Order()], "oldest first, as the API gave them");
        html.ShouldContain("ts-message ts-message-own");
    }

    [Fact]
    public async Task No_agent_email_id_or_avatar_is_on_the_page()
    {
        await using var factory = Host();

        var (_, html) = await GetAsync(factory);

        html.ShouldNotContain("@");
        html.ShouldNotContain("avatar", Case.Insensitive);
        html.ShouldNotContain("aaaaaaaa-0000", Case.Insensitive, "message ids are not rendered");
    }

    [Fact]
    public async Task A_message_body_is_the_apis_sanitised_html_rendered_as_it_came()
    {
        await using var factory = TicketTestKit.Factory(TicketTestKit.Ticket(agentBody: "<p>Hello <b>there</b>, see <a href=\"https://help.example.com/a\">this</a>.</p><ul><li>one</li></ul>"));

        var (_, html) = await GetAsync(factory);

        html.ShouldContain("<div class=\"ts-message-body\"><p>Hello <b>there</b>, see <a href=\"https://help.example.com/a\">this</a>.</p><ul><li>one</li></ul></div>");
    }

    [Fact]
    public async Task Every_other_string_is_encoded_the_subject_the_author_and_the_file_name()
    {
        const string Evil = "<img src=x onerror=alert(1)>";
        await using var factory = TicketTestKit.Factory(TicketTestKit.Ticket(subject: Evil + "Subject", agentName: Evil + "Sam", fileName: Evil + ".txt"));

        var (_, html) = await GetAsync(factory);

        html.ShouldNotContain("<img src=x");
        html.ShouldContain("&lt;img src=x onerror=alert(1)&gt;Subject");
        html.ShouldContain("&lt;img src=x onerror=alert(1)&gt;Sam");
        html.ShouldContain("&lt;img src=x onerror=alert(1)&gt;.txt");
    }

    [Fact]
    public async Task An_attachment_is_a_link_to_the_portals_pass_through_with_its_name_and_size()
    {
        await using var factory = Host();

        var (_, html) = await GetAsync(factory);

        html.ShouldContain($"<a href=\"/t/{TicketTestKit.Token}/attachments/{TicketTestKit.AttachmentId}\">log.txt</a> <span class=\"ts-attachment-size\">(2 KB)</span>");
        html.ShouldNotContain("/api/customer/attachments", Case.Sensitive, "the browser is never sent to the API");
    }

    [Theory]
    [InlineData("New", "Received", false)]
    [InlineData("Open", "In progress", false)]
    [InlineData("Pending", "Waiting for your reply", false)]
    [InlineData("Solved", "Solved", false)]
    [InlineData("Closed", "Closed", true)]
    public async Task The_status_banner_uses_the_customers_wording_and_only_a_closed_ticket_says_a_reply_starts_a_follow_up(string status, string label, bool closed)
    {
        await using var factory = TicketTestKit.Factory(TicketTestKit.Ticket(status));

        var (_, html) = await GetAsync(factory);

        html.ShouldContain($"<span class=\"ts-status-label\">Status:</span> <strong>{label}</strong>");
        (html.Contains("we will start a new follow-up ticket linked to it.", StringComparison.Ordinal)).ShouldBe(closed);
        html.Contains(">Send and start a new follow-up ticket</button>", StringComparison.Ordinal).ShouldBe(closed);
        html.Contains(">Send reply</button>", StringComparison.Ordinal).ShouldBe(!closed);
        html.Contains("If you reply, it will be reopened.", StringComparison.Ordinal).ShouldBe(status == "Solved");
        html.Contains("ts-status-closed", StringComparison.Ordinal).ShouldBe(closed);
    }

    [Fact]
    public async Task A_closed_ticket_still_shows_the_full_history_and_the_reply_form_for_the_follow_up()
    {
        await using var factory = TicketTestKit.Factory(TicketTestKit.Ticket("Closed"));

        var (_, html) = await GetAsync(factory);

        html.ShouldContain("It jams every time.");
        html.ShouldContain("name=\"Reply.Body\"");
        html.ShouldContain("This ticket is closed. If you reply, we will start a new follow-up ticket linked to it.");
    }

    [Fact]
    public async Task The_reply_form_is_an_antiforgery_protected_multipart_post_to_the_same_page_with_a_labelled_body_and_attachments()
    {
        await using var factory = Host();

        var (_, html) = await GetAsync(factory);

        html.ShouldContain($"<form method=\"post\" action=\"/t/{TicketTestKit.Token}\" enctype=\"multipart/form-data\" novalidate");
        html.ShouldContain("name=\"__RequestVerificationToken\"");
        html.ShouldContain("name=\"_handler\" value=\"reply\"");
        html.ShouldContain("<label for=\"body\" class=\"form-label\">Your reply</label>");
        html.ShouldContain("name=\"Reply.Body\" class=\"form-control\" rows=\"6\" maxlength=\"100000\"");
        html.ShouldContain("name=\"Reply.Files\" type=\"file\" multiple");
        html.ShouldContain("Up to 5 files: images and documents of 10 MB each and 25 MB in all");
    }

    [Fact]
    public async Task The_headers_are_the_ticket_headers_and_the_policy_is_the_normal_one()
    {
        await using var factory = Host();

        var (response, _) = await GetAsync(factory);

        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("no-referrer");
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
        response.Headers.GetValues("X-Robots-Tag").Single().ShouldBe("noindex");
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        var policy = response.Headers.GetValues("Content-Security-Policy").Single().Split(';', StringSplitOptions.TrimEntries);
        policy.ShouldNotContain("sandbox", "the ticket page is an ordinary page: only the attachment is sandboxed");
        policy.ShouldContain("script-src 'self'");
    }

    [Fact]
    public async Task The_token_is_only_in_the_forms_action_and_the_attachment_links_never_in_a_marker_a_query_a_script_or_a_title()
    {
        await using var factory = Host();

        var (_, html) = await GetAsync(factory);

        html.ShouldNotContain("[token]");
        var contexts = TicketTestKit.TokenContexts(html);
        contexts.Count.ShouldBe(2, "the form action and the one attachment link");
        contexts.ShouldAllBe(c => c.Contains("action=\"/t/", StringComparison.Ordinal) || c.Contains("href=\"/t/", StringComparison.Ordinal));
        html.ShouldNotContain("?" + TicketTestKit.Token);
        html.ShouldNotContain("=" + TicketTestKit.Token);
        Regex.Match(html, "<title>.*?</title>").Value.ShouldNotContain(TicketTestKit.Token);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task An_inactive_or_unknown_product_is_the_neutral_theme_and_the_ticket_still_shows(HttpStatusCode productStatus)
    {
        await using var factory = TicketTestKit.Factory();
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/paperplane", productStatus, "product-not-found", "Npgsql host=10.0.0.5");

        var (response, html) = await GetAsync(factory);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, "an inactive product on a valid ticket is never a 404");
        html.ShouldContain("It jams every time.");
        html.ShouldContain("<title>Ticket PAP-42</title>");
        html.ShouldNotContain("--ts-accent");
        html.ShouldNotContain("ts-product-header");
        html.ShouldNotContain("Npgsql");
        html.ShouldContain("ts-powered");
    }

    [Fact]
    public async Task A_product_with_unacceptable_branding_is_re_checked_like_every_product_page()
    {
        await using var factory = TicketTestKit.Factory(environment: "Production");
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", new PublicProductDto("paperplane", "Paperplane", "javascript:alert(1)", "#F59E0B;background:url(//evil.example/x)", "#000000", "#9D6507"));

        var (_, html) = await GetAsync(factory);

        html.ShouldNotContain("evil.example");
        html.ShouldNotContain("javascript:");
        html.ShouldNotContain("ts-product-logo");
        html.ShouldNotContain("--ts-accent");
    }

    [Fact]
    public async Task A_ticket_with_no_public_messages_says_so()
    {
        await using var factory = TicketTestKit.Factory(new(
            "PAP-7", "paperplane", "Empty", "New", DateTimeOffset.UnixEpoch, []));

        var (_, html) = await GetAsync(factory);

        html.ShouldContain("There are no messages to show yet.");
        html.ShouldContain("<strong>Received</strong>");
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, "This ticket could not be loaded just now.")]
    [InlineData(HttpStatusCode.InternalServerError, HttpStatusCode.ServiceUnavailable, "This ticket could not be loaded just now.")]
    [InlineData(HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests, "You have sent a lot in a short time.")]
    public async Task When_the_api_fails_the_page_says_so_calmly_with_no_token_and_no_internals(HttpStatusCode api, HttpStatusCode page, string sentence)
    {
        await using var factory = FormTestKit.Factory("Production", product: false);
        factory.Api.OnProblem(HttpMethod.Get, TicketTestKit.TicketApi, api, "internal-error", "System.InvalidOperationException at Npgsql host=10.0.0.5");

        var (response, html) = await GetAsync(factory);

        response.StatusCode.ShouldBe(page);
        html.ShouldContain(sentence);
        html.ShouldNotContain("Npgsql");
        html.ShouldNotContain(TicketTestKit.Token, Case.Sensitive, "not even the address of this page is repeated in the body");
        html.ShouldNotContain("ts-product-header");
        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("no-referrer");
    }

    [Fact]
    public async Task A_transport_failure_is_the_same_calm_page()
    {
        await using var factory = FormTestKit.Factory("Production", product: false);
        factory.Api.On(HttpMethod.Get, TicketTestKit.TicketApi, _ => throw new HttpRequestException("No connection could be made (10.1.2.3:5432)"));

        var (response, html) = await GetAsync(factory);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        html.ShouldNotContain("10.1.2.3");
        factory.Api.Count(HttpMethod.Get, TicketTestKit.TicketApi).ShouldBe(1 + 2, "a read is retried twice");
    }
}
