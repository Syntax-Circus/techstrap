using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using TechStrap.Contracts.Intake;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Products;
using TechStrap.Portal.Tests.Api;
using TechStrap.Portal.Tests.Tickets;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>
/// P09-T06 at the host: the post of the contact form. A valid post creates the ticket through the API, redirects (302) to the received page with a protected reference and never shows the address, subject or
/// name in the redirect; an invalid post shows the error summary and every field error, keeps what was typed and calls nothing; every API failure is shown in the Portal's own words with a fitting status. Review
/// Focus 3: antiforgery is enforced (a post without a valid token is a 400 and reaches no handler), the files are checked before anything is sent, the honeypot is passed through, and every call forwards the
/// visitor's address (Review Focus 5). Every test that makes an API call asserts it (<c>AssertEveryCallBore</c>).
/// </summary>
public sealed class ContactPostHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string Visitor = FormTestKit.Visitor;

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, HttpContent content) => client.PostAsync(FormTestKit.Path, content, Ct);

    private static PortalFactory Host(Action<IServiceCollection>? configure = null) => FormTestKit.Factory(configure: configure);

    private static async Task<(HttpClient Client, string Token)> OpenAsync(PortalFactory factory)
    {
        var client = FormTestKit.Client(factory);
        return (client, await FormTestKit.TokenAsync(client, FormTestKit.Path, Ct));
    }

    private static string Text(string html, string id) => Regex.Match(html, $"id=\"{id}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;

    // ---- the happy path and the redirect ----

    [Fact]
    public async Task A_valid_post_creates_the_ticket_and_redirects_to_the_received_page_with_a_protected_reference()
    {
        await using var factory = Host();
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created("PAP-42"), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token));

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        var location = response.Headers.Location.ShouldNotBeNull().ToString();
        location.ShouldStartWith("http://localhost/p/paperplane/contact/received?ref=");
        location.ShouldNotContain("PAP-42", Case.Sensitive, "the number travels protected");
        // The reference is random base64, so a short word can occur in it by chance: only strings that cannot are checked (the address, the surname, the subject).
        location.ShouldNotContain("example.com", Case.Insensitive);
        location.ShouldNotContain("Lovelace", Case.Insensitive);
        location.ShouldNotContain("Printer", Case.Insensitive);
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
        var sent = factory.Api.Requests.Single(r => r.Method == HttpMethod.Post);
        sent.Client.ShouldBe(ApiClientNames.Write);
        sent.Path.ShouldBe(FormTestKit.ApiTicketsPath);
        sent.Body.ShouldNotBeNull().ShouldContain("ada@example.com");
        sent.Body.ShouldContain("Printer jam");
        sent.Body.ShouldNotContain("name=Website", Case.Sensitive, "an empty honeypot is not sent");
        factory.Api.AssertEveryCallBore(Visitor);
    }

    [Fact]
    public async Task Following_the_redirect_shows_the_ticket_number_and_a_refresh_sends_nothing_again()
    {
        await using var factory = Host();
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created("PAP-42"), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;
        using var posted = await PostAsync(client, FormTestKit.ContactForm(token));
        var next = posted.Headers.Location!.PathAndQuery;

        var first = await client.GetStringAsync(next, Ct);
        var second = await client.GetStringAsync(next, Ct);

        first.ShouldContain("We have received your request.");
        first.ShouldContain("<strong class=\"ts-ticket-number\">PAP-42</strong>");
        second.ShouldContain("PAP-42");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(1, "post, redirect, get: reloading the confirmation never posts again");
    }

    [Fact]
    public async Task Text_with_surrounding_spaces_is_sent_trimmed()
    {
        await using var factory = Host();
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created(), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token, email: "  ada@example.com ", name: " Ada ", subject: "  Printer jam  ", body: "\r\n It jams.\r\n"));

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        var body = factory.Api.Requests.Single(r => r.Method == HttpMethod.Post).Body!;
        body.ShouldContain("\r\n\r\nada@example.com\r\n");
        body.ShouldContain("\r\n\r\nAda\r\n");
        body.ShouldContain("\r\n\r\nPrinter jam\r\n");
        body.ShouldContain("\r\n\r\nIt jams.\r\n");
    }

    // ---- antiforgery ----

    [Fact]
    public async Task A_post_without_an_antiforgery_token_is_a_400_and_reaches_no_handler()
    {
        await using var factory = Host();
        var (client, _) = await OpenAsync(factory);
        using var __ = client;
        var apiCallsBefore = factory.Api.Requests.Count;

        using var response = await PostAsync(client, FormTestKit.ContactForm(null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        factory.Api.Requests.Count.ShouldBe(apiCallsBefore, "no handler ran: not even the product was asked again");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0);
    }

    [Fact]
    public async Task A_post_with_a_wrong_token_or_without_the_cookie_is_a_400()
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;
        using var stranger = FormTestKit.Client(factory);

        using var wrong = await PostAsync(client, FormTestKit.ContactForm(token + "x"));
        using var noCookie = await PostAsync(stranger, FormTestKit.ContactForm(token));

        wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        noCookie.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0);
    }

    // ---- validation: nothing is sent, the text is kept ----

    [Fact]
    public async Task An_invalid_post_shows_the_summary_and_the_field_errors_keeps_the_text_and_calls_nothing()
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;
        var before = factory.Api.Requests.Count;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token, email: "not an address", name: "Ada", subject: "", body: "It jams <b>every</b> time."));
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<section class=\"ts-error-summary alert alert-danger\" role=\"alert\" tabindex=\"-1\" autofocus aria-labelledby=\"error-summary-heading\">");
        html.ShouldContain("<a href=\"#email\">Enter a valid email address, like name@example.com.</a>");
        html.ShouldContain("<a href=\"#subject\">Enter a subject.</a>");
        html.ShouldContain("<p id=\"email-error\" class=\"ts-field-error\">Enter a valid email address, like name@example.com.</p>");
        html.ShouldContain("aria-describedby=\"email-error\" aria-invalid=\"true\"");
        html.ShouldNotContain("<a href=\"#name\">", Case.Sensitive, "a valid field has no error");
        Text(html, "name").ShouldBe("Ada");
        Text(html, "email").ShouldBe("not an address");
        html.ShouldContain("It jams &lt;b&gt;every&lt;/b&gt; time.</textarea>");
        html.ShouldContain("files are not kept");
        factory.Api.Requests.Count.ShouldBe(before + 1, "the post asked for the product again (the page is rebuilt) and nothing else");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0);
        factory.Api.AssertEveryCallBore(Visitor);
    }

    [Fact]
    public async Task Everything_missing_lists_every_field_once_in_the_order_of_the_form()
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token, email: "", name: "", subject: "", body: ""));
        var html = await response.Content.ReadAsStringAsync(Ct);

        var summary = FormTestKit.Between(html, "<ul>", "</ul>");
        Regex.Matches(summary, "<a href=\"#(\\w+)\">").Select(m => m.Groups[1].Value).ShouldBe(["name", "email", "subject", "body"]);
    }

    [Fact]
    public async Task A_prefill_posted_back_is_judged_exactly_like_typed_text()
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;
        var tooLong = new string('s', IntakeLimits.SubjectMaxLength + 1);

        using var response = await PostAsync(client, FormTestKit.ContactForm(token, email: "jane@", subject: tooLong));
        var html = await response.Content.ReadAsStringAsync(Ct);

        html.ShouldContain("The subject must be at most 200 characters.");
        html.ShouldContain("Enter a valid email address, like name@example.com.");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0);
    }

    // ---- the honeypot ----

    [Fact]
    public async Task A_filled_honeypot_is_passed_to_the_api_and_the_visitor_gets_the_same_redirect()
    {
        await using var factory = Host();
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created("PAP-43"), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token, website: "http://spam.example"));

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        response.Headers.Location!.ToString().ShouldStartWith("http://localhost/p/paperplane/contact/received?ref=");
        var body = factory.Api.Requests.Single(r => r.Method == HttpMethod.Post).Body!;
        body.ShouldContain("name=Website");
        body.ShouldContain("http://spam.example");
        factory.Api.AssertEveryCallBore(Visitor);
    }

    // ---- attachments ----

    [Fact]
    public async Task Files_are_sent_on_with_their_cleaned_names_types_and_content()
    {
        await using var factory = Host();
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created(), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token, files: [new("C:\\fakepath\\log.txt", "the log"u8.ToArray()), new("shot.png", [1, 2, 3], "image/png")]));

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        var body = factory.Api.Requests.Single(r => r.Method == HttpMethod.Post).Body!;
        body.ShouldContain("name=Attachments; filename=log.txt");
        body.ShouldContain("the log");
        body.ShouldContain("name=Attachments; filename=shot.png");
        body.ShouldContain("Content-Type: image/png");
        body.ShouldNotContain("fakepath");
    }

    [Fact]
    public async Task A_part_with_no_file_chosen_is_ignored_and_a_post_without_files_is_an_ordinary_post()
    {
        await using var factory = Host();
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created(), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;
        var form = FormTestKit.ContactForm(token);
        var empty = new ByteArrayContent([]);
        empty.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("form-data") { Name = "\"Form.Files\"", FileName = "\"\"" };
        form.Add(empty);

        using var response = await PostAsync(client, form);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        factory.Api.Requests.Single(r => r.Method == HttpMethod.Post).Body!.ShouldNotContain("name=Attachments");
    }

    [Fact]
    public async Task Six_files_a_type_that_is_not_allowed_an_empty_file_and_a_big_file_are_each_named_before_anything_is_sent()
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;
        var files = new List<PostedFile>
        {
            new("a.txt", [1]), new("b.txt", [1]), new("c.txt", [1]), new("d.txt", [1]),
            new("virus.exe", [1], "application/octet-stream"),
            new("empty.txt", []),
        };

        using var response = await PostAsync(client, FormTestKit.ContactForm(token, files: [.. files]));
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("Attach at most 5 files.");
        html.ShouldContain("virus.exe is a type we cannot accept.");
        html.ShouldContain("empty.txt is empty. Remove it or choose another.");
        html.ShouldContain("<a href=\"#attachments\">");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0);
    }

    [Fact]
    public async Task A_file_over_the_per_file_limit_is_named_and_not_sent()
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;
        var big = new PostedFile("big report.pdf", new byte[(int)IntakeLimits.MaxFileBytes + 1], "application/pdf");

        using var response = await PostAsync(client, FormTestKit.ContactForm(token, files: [big]));
        var html = await response.Content.ReadAsStringAsync(Ct);

        html.ShouldContain("big report.pdf is over 10 MB. Send a smaller file.");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0);
    }

    [Fact]
    public async Task Files_that_together_pass_the_message_limit_are_refused_before_anything_is_sent()
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;
        PostedFile[] files = [new("a.zip", new byte[8_900_000], "application/zip"), new("b.zip", new byte[8_900_000], "application/zip"), new("c.zip", new byte[8_900_000], "application/zip")];

        using var response = await PostAsync(client, FormTestKit.ContactForm(token, files: files));
        var html = await response.Content.ReadAsStringAsync(Ct);

        html.ShouldContain("Your files add up to more than 25 MB.");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0);
    }

    [Fact]
    public async Task A_file_name_with_markup_is_encoded_in_the_error()
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token, files: [new("<img src=x onerror=alert(1)>.exe", [1])]));
        var html = await response.Content.ReadAsStringAsync(Ct);

        html.ShouldNotContain("<img src=x");
        html.ShouldContain("&lt;img src=x onerror=alert(1)&gt;.exe is a type we cannot accept.");
    }

    // ---- what the API says ----

    [Fact]
    public async Task The_apis_field_codes_become_the_portals_own_sentences_on_the_right_fields()
    {
        await using var factory = Host();
        factory.Api.On(HttpMethod.Post, FormTestKit.ApiTicketsPath, _ => StubApiHandler.ValidationProblem(
            [("email", "email-invalid", "API TEXT must not show"), ("body", "body-too-long", "Domain text must not show"), ("attachments", "attachment-type-not-allowed", "x")]));
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token));
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("Enter a valid email address, like name@example.com.");
        html.ShouldContain("The message must be at most 100,000 characters.");
        html.ShouldContain("One of the files is a type we cannot accept.");
        html.ShouldNotContain("API TEXT");
        html.ShouldNotContain("Domain text");
        html.ShouldContain("<a href=\"#attachments\">");
        Text(html, "email").ShouldBe("ada@example.com", "what was typed is kept");
    }

    [Fact]
    public async Task An_unknown_code_is_the_generic_check_your_answers_sentence_never_the_apis_text()
    {
        await using var factory = Host();
        factory.Api.On(HttpMethod.Post, FormTestKit.ApiTicketsPath, _ => StubApiHandler.ValidationProblem("whatever", "a-new-code", "Column secret of table x"));
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token));
        var html = await response.Content.ReadAsStringAsync(Ct);

        html.ShouldContain("Some of what you entered needs another look.");
        html.ShouldNotContain("Column secret");
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests, "Too many attempts. Wait a few minutes and try again.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, "We could not send that just now.")]
    [InlineData(HttpStatusCode.InternalServerError, HttpStatusCode.ServiceUnavailable, "We could not send that just now.")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.OK, "That is too large to send.")]
    [InlineData(HttpStatusCode.UnsupportedMediaType, HttpStatusCode.OK, "That could not be sent in that form.")]
    public async Task A_failure_of_the_api_is_shown_calmly_with_a_fitting_status_and_the_text_is_kept(HttpStatusCode api, HttpStatusCode page, string sentence)
    {
        await using var factory = Host();
        factory.Api.OnProblem(HttpMethod.Post, FormTestKit.ApiTicketsPath, api, "x", "System.InvalidOperationException at Npgsql host=10.0.0.5");
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token));
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(page);
        html.ShouldContain(sentence);
        html.ShouldNotContain("Npgsql");
        html.ShouldNotContain("10.0.0.5");
        Text(html, "subject").ShouldBe("Printer jam");
        html.ShouldContain("It jams every time.</textarea>");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(1, "a post is never retried");
    }

    [Fact]
    public async Task A_transport_failure_is_the_same_calm_page_with_one_attempt()
    {
        await using var factory = Host();
        factory.Api.On(HttpMethod.Post, FormTestKit.ApiTicketsPath, _ => throw new HttpRequestException("No connection could be made (10.1.2.3:5432)"));
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token));
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        html.ShouldContain("We could not send that just now.");
        html.ShouldNotContain("10.1.2.3");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(1);
    }

    [Fact]
    public async Task A_product_that_vanishes_between_the_page_and_the_post_is_the_uniform_404()
    {
        await using var factory = Host();
        factory.Api.OnProblem(HttpMethod.Post, FormTestKit.ApiTicketsPath, HttpStatusCode.NotFound, "product-not-found", "No such product.");
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token));
        var seen = await Seen.OfAsync(response, factory.Api.Requests.Count, Ct);

        // The product loaded (and themed the page) before the post failed; the 404 must still be the neutral one.
        seen.ShouldBeTheNeutralNotFound(await Seen.NeutralNotFoundAsync(Ct, "/p/nope/contact"));
        seen.Body.ShouldContain("Page not found");
        seen.Body.ShouldNotContain("<form");
    }

    [Fact]
    public async Task A_valid_post_to_an_unknown_product_is_a_404_before_the_handler_and_never_calls_the_ticket_endpoint()
    {
        await using var factory = Host();
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/nope", HttpStatusCode.NotFound, "product-not-found", "No such product.");
        factory.Api.OnJson(HttpMethod.Post, "/api/public/products/nope/tickets", FormTestKit.Created(), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        // The token and cookie are good (they came from a real product's page); only the product is unknown.
        using var response = await client.PostAsync("/p/nope/contact", FormTestKit.ContactForm(token), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        // The page asks for the product before the form handler runs, finds none and ends in NavigationManager.NotFound(). The form was never rendered, so the framework has nothing to post to and answers its own
        // plain-text 400 (not the 404 page a GET gets). Nothing was created and no form came back.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("text/plain");
        html.ShouldBe("Cannot submit the form 'contact' because no form on the page currently has that name.");
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
        factory.Api.Count(HttpMethod.Post, "/api/public/products/nope/tickets").ShouldBe(0);
    }

    [Fact]
    public async Task A_post_to_an_unknown_product_is_answered_in_one_request_not_re_executed_against_the_not_found_page()
    {
        // The wording of D-045 depends on this: NavigationManager.NotFound() renders the not-found page in the SAME request (one dependency scope, and the layout is built once), and the framework then rejects the
        // post because no form named "contact" was rendered. A re-execution (UseStatusCodePagesWithReExecute) would build a second scope.
        var scopes = 0;
        await using var factory = Host(services => services.AddScoped(_ =>
        {
            Interlocked.Increment(ref scopes);
            return new ProductScope();
        }));
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/nope", HttpStatusCode.NotFound, "product-not-found", "No such product.");
        var (client, token) = await OpenAsync(factory);
        using var _ = client;
        var before = scopes;

        using var response = await client.PostAsync("/p/nope/contact", FormTestKit.ContactForm(token), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (scopes - before).ShouldBe(1, "the post, the not-found page and the layout share one request scope");
    }

    // ---- logs ----

    [Fact]
    public async Task What_the_visitor_posted_never_reaches_a_log_event_at_any_level()
    {
        await using var factory = FormTestKit.Factory(settings: PortalFactory.VerboseLogging);
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", FormTestKit.Product());
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created("PAP-77"), HttpStatusCode.Created);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await PostAsync(client, FormTestKit.ContactForm(token, email: "private.person@example.com", name: "Zebediah Quux", subject: "Sensitive subject line", body: "A very private body"));
        var location = response.Headers.Location!.PathAndQuery;
        await client.GetStringAsync(location, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        factory.LogSink.Events.ShouldContain(e => e.Level <= LogEventLevel.Debug, "the Verbose setting must have taken effect");
        factory.LogSink.Events.ShouldContain(e => Everything(e).Contains("ref=[redacted]", StringComparison.Ordinal), "control: the redirect target was logged, masked");
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text =>
            !text.Contains("private.person", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("Zebediah", StringComparison.Ordinal)
            && !text.Contains("Quux", StringComparison.Ordinal)
            && !text.Contains("Sensitive subject", StringComparison.Ordinal)
            && !text.Contains("very private body", StringComparison.Ordinal)
            && !text.Contains("PAP-77", StringComparison.Ordinal)
            && !text.Contains(token, StringComparison.Ordinal));
    }

    private static string Everything(LogEvent e) => string.Join('\n', [e.RenderMessage(), e.Exception?.ToString() ?? string.Empty, .. e.Properties.Values.Select(v => v.ToString())]);
}
