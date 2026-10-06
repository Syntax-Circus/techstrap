using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using TechStrap.Contracts.Intake;
using TechStrap.Contracts.Tickets;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;
using TechStrap.Portal.Tests.Forms;

namespace TechStrap.Portal.Tests.Tickets;

/// <summary>
/// P09-T09 at the host: the reply form on the ticket page. A reply on an open ticket is sent through the write client (once, with the token as a header) and redirects to the same page; a reply on a Closed ticket
/// redirects to the follow-up's own page, whose token is read from the API's link and checked, and never leaves the site (Review Focus 1); a link that cannot be read gives a generic confirmation and no redirect.
/// Validation, 409, 413, 415, 429 and outages each have their own message and the text is kept. Antiforgery is enforced (Review Focus 3), the size limit applies before the form is read, and the redirect after a post guards a refresh (not a double click: that is a known gap, deferred to 09c, so P09-T09 stays open). Every call forwards the visitor's address (Review Focus 5).
/// </summary>
public sealed class TicketReplyHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string Visitor = FormTestKit.Visitor;
    private const string NewToken = "Zk9_-qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq";

    private static PortalFactory Host(CustomerTicketDto? ticket = null, Action<IServiceCollection>? configure = null) => TicketTestKit.Factory(ticket, configure: configure);

    private static async Task<(HttpClient Client, string Token)> OpenAsync(PortalFactory factory)
    {
        var client = TicketTestKit.Client(factory);
        return (client, await FormTestKit.TokenAsync(client, TicketTestKit.Path, Ct));
    }

    private static CustomerReplyResponse Replied() => new("PAP-42", Guid.NewGuid(), false, null);

    private static CustomerReplyResponse FollowUp(string? link) => new("PAP-43", Guid.NewGuid(), true, link);

    // ---- open ticket ----

    [Fact]
    public async Task A_reply_is_sent_once_through_the_write_client_with_the_token_header_and_redirects_to_the_same_page()
    {
        await using var factory = Host();
        factory.Api.OnJson(HttpMethod.Post, TicketTestKit.ReplyApi, Replied(), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token, "Still broken.", new PostedFile("shot.png", [1, 2, 3], "image/png")), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        response.Headers.Location.ShouldNotBeNull().ToString().ShouldBe("http://localhost" + TicketTestKit.Path);
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
        var sent = factory.Api.Requests.Single(r => r.Method == HttpMethod.Post);
        sent.Client.ShouldBe(ApiClientNames.Write);
        sent.TicketToken.ShouldBe(TicketTestKit.Token);
        sent.Path.ShouldBe(TicketTestKit.ReplyApi);
        sent.Body.ShouldNotBeNull().ShouldContain("Still broken.");
        sent.Body.ShouldContain("name=Attachments; filename=shot.png");
        sent.Body.ShouldNotContain(TicketTestKit.Token, Case.Sensitive, "the token is a header, never a form field");
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(1);
        factory.Api.AssertEveryCallBore(Visitor);
    }

    [Fact]
    public async Task A_refresh_after_the_redirect_sends_nothing_again()
    {
        await using var factory = Host();
        factory.Api.OnJson(HttpMethod.Post, TicketTestKit.ReplyApi, Replied(), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;
        using var posted = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token), Ct);

        await client.GetStringAsync(posted.Headers.Location!.PathAndQuery, Ct);
        await client.GetStringAsync(posted.Headers.Location!.PathAndQuery, Ct);

        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(1, "post, redirect, get: reloading never posts again");
    }

    [Fact]
    public async Task The_reply_text_is_sent_trimmed()
    {
        await using var factory = Host();
        factory.Api.OnJson(HttpMethod.Post, TicketTestKit.ReplyApi, Replied(), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token, "\r\n  Hello.  \r\n"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        factory.Api.Requests.Single(r => r.Method == HttpMethod.Post).Body!.ShouldContain("\r\n\r\nHello.\r\n");
    }

    // ---- the Closed ticket and the follow-up ----

    [Fact]
    public async Task A_reply_on_a_closed_ticket_redirects_to_the_follow_ups_own_page_on_this_site()
    {
        await using var factory = Host(TicketTestKit.Ticket("Closed"));
        factory.Api.OnJson(HttpMethod.Post, TicketTestKit.ReplyApi, FollowUp("https://help.example.com/t/" + NewToken), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token, "Back again."), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        response.Headers.Location!.ToString().ShouldBe("http://localhost/t/" + NewToken, "the Portal's own host and its own route, whatever host the API's link had");
    }

    [Theory]
    [InlineData("https://evil.example/t/" + NewToken)]
    [InlineData("https://help.example.com/t/" + NewToken + "?next=https://evil.example")]
    [InlineData("//evil.example/t/" + NewToken)]
    public async Task The_redirect_never_leaves_the_site_whatever_host_the_apis_link_names(string link)
    {
        await using var factory = Host(TicketTestKit.Ticket("Closed"));
        factory.Api.OnJson(HttpMethod.Post, TicketTestKit.ReplyApi, FollowUp(link), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token), Ct);

        if (response.StatusCode == HttpStatusCode.Found)
        {
            response.Headers.Location!.Host.ShouldBe("localhost");
            response.Headers.Location.AbsolutePath.ShouldBe("/t/" + NewToken);
            response.Headers.Location.Query.ShouldBeEmpty();
        }
        else
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Headers.Contains("Location").ShouldBeFalse();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://evil.example/t/short")]
    [InlineData("https://evil.example/")]
    [InlineData("not a link")]
    public async Task A_follow_up_link_that_cannot_be_read_gives_a_generic_confirmation_and_no_redirect(string? link)
    {
        await using var factory = Host(TicketTestKit.Ticket("Closed"));
        factory.Api.OnJson(HttpMethod.Post, TicketTestKit.ReplyApi, FollowUp(link), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Location").ShouldBeFalse();
        html.ShouldContain("Your reply was received and we started a new follow-up ticket for it. We have emailed you its link.");
        html.ShouldNotContain("name=\"Reply.Body\"", Case.Sensitive, "the form is gone, so the same reply cannot be sent twice from this page");
        html.ShouldNotContain("evil.example");
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(1);
    }

    // ---- antiforgery and the size limit ----

    [Fact]
    public async Task A_post_without_an_antiforgery_token_is_a_400_and_nothing_reaches_the_api()
    {
        await using var factory = Host();
        var (client, _) = await OpenAsync(factory);
        using var __ = client;
        var before = factory.Api.Requests.Count;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(null), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        factory.Api.Requests.Count.ShouldBe(before);
    }

    [Fact]
    public async Task On_the_real_server_a_post_over_the_forms_limit_is_a_413_before_anything_is_read()
    {
        await using var factory = TicketTestKit.Factory();
        factory.UseKestrel(0);
        factory.StartServer();
        var address = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.Add("X-Forwarded-For", FormTestKit.Visitor);
        var token = await FormTestKit.TokenAsync(client, TicketTestKit.Path, Ct);
        var apiCalls = factory.Api.Requests.Count;
        using var request = new HttpRequestMessage(HttpMethod.Post, TicketTestKit.Path) { Content = TicketTestKit.ReplyForm(token, "x", new PostedFile("big.zip", new byte[(int)IntakeLimits.FormBodyBytes], "application/zip")) };
        request.Headers.ExpectContinue = true;

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        factory.Api.Requests.Count.ShouldBe(apiCalls);
    }

    // ---- validation: nothing is sent, the text is kept ----

    [Fact]
    public async Task An_empty_reply_is_an_error_with_the_summary_and_calls_nothing()
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token, "   "), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<a href=\"#body\">Write a message.</a>");
        html.ShouldContain("<p id=\"body-error\" class=\"ts-field-error\">Write a message.</p>");
        html.ShouldContain("It jams every time.", Case.Sensitive, "the conversation is still shown");
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(0);
    }

    [Fact]
    public async Task A_reply_over_the_limit_and_files_that_break_the_rules_are_refused_before_anything_is_sent()
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;
        var tooLong = new string('x', IntakeLimits.BodyMaxLength + 1);

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token, tooLong, new PostedFile("virus.exe", [1]), new PostedFile("empty.txt", [])), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        html.ShouldContain("The message must be at most 100,000 characters.");
        html.ShouldContain("virus.exe is a type we cannot accept.");
        html.ShouldContain("empty.txt is empty. Remove it or choose another.");
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(0);
    }

    [Fact]
    public async Task What_the_visitor_typed_is_kept_encoded_when_a_reply_fails()
    {
        await using var factory = Host();
        factory.Api.OnProblem(HttpMethod.Post, TicketTestKit.ReplyApi, HttpStatusCode.Conflict, "reply-conflict", "rowversion 17 != 18");
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token, "I wrote <b>this</b> & more"), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        html.ShouldContain("I wrote &lt;b&gt;this&lt;/b&gt; &amp; more</textarea>");
        html.ShouldNotContain("I wrote <b>this</b>");
    }

    // ---- what the API says ----

    [Fact]
    public async Task A_409_has_its_own_message_a_409_status_and_keeps_the_text()
    {
        await using var factory = Host();
        factory.Api.OnProblem(HttpMethod.Post, TicketTestKit.ReplyApi, HttpStatusCode.Conflict, "reply-conflict", "rowversion 17 != 18");
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token, "Still broken."), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        html.ShouldContain("Your reply could not be saved this time. Your text is still here: send it again.");
        html.ShouldContain("Still broken.</textarea>");
        html.ShouldNotContain("rowversion");
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(1, "never retried");
    }

    [Fact]
    public async Task The_apis_field_codes_become_the_portals_sentences()
    {
        await using var factory = Host();
        factory.Api.On(HttpMethod.Post, TicketTestKit.ReplyApi, _ => StubApiHandler.ValidationProblem(
            [("body", "body-required", "API TEXT"), ("attachments", "attachments-too-many", "API TEXT")]));
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("Write a message.");
        html.ShouldContain("Attach at most 5 files.");
        html.ShouldNotContain("API TEXT");
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.OK, "That is too large to send.")]
    [InlineData(HttpStatusCode.UnsupportedMediaType, HttpStatusCode.OK, "That could not be sent in that form.")]
    [InlineData(HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests, "Too many attempts.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, "We could not send that just now.")]
    public async Task Every_other_failure_is_calm_with_a_fitting_status_and_the_text_is_kept(HttpStatusCode api, HttpStatusCode page, string sentence)
    {
        await using var factory = Host();
        factory.Api.OnProblem(HttpMethod.Post, TicketTestKit.ReplyApi, api, "x", "System.InvalidOperationException at Npgsql host=10.0.0.5");
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token, "Still broken."), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(page);
        html.ShouldContain(sentence);
        html.ShouldNotContain("Npgsql");
        html.ShouldContain("Still broken.</textarea>");
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, "This ticket could not be loaded just now.")]
    [InlineData(HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests, "You have sent a lot in a short time.")]
    public async Task When_the_ticket_cannot_be_loaded_for_the_post_the_reply_text_is_kept_beside_the_notice(HttpStatusCode api, HttpStatusCode page, string sentence)
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;
        // The page loaded for the visitor; between the page and the post the API stops answering the ticket read that every post begins with.
        factory.Api.OnProblem(HttpMethod.Get, TicketTestKit.TicketApi, api, "x", "System.InvalidOperationException at Npgsql host=10.0.0.5");

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token, "A long reply I do not want to lose."), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(page);
        html.ShouldContain(sentence);
        html.ShouldContain("A long reply I do not want to lose.</textarea>");
        html.ShouldContain("<form method=\"post\"");
        html.ShouldNotContain("Npgsql");
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(0, "nothing is sent while the ticket cannot be read");
        // The token is still only in the form's action and nowhere else a visitor could copy it from.
        TicketTestKit.TokenContexts(html).ShouldAllBe(c => c.EndsWith("action=\"/t/" + TicketTestKit.Token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_post_to_a_malformed_or_unknown_ticket_token_is_the_same_plain_400_so_nothing_tells_them_apart()
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        factory.Api.OnProblem(HttpMethod.Get, TicketTestKit.TicketApi, HttpStatusCode.NotFound, "token-not-found", "No such token.");
        // A good antiforgery token and cookie (they came from a ticket page); only the address of the post is wrong. The ticket is never rendered, so the form it posts to does not exist.
        using var malformed = await client.PostAsync("/t/x", TicketTestKit.ReplyForm(token), Ct);
        using var unknown = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token), Ct);
        var seenMalformed = await Seen.OfAsync(malformed, 0, Ct);
        var seenUnknown = await Seen.OfAsync(unknown, 0, Ct);

        seenMalformed.Status.ShouldBe(HttpStatusCode.BadRequest);
        seenMalformed.Body.ShouldBe("Cannot submit the form 'reply' because no form on the page currently has that name.");
        seenUnknown.Status.ShouldBe(seenMalformed.Status);
        seenUnknown.Body.ShouldBe(seenMalformed.Body, "byte for byte");
        seenUnknown.Headers.ShouldBe(seenMalformed.Headers);
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(0);
    }

    [Fact]
    public async Task A_token_that_stops_working_between_the_page_and_the_post_is_the_uniform_404()
    {
        await using var factory = Host();
        factory.Api.OnProblem(HttpMethod.Post, TicketTestKit.ReplyApi, HttpStatusCode.NotFound, "token-revoked", "An agent revoked this token.");
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token), Ct);
        var seen = await Seen.OfAsync(response, factory.Api.Requests.Count, Ct);

        // The page was themed with the product before the post failed; the 404 must still be the neutral one a visitor with a bad token gets.
        seen.ShouldBeTheNeutralNotFound(await Seen.NeutralNotFoundAsync(Ct));
        seen.Body.ShouldContain("Page not found");
        seen.Body.ShouldNotContain("revoked");
    }

    // ---- logs ----

    [Fact]
    public async Task The_token_and_the_reply_text_never_reach_a_log_event_at_any_level()
    {
        await using var factory = FormTestKit.Factory(settings: PortalFactory.VerboseLogging);
        factory.Api.OnJson(HttpMethod.Get, TicketTestKit.TicketApi, TicketTestKit.Ticket("Closed"));
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", FormTestKit.Product());
        factory.Api.OnJson(HttpMethod.Post, TicketTestKit.ReplyApi, FollowUp("https://help.example.com/t/" + NewToken), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token, "A very private reply"), Ct);
        await client.GetStringAsync(response.Headers.Location!.PathAndQuery, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        factory.LogSink.Events.ShouldContain(e => e.Level <= LogEventLevel.Debug, "the Verbose setting must have taken effect");
        factory.LogSink.Events.ShouldContain(e => Everything(e).Contains("[token]", StringComparison.Ordinal), "control: the request line was logged, with the token masked");
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text =>
            !text.Contains(TicketTestKit.Token, StringComparison.Ordinal)
            && !text.Contains(NewToken, StringComparison.Ordinal)
            && !text.Contains("very private reply", StringComparison.Ordinal)
            && !text.Contains(token, StringComparison.Ordinal));
    }

    private static string Everything(LogEvent e) => string.Join('\n', [e.RenderMessage(), e.Exception?.ToString() ?? string.Empty, .. e.Properties.Values.Select(v => v.ToString())]);
}
