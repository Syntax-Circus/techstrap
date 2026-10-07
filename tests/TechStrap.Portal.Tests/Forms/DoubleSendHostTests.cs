using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using TechStrap.Contracts.Tickets;
using TechStrap.Portal.Forms;
using TechStrap.Portal.Tests.Api;
using TechStrap.Portal.Tests.Tickets;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>
/// PHASE-09 T09 at the host (D-045 09d addendum, Review Focus 1 and 2): a double click on the contact, reply or lost-link form sends once. The same id posted twice, one after the other or at the same time, makes exactly
/// one API call and both posts redirect to the same place; a first post that the browser aborted still completes its write on its own token and the repeat goes to its result (or to the safe fallback when that result
/// is unknown); a 429, 409 or 503 lets a retry send; an id cannot be used on another form, product or ticket; a post with no id or a malformed one is not guarded; and neither a follow-up's token, a reference nor an id
/// reaches a log. Every test that calls the API asserts the visitor's address (the factory does it on dispose).
/// </summary>
public sealed class DoubleSendHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string NewToken = "Zk9_-qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq";
    private const string LostLinkPath = "/p/paperplane/lost-link";
    private const string LinkApi = "/api/customer/access-link";
    private static readonly string ReceivedNoRef = "http://localhost" + FormTestKit.ReceivedPath;

    // ---- helpers ----

    private static async Task<(HttpClient Client, string Token, string Id)> OpenContactAsync(PortalFactory factory)
    {
        var client = FormTestKit.Client(factory);
        var html = await client.GetStringAsync(FormTestKit.Path, Ct);
        return (client, FormTestKit.TokenFrom(html), FormTestKit.SubmitIdFrom(html));
    }

    private static async Task<(HttpClient Client, string Token, string Id)> OpenTicketAsync(PortalFactory factory, string path)
    {
        var client = TicketTestKit.Client(factory);
        var html = await client.GetStringAsync(path, Ct);
        return (client, FormTestKit.TokenFrom(html), FormTestKit.SubmitIdFrom(html, "Reply.SubmitId"));
    }

    private static async Task<(HttpClient Client, string Token, string Id)> OpenLostLinkAsync(PortalFactory factory)
    {
        var client = FormTestKit.Client(factory);
        var html = await client.GetStringAsync(LostLinkPath, Ct);
        return (client, FormTestKit.TokenFrom(html), FormTestKit.SubmitIdFrom(html));
    }

    private static MultipartFormDataContent Contact(string token, string id) => FormTestKit.ContactForm(token).WithSubmitId(id);

    private static MultipartFormDataContent Reply(string token, string id, string body = "Still broken.") => TicketTestKit.ReplyForm(token, body).WithSubmitId(id, "Reply.SubmitId");

    private static MultipartFormDataContent LostLink(string token, string id)
    {
        var form = new MultipartFormDataContent { { new StringContent("lost-link"), "_handler" }, { new StringContent(token), "__RequestVerificationToken" }, { new StringContent("ada@example.com"), "Form.Email" } };
        return form.WithSubmitId(id);
    }

    private static Task<HttpResponseMessage> PostContact(HttpClient client, string token, string id, CancellationToken cancellation) =>
        client.PostAsync(FormTestKit.Path, Contact(token, id), cancellation);

    private static Task<HttpResponseMessage> PostReply(HttpClient client, string path, string token, string id, CancellationToken cancellation) => client.PostAsync(path, Reply(token, id), cancellation);

    private static Task<HttpResponseMessage> PostLostLink(HttpClient client, string token, string id, CancellationToken cancellation) => client.PostAsync(LostLinkPath, LostLink(token, id), cancellation);

    private static PortalFactory Host(Action<IServiceCollection>? configure = null)
    {
        var factory = FormTestKit.Factory(configure: configure);
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created(), HttpStatusCode.Created);
        return factory;
    }

    /// <summary>The API call of <paramref name="path"/> is held until the test lets it go; <c>Arrived</c> completes when the Portal's request reached it.</summary>
    private sealed class Gate
    {
        private readonly TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Arrived => _arrived.Task;

        /// <summary>Whether the Portal's call had been cancelled at the moment the answer was given.</summary>
        public bool CancelledWhenAnswered { get; private set; }

        public void Release() => _release.TrySetResult();

        public void Hold(StubApiHandler api, string path, Func<HttpResponseMessage> answer) =>
            api.OnAsync(HttpMethod.Post, path, async (_, token) =>
            {
                _arrived.TrySetResult();
                await _release.Task.WaitAsync(token);
                CancelledWhenAnswered = token.IsCancellationRequested;
                return answer();
            });
    }

    private static HttpResponseMessage Json<T>(T body, HttpStatusCode status) => StubApiHandler.JsonResponse(status, body);

    // ---- the form carries a fresh id ----

    [Fact(Timeout = 10000)]
    public async Task Each_form_renders_a_hidden_22_character_id_right_after_the_antiforgery_field_and_a_fresh_one_every_time()
    {
        await using var factory = TicketTestKit.Factory();
        using var client = TicketTestKit.Client(factory);

        var contact = new[] { await client.GetStringAsync(FormTestKit.Path, TestContext.Current.CancellationToken), await client.GetStringAsync(FormTestKit.Path, TestContext.Current.CancellationToken) };
        var lostLink = new[] { await client.GetStringAsync(LostLinkPath, TestContext.Current.CancellationToken), await client.GetStringAsync(LostLinkPath, TestContext.Current.CancellationToken) };
        var reply = new[] { await client.GetStringAsync(TicketTestKit.Path, TestContext.Current.CancellationToken), await client.GetStringAsync(TicketTestKit.Path, TestContext.Current.CancellationToken) };

        foreach (var (pages, name) in new[] { (contact, "Form.SubmitId"), (lostLink, "Form.SubmitId"), (reply, "Reply.SubmitId") })
        {
            var ids = pages.Select(page => FormTestKit.SubmitIdFrom(page, name)).ToList();
            ids.ShouldAllBe(id => SubmitIds.IsWellFormed(id));
            ids[0].ShouldNotBe(ids[1]);
            System.Text.RegularExpressions.Regex.IsMatch(pages[0], $"name=\"__RequestVerificationToken\"[^>]*/>\\s*<input type=\"hidden\" name=\"{name}\"").ShouldBeTrue("the id sits straight after the antiforgery field");
        }
    }

    [Fact(Timeout = 10000)]
    public async Task A_form_shown_again_after_an_error_carries_a_new_id_not_the_posted_one()
    {
        await using var factory = Host();
        var (client, token, id) = await OpenContactAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(FormTestKit.Path, FormTestKit.ContactForm(token, email: "").WithSubmitId(id), TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("ts-error-summary");
        var again = FormTestKit.SubmitIdFrom(html);
        SubmitIds.IsWellFormed(again).ShouldBeTrue();
        again.ShouldNotBe(id);
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0);
    }

    [Fact(Timeout = 10000)]
    public async Task A_post_that_fails_validation_does_not_claim_its_id()
    {
        await using var factory = Host();
        var (client, token, id) = await OpenContactAsync(factory);
        using var _ = client;

        using var invalid = await client.PostAsync(FormTestKit.Path, FormTestKit.ContactForm(token, email: "").WithSubmitId(id), TestContext.Current.CancellationToken);
        using var valid = await PostContact(client, token, id, TestContext.Current.CancellationToken);

        invalid.StatusCode.ShouldBe(HttpStatusCode.OK);
        valid.StatusCode.ShouldBe(HttpStatusCode.Found);
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(1);
    }

    // ---- contact ----

    [Fact(Timeout = 10000)]
    public async Task Contact_the_same_id_posted_twice_in_a_row_calls_the_api_once_and_redirects_to_the_same_place()
    {
        await using var factory = Host();
        var (client, token, id) = await OpenContactAsync(factory);
        using var _ = client;

        using var first = await PostContact(client, token, id, TestContext.Current.CancellationToken);
        using var second = await PostContact(client, token, id, TestContext.Current.CancellationToken);

        first.StatusCode.ShouldBe(HttpStatusCode.Found);
        second.StatusCode.ShouldBe(HttpStatusCode.Found);
        second.Headers.Location.ShouldBe(first.Headers.Location);
        first.Headers.Location!.Query.ShouldStartWith("?ref=");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(1);
    }

    [Fact(Timeout = 10000)]
    public async Task Contact_two_posts_at_the_same_time_make_one_api_call_and_both_redirect_to_the_same_place()
    {
        var gate = new Gate();
        await using var factory = Host();
        gate.Hold(factory.Api, FormTestKit.ApiTicketsPath, () => Json(FormTestKit.Created(), HttpStatusCode.Created));
        var (client, token, id) = await OpenContactAsync(factory);
        using var _ = client;

        var first = PostContact(client, token, id, TestContext.Current.CancellationToken);
        await gate.Arrived.WaitAsync(TestContext.Current.CancellationToken);
        var second = PostContact(client, token, id, TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        gate.Release();
        using var firstResponse = await first;
        using var secondResponse = await second;

        firstResponse.StatusCode.ShouldBe(HttpStatusCode.Found);
        secondResponse.Headers.Location.ShouldBe(firstResponse.Headers.Location);
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(1);
    }

    [Fact(Timeout = 10000)]
    public async Task Contact_a_first_post_the_browser_aborted_still_completes_its_write_and_the_repeat_goes_to_its_result()
    {
        var gate = new Gate();
        await using var factory = Host();
        gate.Hold(factory.Api, FormTestKit.ApiTicketsPath, () => Json(FormTestKit.Created(), HttpStatusCode.Created));
        var (client, token, id) = await OpenContactAsync(factory);
        using var _ = client;
        using var abort = new CancellationTokenSource();

        var first = PostContact(client, token, id, abort.Token);
        await gate.Arrived.WaitAsync(TestContext.Current.CancellationToken);
        await abort.CancelAsync();
        var second = PostContact(client, token, id, TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        gate.Release();
        using var response = await second;
        await Should.ThrowAsync<OperationCanceledException>(() => first);

        gate.CancelledWhenAnswered.ShouldBeFalse("the write runs on its own token, which the browser cannot cancel");
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        response.Headers.Location!.Query.ShouldStartWith("?ref="); // the repeat goes to the first request's result: the received page with its reference
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(1);
    }

    [Fact(Timeout = 10000)]
    public async Task Contact_when_the_first_write_times_out_the_repeat_goes_to_the_received_page_without_a_reference_and_never_writes()
    {
        await using var factory = Host(services => services.AddSingleton(new SubmitGuard(SubmitGuard.Lifetime, TimeSpan.FromMilliseconds(300), 100)));
        factory.Api.OnAsync(HttpMethod.Post, FormTestKit.ApiTicketsPath, async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.Created);
        });
        var (client, token, id) = await OpenContactAsync(factory);
        using var _ = client;

        using var first = await PostContact(client, token, id, TestContext.Current.CancellationToken);
        using var second = await PostContact(client, token, id, TestContext.Current.CancellationToken);

        first.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("We could not send that just now.");
        second.StatusCode.ShouldBe(HttpStatusCode.Found);
        second.Headers.Location!.ToString().ShouldBe(ReceivedNoRef);
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(1);
    }

    [Theory(Timeout = 10000)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Contact_a_failure_releases_the_claim_so_a_retry_with_the_same_id_calls_the_api_again(HttpStatusCode failure)
    {
        await using var factory = Host();
        factory.Api.OnProblem(HttpMethod.Post, FormTestKit.ApiTicketsPath, failure, "x", "no");
        var (client, token, id) = await OpenContactAsync(factory);
        using var _ = client;

        using var failed = await PostContact(client, token, id, TestContext.Current.CancellationToken);
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created(), HttpStatusCode.Created);
        using var retry = await PostContact(client, token, id, TestContext.Current.CancellationToken);

        failed.StatusCode.ShouldNotBe(HttpStatusCode.Found);
        retry.StatusCode.ShouldBe(HttpStatusCode.Found);
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(2);
    }

    [Fact(Timeout = 10000)]
    public async Task Contact_a_repeat_that_was_waiting_when_the_first_failed_gets_the_same_failure_and_sends_nothing()
    {
        var gate = new Gate();
        await using var factory = Host();
        gate.Hold(factory.Api, FormTestKit.ApiTicketsPath, () => StubApiHandler.Problem(HttpStatusCode.TooManyRequests, "rate-limited", "slow down"));
        var (client, token, id) = await OpenContactAsync(factory);
        using var _ = client;

        var first = PostContact(client, token, id, TestContext.Current.CancellationToken);
        await gate.Arrived.WaitAsync(TestContext.Current.CancellationToken);
        var second = PostContact(client, token, id, TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        gate.Release();
        using var firstResponse = await first;
        using var secondResponse = await second;

        firstResponse.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        secondResponse.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(1);
    }

    [Theory(Timeout = 10000)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("AbC-_0123456789AbC-_0=")]
    public async Task Contact_a_missing_or_malformed_id_is_not_guarded(string? id)
    {
        await using var factory = Host();
        var (client, token, _) = await OpenContactAsync(factory);
        using var _c = client;

        for (var i = 0; i < 2; i++)
        {
            var form = FormTestKit.ContactForm(token);
            if (id is not null)
            {
                form.WithSubmitId(id);
            }

            using var response = await client.PostAsync(FormTestKit.Path, form, TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.Found);
        }

        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(2);
    }

    // ---- an id belongs to one form, one product and one ticket ----

    [Fact(Timeout = 10000)]
    public async Task An_id_from_the_contact_form_cannot_claim_the_lost_link_form_and_the_other_way_round()
    {
        await using var factory = Host();
        factory.Api.OnStatus(HttpMethod.Post, LinkApi, HttpStatusCode.Accepted);
        var (client, token, id) = await OpenContactAsync(factory);
        using var _ = client;
        var lostToken = FormTestKit.TokenFrom(await client.GetStringAsync(LostLinkPath, TestContext.Current.CancellationToken));

        using var contact = await PostContact(client, token, id, TestContext.Current.CancellationToken);
        using var lost = await PostLostLink(client, lostToken, id, TestContext.Current.CancellationToken);
        using var lostAgain = await PostLostLink(client, lostToken, id, TestContext.Current.CancellationToken);

        contact.StatusCode.ShouldBe(HttpStatusCode.Found);
        lost.StatusCode.ShouldBe(HttpStatusCode.Found);
        lostAgain.Headers.Location.ShouldBe(lost.Headers.Location);
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(1);
        factory.Api.Count(HttpMethod.Post, LinkApi).ShouldBe(1, "the lost-link claim is its own: one email, though the id was also used on the contact form");
    }

    [Fact(Timeout = 10000)]
    public async Task An_id_from_one_product_cannot_claim_another_products_contact_form()
    {
        await using var factory = Host();
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/other", FormTestKit.Product("Other") with { Key = "other" });
        factory.Api.OnJson(HttpMethod.Post, "/api/public/products/other/tickets", FormTestKit.Created("OTH-1"), HttpStatusCode.Created);
        var (client, token, id) = await OpenContactAsync(factory);
        using var _ = client;
        var otherToken = FormTestKit.TokenFrom(await client.GetStringAsync("/p/other/contact", TestContext.Current.CancellationToken));

        using var paperplane = await PostContact(client, token, id, TestContext.Current.CancellationToken);
        using var other = await client.PostAsync("/p/other/contact", FormTestKit.ContactForm(otherToken).WithSubmitId(id), TestContext.Current.CancellationToken);

        paperplane.Headers.Location!.ToString().ShouldContain("/p/paperplane/contact/received");
        other.Headers.Location!.ToString().ShouldContain("/p/other/contact/received");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(1);
        factory.Api.Count(HttpMethod.Post, "/api/public/products/other/tickets").ShouldBe(1);
    }

    // ---- lost link ----

    [Fact(Timeout = 10000)]
    public async Task Lost_link_the_same_id_twice_asks_for_one_email_and_both_posts_go_to_the_sent_page()
    {
        await using var factory = Host();
        factory.Api.OnStatus(HttpMethod.Post, LinkApi, HttpStatusCode.Accepted);
        var (client, token, id) = await OpenLostLinkAsync(factory);
        using var _ = client;

        using var first = await PostLostLink(client, token, id, TestContext.Current.CancellationToken);
        using var second = await PostLostLink(client, token, id, TestContext.Current.CancellationToken);

        first.Headers.Location!.ToString().ShouldBe("http://localhost/p/paperplane/lost-link?sent=1");
        second.Headers.Location.ShouldBe(first.Headers.Location);
        factory.Api.Count(HttpMethod.Post, LinkApi).ShouldBe(1);
    }

    [Fact(Timeout = 10000)]
    public async Task Lost_link_two_posts_at_the_same_time_and_an_aborted_first_post_send_one_email()
    {
        var gate = new Gate();
        await using var factory = Host();
        gate.Hold(factory.Api, LinkApi, () => new HttpResponseMessage(HttpStatusCode.Accepted));
        var (client, token, id) = await OpenLostLinkAsync(factory);
        using var _ = client;
        using var abort = new CancellationTokenSource();

        var first = client.PostAsync(LostLinkPath, LostLink(token, id), abort.Token);
        await gate.Arrived.WaitAsync(TestContext.Current.CancellationToken);
        await abort.CancelAsync();
        var second = PostLostLink(client, token, id, TestContext.Current.CancellationToken);
        var third = PostLostLink(client, token, id, TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        gate.Release();
        using var secondResponse = await second;
        using var thirdResponse = await third;
        await Should.ThrowAsync<OperationCanceledException>(() => first);

        gate.CancelledWhenAnswered.ShouldBeFalse();
        secondResponse.Headers.Location!.ToString().ShouldBe("http://localhost/p/paperplane/lost-link?sent=1");
        thirdResponse.Headers.Location.ShouldBe(secondResponse.Headers.Location);
        factory.Api.Count(HttpMethod.Post, LinkApi).ShouldBe(1);
    }

    [Fact(Timeout = 10000)]
    public async Task Lost_link_a_rate_limit_releases_the_claim_so_a_retry_sends()
    {
        await using var factory = Host();
        factory.Api.OnProblem(HttpMethod.Post, LinkApi, HttpStatusCode.TooManyRequests, "rate-limited", "slow");
        var (client, token, id) = await OpenLostLinkAsync(factory);
        using var _ = client;

        using var limited = await PostLostLink(client, token, id, TestContext.Current.CancellationToken);
        factory.Api.OnStatus(HttpMethod.Post, LinkApi, HttpStatusCode.Accepted);
        using var retry = await PostLostLink(client, token, id, TestContext.Current.CancellationToken);

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        retry.StatusCode.ShouldBe(HttpStatusCode.Found);
        factory.Api.Count(HttpMethod.Post, LinkApi).ShouldBe(2);
    }

    // ---- reply ----

    private static PortalFactory ReplyHost(CustomerTicketDto? ticket = null, CustomerReplyResponse? answer = null)
    {
        var factory = TicketTestKit.Factory(ticket);
        factory.Api.OnJson(HttpMethod.Post, TicketTestKit.ReplyApi, answer ?? new CustomerReplyResponse("PAP-42", Guid.NewGuid(), false, null), HttpStatusCode.Created);
        return factory;
    }

    [Fact(Timeout = 10000)]
    public async Task Reply_the_same_id_twice_sends_one_reply_and_both_posts_go_back_to_the_ticket()
    {
        await using var factory = ReplyHost();
        var (client, token, id) = await OpenTicketAsync(factory, TicketTestKit.Path);
        using var _ = client;

        using var first = await PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);
        using var second = await PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);

        first.Headers.Location!.ToString().ShouldBe("http://localhost" + TicketTestKit.Path);
        second.Headers.Location.ShouldBe(first.Headers.Location);
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(1);
    }

    [Fact(Timeout = 10000)]
    public async Task Reply_on_a_closed_ticket_a_repeat_goes_to_the_same_follow_up_page_and_starts_one_follow_up()
    {
        var link = "https://help.example.com/t/" + NewToken;
        await using var factory = ReplyHost(TicketTestKit.Ticket("Closed"), new CustomerReplyResponse("PAP-43", Guid.NewGuid(), true, link));
        var (client, token, id) = await OpenTicketAsync(factory, TicketTestKit.Path);
        using var _ = client;

        using var first = await PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);
        using var second = await PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);

        first.Headers.Location!.ToString().ShouldBe("http://localhost/t/" + NewToken);
        second.Headers.Location.ShouldBe(first.Headers.Location);
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(1);
    }

    [Fact(Timeout = 10000)]
    public async Task Reply_on_a_closed_ticket_two_posts_at_the_same_time_and_an_aborted_first_post_start_one_follow_up()
    {
        var gate = new Gate();
        var link = "https://help.example.com/t/" + NewToken;
        await using var factory = ReplyHost(TicketTestKit.Ticket("Closed"));
        gate.Hold(factory.Api, TicketTestKit.ReplyApi, () => Json(new CustomerReplyResponse("PAP-43", Guid.NewGuid(), true, link), HttpStatusCode.Created));
        var (client, token, id) = await OpenTicketAsync(factory, TicketTestKit.Path);
        using var _ = client;
        using var abort = new CancellationTokenSource();

        var first = client.PostAsync(TicketTestKit.Path, Reply(token, id), abort.Token);
        await gate.Arrived.WaitAsync(TestContext.Current.CancellationToken);
        await abort.CancelAsync();
        var second = PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);
        var third = PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        gate.Release();
        using var secondResponse = await second;
        using var thirdResponse = await third;
        await Should.ThrowAsync<OperationCanceledException>(() => first);

        gate.CancelledWhenAnswered.ShouldBeFalse();
        secondResponse.Headers.Location!.ToString().ShouldBe("http://localhost/t/" + NewToken);
        thirdResponse.Headers.Location.ShouldBe(secondResponse.Headers.Location);
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(1);
    }

    [Fact(Timeout = 10000)]
    public async Task Reply_when_the_first_answer_is_unknown_a_repeat_goes_back_to_the_ticket_page_and_never_sends()
    {
        await using var factory = TicketTestKit.Factory(TicketTestKit.Ticket("Closed"), configure: services => services.AddSingleton(new SubmitGuard(SubmitGuard.Lifetime, TimeSpan.FromMilliseconds(300), 100)));
        factory.Api.OnAsync(HttpMethod.Post, TicketTestKit.ReplyApi, async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.Created);
        });
        var (client, token, id) = await OpenTicketAsync(factory, TicketTestKit.Path);
        using var _ = client;

        using var first = await PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);
        using var second = await PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);

        first.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        second.Headers.Location!.ToString().ShouldBe("http://localhost" + TicketTestKit.Path);
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(1);
    }

    [Fact(Timeout = 10000)]
    public async Task Reply_a_follow_up_whose_link_cannot_be_read_shows_the_confirmation_for_the_repeat_too_and_sends_once()
    {
        await using var factory = ReplyHost(TicketTestKit.Ticket("Closed"), new CustomerReplyResponse("PAP-43", Guid.NewGuid(), true, "not a link"));
        var (client, token, id) = await OpenTicketAsync(factory, TicketTestKit.Path);
        using var _ = client;

        using var first = await PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);
        using var second = await PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe(await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), "the same confirmation page, whichever post asks");
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(1);
    }

    [Theory(Timeout = 10000)]
    [InlineData(HttpStatusCode.Conflict, "reply-conflict")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate-limited")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "x")]
    public async Task Reply_a_failure_releases_the_claim_so_a_retry_with_the_same_id_sends(HttpStatusCode failure, string code)
    {
        await using var factory = ReplyHost();
        factory.Api.OnProblem(HttpMethod.Post, TicketTestKit.ReplyApi, failure, code, "no");
        var (client, token, id) = await OpenTicketAsync(factory, TicketTestKit.Path);
        using var _ = client;

        using var failed = await PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);
        factory.Api.OnJson(HttpMethod.Post, TicketTestKit.ReplyApi, new CustomerReplyResponse("PAP-42", Guid.NewGuid(), false, null), HttpStatusCode.Created);
        using var retry = await PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);

        failed.StatusCode.ShouldNotBe(HttpStatusCode.Found);
        retry.StatusCode.ShouldBe(HttpStatusCode.Found);
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(2);
    }

    [Fact(Timeout = 10000)]
    public async Task Reply_an_id_cannot_claim_a_reply_to_another_ticket()
    {
        await using var factory = ReplyHost();
        var (client, token, id) = await OpenTicketAsync(factory, TicketTestKit.Path);
        using var _ = client;
        var otherPath = "/t/" + TicketTestKit.OtherToken;
        var otherToken = FormTestKit.TokenFrom(await client.GetStringAsync(otherPath, TestContext.Current.CancellationToken));

        using var mine = await PostReply(client, TicketTestKit.Path, token, id, TestContext.Current.CancellationToken);
        using var theirs = await PostReply(client, otherPath, otherToken, id, TestContext.Current.CancellationToken);

        mine.Headers.Location!.ToString().ShouldBe("http://localhost" + TicketTestKit.Path);
        theirs.Headers.Location!.ToString().ShouldBe("http://localhost" + otherPath);
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(2);
    }

    [Fact(Timeout = 10000)]
    public async Task Reply_a_post_with_no_id_is_not_guarded()
    {
        await using var factory = ReplyHost();
        var (client, token, _) = await OpenTicketAsync(factory, TicketTestKit.Path);
        using var _c = client;

        using var first = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token), TestContext.Current.CancellationToken);
        using var second = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token), TestContext.Current.CancellationToken);

        first.StatusCode.ShouldBe(HttpStatusCode.Found);
        second.StatusCode.ShouldBe(HttpStatusCode.Found);
        factory.Api.Count(HttpMethod.Post, TicketTestKit.ReplyApi).ShouldBe(2);
    }

    // ---- secrecy ----

    [Fact(Timeout = 10000)]
    public async Task No_log_event_at_any_level_carries_a_follow_ups_token_a_reference_or_an_id()
    {
        var link = "https://help.example.com/t/" + NewToken;
        await using var factory = FormTestKit.Factory(settings: PortalFactory.VerboseLogging);
        factory.Api.OnJson(HttpMethod.Get, TicketTestKit.TicketApi, TicketTestKit.Ticket("Closed"));
        factory.Api.OnJson(HttpMethod.Post, TicketTestKit.ReplyApi, new CustomerReplyResponse("PAP-43", Guid.NewGuid(), true, link), HttpStatusCode.Created);
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created(), HttpStatusCode.Created);
        var (contactClient, contactToken, contactId) = await OpenContactAsync(factory);
        using var _ = contactClient;
        var (replyClient, replyToken, replyId) = await OpenTicketAsync(factory, TicketTestKit.Path);
        using var __ = replyClient;

        using var contact = await PostContact(contactClient, contactToken, contactId, TestContext.Current.CancellationToken);
        using var contactRepeat = await PostContact(contactClient, contactToken, contactId, TestContext.Current.CancellationToken);
        using var reply = await PostReply(replyClient, TicketTestKit.Path, replyToken, replyId, TestContext.Current.CancellationToken);
        using var replyRepeat = await PostReply(replyClient, TicketTestKit.Path, replyToken, replyId, TestContext.Current.CancellationToken);
        await replyClient.GetStringAsync(reply.Headers.Location!.PathAndQuery, TestContext.Current.CancellationToken);
        var reference = contact.Headers.Location!.Query["?ref=".Length..];

        reference.Length.ShouldBeGreaterThan(20);
        factory.LogSink.Events.ShouldContain(e => e.Level <= LogEventLevel.Debug, "the Verbose setting must have taken effect");
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text =>
            !text.Contains(NewToken, StringComparison.Ordinal)
            && !text.Contains(TicketTestKit.Token, StringComparison.Ordinal)
            && !text.Contains(Uri.UnescapeDataString(reference), StringComparison.Ordinal)
            && !text.Contains(reference, StringComparison.Ordinal)
            && !text.Contains(contactId, StringComparison.Ordinal)
            && !text.Contains(replyId, StringComparison.Ordinal));
    }

    private static string Everything(LogEvent e) => string.Join('\n', [e.RenderMessage(), e.Exception?.ToString() ?? string.Empty, .. e.Properties.Values.Select(v => v.ToString())]);
}
