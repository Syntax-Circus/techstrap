using System.Net;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Forms;
using TechStrap.Portal.Tests.Api;
using TechStrap.Portal.Tests.Forms;

namespace TechStrap.Portal.Tests.Tickets;

/// <summary>
/// P09-T10 at the host: the lost-link page. For any well-formed address the Portal shows the same confirmation, byte for byte, because it never looks at anything the API answered beyond success (Review Focus 2); a
/// malformed address is an ordinary field error and the API is not asked; a 429 or an outage is a calm notice. The page is themed, noindex and no-store, and every call forwards the visitor's address. Timing is out
/// of scope (D-038 accepts the residual difference).
/// </summary>
public sealed class LostLinkHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string Path = "/p/paperplane/lost-link";
    private const string LinkApi = "/api/customer/access-link";
    private const string Visitor = FormTestKit.Visitor;

    private static PortalFactory Host() => FormTestKit.Factory();

    private static MultipartFormDataContent Form(string? token, string? email = "ada@example.com")
    {
        var form = new MultipartFormDataContent { { new StringContent("lost-link"), "_handler" } };
        if (token is not null)
        {
            form.Add(new StringContent(token), "__RequestVerificationToken");
        }

        if (email is not null)
        {
            form.Add(new StringContent(email), "Form.Email");
        }

        return form;
    }

    private static async Task<(HttpClient Client, string Token)> OpenAsync(PortalFactory factory)
    {
        var client = FormTestKit.Client(factory);
        return (client, await FormTestKit.TokenAsync(client, Path, Ct));
    }

    [Fact]
    public async Task The_page_is_themed_and_has_a_labelled_antiforgery_protected_form_and_the_form_page_headers()
    {
        await using var factory = FormTestKit.Factory();
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync(Path, Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<title>New ticket link: Paperplane</title>");
        html.ShouldContain("--ts-accent:#F59E0B");
        html.ShouldContain("<h1>Lost your ticket link?</h1>");
        html.ShouldContain("<form method=\"post\" action=\"/p/paperplane/lost-link\" novalidate");
        html.ShouldContain("name=\"__RequestVerificationToken\"");
        html.ShouldContain("name=\"_handler\" value=\"lost-link\"");
        html.ShouldContain("<label for=\"email\" class=\"form-label\">Email address</label>");
        html.ShouldContain("name=\"Form.Email\" type=\"email\" class=\"form-control\" maxlength=\"320\" autocomplete=\"email\"");
        html.ShouldContain("<button type=\"submit\" class=\"btn btn-primary\">Send me a new link</button>");
        html.ShouldNotContain("Confirmation");
        response.Headers.GetValues("X-Robots-Tag").Single().ShouldBe("noindex");
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
    }

    [Fact]
    public async Task A_post_asks_the_api_once_through_the_write_client_with_no_token_and_redirects_to_the_confirmation()
    {
        await using var factory = Host();
        factory.Api.OnStatus(HttpMethod.Post, LinkApi, HttpStatusCode.Accepted);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(Path, Form(token, "  ada@example.com "), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        response.Headers.Location!.ToString().ShouldBe("http://localhost/p/paperplane/lost-link?sent=1");
        var sent = factory.Api.Requests.Single(r => r.Method == HttpMethod.Post);
        sent.Client.ShouldBe(ApiClientNames.Write);
        sent.TicketToken.ShouldBeNull();
        sent.Path.ShouldBe(LinkApi);
        sent.Body.ShouldNotBeNull().ShouldContain("\"email\":\"ada@example.com\"");
        factory.Api.AssertEveryCallBore(Visitor);
    }

    [Fact]
    public async Task The_response_to_a_known_and_an_unknown_address_is_byte_identical_whatever_the_api_says_besides_success()
    {
        async Task<(string Redirect, string Page)> RunAsync(Func<HttpResponseMessage> answer, string email)
        {
            await using var factory = Host();
            factory.Api.On(HttpMethod.Post, LinkApi, _ => answer());
            var (client, token) = await OpenAsync(factory);
            using var _ = client;
            using var posted = await client.PostAsync(Path, Form(token, email), Ct);
            posted.StatusCode.ShouldBe(HttpStatusCode.Found);
            var location = posted.Headers.Location!;
            var page = await client.GetStringAsync(location.PathAndQuery, Ct);
            return (location.ToString() + string.Join(",", posted.Headers.Select(h => h.Key)), page);
        }

        // The API answers 202 whatever the address; these answers differ in everything the Portal could (wrongly) look at.
        var known = await RunAsync(() => new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("{\"sent\":true,\"matches\":3}") }, "known@example.com");
        var unknown = await RunAsync(() => new HttpResponseMessage(HttpStatusCode.Accepted), "nobody@example.org");
        var odd = await RunAsync(() => new HttpResponseMessage(HttpStatusCode.NoContent) { Content = new StringContent("different body") }, "x@y.example");

        unknown.Page.ShouldBe(known.Page, "byte for byte");
        odd.Page.ShouldBe(known.Page);
        unknown.Redirect.ShouldBe(known.Redirect);
        odd.Redirect.ShouldBe(known.Redirect);
        known.Page.ShouldContain("If we have tickets for that address, we have sent a new link. Check your inbox and your spam folder.");
        known.Page.ShouldNotContain("known@example.com");
        known.Page.ShouldNotContain("nobody@example.org");
        known.Page.ShouldNotContain("<form", Case.Sensitive, "no form, so no per-request antiforgery value to make two responses differ");
    }

    [Fact]
    public async Task The_confirmation_page_has_a_link_back_and_the_form_page_headers()
    {
        await using var factory = FormTestKit.Factory();
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync(Path + "?sent=1", Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("role=\"status\"");
        html.ShouldContain("href=\"/p/paperplane\">Back to Paperplane</a>");
        response.Headers.GetValues("X-Robots-Tag").Single().ShouldBe("noindex");
        factory.Api.Requests.Select(r => r.Path).ShouldBe(["/api/public/products/paperplane"], "opening the confirmation asks nothing about any address");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not an address")]
    [InlineData("ada@")]
    [InlineData("ada@example")]
    public async Task A_malformed_address_is_a_field_error_and_the_api_is_not_asked(string email)
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(Path, Form(token, email), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<a href=\"#email\">");
        html.ShouldContain("class=\"ts-field-error\"");
        html.ShouldNotContain("role=\"status\"", Case.Sensitive, "no confirmation for a malformed address");
        factory.Api.Count(HttpMethod.Post, LinkApi).ShouldBe(0);
    }

    [Fact]
    public async Task What_was_typed_is_kept_encoded_when_the_address_is_refused()
    {
        await using var factory = Host();
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(Path, Form(token, "\"><script>alert(1)</script>"), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        html.ShouldNotContain("<script>alert(1)");
        html.ShouldContain("value=\"&quot;&gt;&lt;script&gt;alert(1)&lt;/script&gt;\"");
    }

    [Fact]
    public async Task An_address_the_api_calls_invalid_is_the_same_field_error()
    {
        await using var factory = Host();
        factory.Api.On(HttpMethod.Post, LinkApi, _ => StubApiHandler.ValidationProblem("email", "email-invalid", "API TEXT"));
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(Path, Form(token, "ada@example.com"), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        html.ShouldContain("Enter a valid email address, like name@example.com.");
        html.ShouldNotContain("API TEXT");
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests, "Too many attempts.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, "We could not send that just now.")]
    [InlineData(HttpStatusCode.NotFound, HttpStatusCode.ServiceUnavailable, "We could not send that just now.")]
    public async Task A_429_or_an_outage_is_a_calm_notice_with_a_fitting_status_and_the_address_kept(HttpStatusCode api, HttpStatusCode page, string sentence)
    {
        await using var factory = Host();
        factory.Api.OnProblem(HttpMethod.Post, LinkApi, api, "x", "Npgsql host=10.0.0.5");
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(Path, Form(token, "ada@example.com"), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(page);
        html.ShouldContain(sentence);
        html.ShouldNotContain("Npgsql");
        html.ShouldContain("value=\"ada@example.com\"");
        factory.Api.Count(HttpMethod.Post, LinkApi).ShouldBe(1, "never retried");
    }

    [Fact]
    public async Task A_post_without_an_antiforgery_token_is_a_400_and_asks_nothing()
    {
        await using var factory = Host();
        var (client, _) = await OpenAsync(factory);
        using var __ = client;

        using var response = await client.PostAsync(Path, Form(null), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        factory.Api.Count(HttpMethod.Post, LinkApi).ShouldBe(0);
    }

    [Theory]
    [InlineData("/p/nope/lost-link")]
    [InlineData("/p/Paperplane/lost-link")]
    public async Task An_unknown_inactive_or_malformed_product_is_the_uniform_404(string path)
    {
        await using var factory = FormTestKit.Factory(product: false);
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/nope", HttpStatusCode.NotFound, "product-not-found", "No such product.");
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("Page not found");
    }

    [Fact]
    public async Task The_footer_link_of_every_product_page_reaches_this_page()
    {
        await using var factory = FormTestKit.Factory();
        using var client = FormTestKit.Client(factory);

        var home = await client.GetStringAsync("/p/paperplane", Ct);

        home.ShouldContain("href=\"/p/paperplane/lost-link\"");
    }

    [Fact]
    public async Task The_address_never_reaches_a_log_event_at_any_level()
    {
        await using var factory = FormTestKit.Factory(settings: PortalFactory.VerboseLogging);
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", FormTestKit.Product());
        factory.Api.OnStatus(HttpMethod.Post, LinkApi, HttpStatusCode.Accepted);
        var (client, token) = await OpenAsync(factory);
        using var _ = client;

        using var response = await client.PostAsync(Path, Form(token, "private.person@example.com"), Ct);
        await client.GetStringAsync(response.Headers.Location!.PathAndQuery, Ct);

        factory.LogSink.Events.ShouldContain(e => e.Level <= Serilog.Events.LogEventLevel.Debug);
        factory.LogSink.Events.Select(e => string.Join('\n', [e.RenderMessage(), .. e.Properties.Values.Select(v => v.ToString())]))
            .ShouldAllBe(text => !text.Contains("private.person", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_lost_link_copy_says_the_same_thing_whatever_the_address()
    {
        LostLinkCopy.Confirmation.ShouldBe("If we have tickets for that address, we have sent a new link. Check your inbox and your spam folder.");
        LostLinkCopy.Confirmation.ShouldNotContain("no account", Case.Insensitive);
        LostLinkCopy.Confirmation.ShouldNotContain("not found", Case.Insensitive);
    }
}
