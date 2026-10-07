using System.Net;
using TechStrap.Contracts.Intake;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>
/// P09-T06 and T21 at the host: the contact page as a visitor first sees it. A themed product page with a labelled, antiforgery-protected multipart form, the limits of the API on its inputs, an honeypot a person
/// cannot reach, the attachment rule stated before a file is picked, and the prefill (<c>?subject&amp;name&amp;email</c>) in visible, editable inputs: validated like typed text, never hidden, never echoed from any
/// other parameter and never submitted for the visitor.
/// </summary>
public sealed class ContactPageHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task<(HttpResponseMessage Response, string Html)> GetAsync(PortalFactory factory, string path)
    {
        using var client = FormTestKit.Client(factory);
        var response = await client.GetAsync(path, Ct);
        return (response, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task The_page_is_themed_and_has_a_labelled_antiforgery_protected_multipart_form()
    {
        await using var factory = FormTestKit.Factory();

        var (response, html) = await GetAsync(factory, FormTestKit.Path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<title>Contact Paperplane support</title>");
        html.ShouldContain("--ts-accent:#F59E0B");
        html.ShouldContain("<h1>Contact support</h1>");
        html.ShouldContain("<form method=\"post\" action=\"/p/paperplane/contact\" enctype=\"multipart/form-data\" novalidate");
        html.ShouldContain("name=\"__RequestVerificationToken\"");
        html.ShouldContain("name=\"_handler\" value=\"contact\"");
        foreach (var (id, text) in new[] { ("name", "Your name"), ("email", "Email address"), ("subject", "Subject"), ("body", "Message"), ("attachments", "Attachments (optional)") })
        {
            html.ShouldContain($"<label for=\"{id}\" class=\"form-label\">{text}</label>");
            html.ShouldContain($"id=\"{id}\"");
        }

        html.ShouldContain("<button type=\"submit\" class=\"btn btn-primary\">Send message</button>");
        html.ShouldContain("href=\"/p/paperplane/kb\">Browse help articles</a>");
        html.ShouldNotContain("<script>", Case.Sensitive, "the form works without script");
        factory.Api.Count(HttpMethod.Get, "/api/public/products/paperplane").ShouldBe(1);
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0, "opening the page never creates anything");
    }

    [Fact]
    public async Task The_inputs_carry_the_apis_limits_and_the_right_keyboards_and_autocomplete_values()
    {
        await using var factory = FormTestKit.Factory();

        var (_, html) = await GetAsync(factory, FormTestKit.Path);

        html.ShouldContain($"name=\"Form.Name\" type=\"text\" class=\"form-control\" maxlength=\"{IntakeLimits.NameMaxLength}\" autocomplete=\"name\"");
        html.ShouldContain($"name=\"Form.Email\" type=\"email\" class=\"form-control\" maxlength=\"{IntakeLimits.EmailMaxLength}\" autocomplete=\"email\"");
        html.ShouldContain($"name=\"Form.Subject\" type=\"text\" class=\"form-control\" maxlength=\"{IntakeLimits.SubjectMaxLength}\"");
        html.ShouldContain($"name=\"Form.Body\" class=\"form-control\" rows=\"8\" maxlength=\"{IntakeLimits.BodyMaxLength}\"");
    }

    [Fact]
    public async Task The_attachment_rule_is_stated_before_a_file_is_picked_and_the_input_is_a_plain_multiple_file_input()
    {
        await using var factory = FormTestKit.Factory();

        var (_, html) = await GetAsync(factory, FormTestKit.Path);

        html.ShouldContain("Up to 5 files: images and documents of 10 MB each and 25 MB in all (.png, .jpg, .jpeg, .gif, .webp, .pdf, .txt, .log, .csv, .zip).");
        html.ShouldContain("name=\"Form.Files\" type=\"file\" multiple accept=\".png,.jpg,.jpeg,.gif,.webp,.pdf,.txt,.log,.csv,.zip\"");
        html.ShouldContain("aria-describedby=\"attachments-hint\"");
    }

    [Fact]
    public async Task The_honeypot_is_out_of_sight_out_of_the_tab_order_out_of_assistive_technology_and_not_autofilled()
    {
        await using var factory = FormTestKit.Factory();

        var (_, html) = await GetAsync(factory, FormTestKit.Path);

        html.ShouldContain("<div class=\"ts-hp\" aria-hidden=\"true\">");
        html.ShouldContain("<input id=\"contact-website\" name=\"Form.Website\" type=\"text\" tabindex=\"-1\" autocomplete=\"off\"");
        html.ShouldNotContain("style=\"position", Case.Sensitive, "the CSP allows no inline style: the class is in the style sheet");
    }

    [Fact]
    public async Task The_page_is_noindex_and_no_store_and_keeps_the_shared_headers_and_the_normal_policy()
    {
        await using var factory = FormTestKit.Factory();

        var (response, _) = await GetAsync(factory, FormTestKit.Path);

        response.Headers.GetValues("X-Robots-Tag").Single().ShouldBe("noindex");
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("strict-origin-when-cross-origin");
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        policy.ShouldNotContain("sandbox");
        policy.ShouldContain("form-action 'self'");
    }

    [Fact]
    public async Task The_visitors_address_behind_a_trusted_proxy_reaches_the_api_on_the_product_call_of_the_page()
    {
        await using var factory = FormTestKit.Factory();
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync(FormTestKit.Path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Api.AssertEveryCallBore(FormTestKit.Visitor);
    }

    [Theory]
    [InlineData("/p/nope/contact")]
    [InlineData("/p/Paperplane/contact")]
    [InlineData("/p/paper%20plane/contact")]
    public async Task An_unknown_inactive_or_malformed_product_is_the_uniform_404_and_shows_no_form(string path)
    {
        await using var factory = FormTestKit.Factory(product: false);
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/nope", HttpStatusCode.NotFound, "product-not-found", "No such product.");

        var (response, html) = await GetAsync(factory, path);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        html.ShouldContain("Page not found");
        html.ShouldNotContain("<form");
        html.ShouldNotContain("--ts-accent");
        response.Headers.GetValues("X-Robots-Tag").Single().ShouldBe("noindex");
    }

    [Fact]
    public async Task When_the_api_cannot_be_asked_the_page_says_so_calmly_and_shows_no_form()
    {
        await using var factory = FormTestKit.Factory("Production", product: false);
        factory.Api.OnStatus(HttpMethod.Get, "/api/public/products/paperplane", HttpStatusCode.ServiceUnavailable);

        var (response, html) = await GetAsync(factory, FormTestKit.Path);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        html.ShouldContain("This page could not be loaded.");
        html.ShouldNotContain("<form");
    }

    // ---- P09-T21: the prefill ----

    [Fact]
    public async Task The_prefill_fills_three_visible_editable_inputs_and_nothing_else()
    {
        await using var factory = FormTestKit.Factory();

        var (response, html) = await GetAsync(factory, FormTestKit.Path + "?subject=Printer%20jam&name=Jane%20Doe&email=jane%40example.com");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("name=\"Form.Subject\" type=\"text\" class=\"form-control\" value=\"Printer jam\" maxlength=\"200\"");
        html.ShouldContain("name=\"Form.Name\" type=\"text\" class=\"form-control\" value=\"Jane Doe\" maxlength=\"100\"");
        html.ShouldContain("name=\"Form.Email\" type=\"email\" class=\"form-control\" value=\"jane@example.com\" maxlength=\"320\"");
        // The one hidden Form field is the double-send id (a random value, never prefill data).
        System.Text.RegularExpressions.Regex.IsMatch(html, "type=\"hidden\" name=\"Form\\.(?!SubmitId\")").ShouldBeFalse("no hidden field carries prefill data");
        html.ShouldNotContain("value=\"Printer jam\" type=\"hidden\"");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0, "a prefill never submits anything");
    }

    [Fact]
    public async Task Prefilled_markup_is_encoded_wherever_it_appears()
    {
        await using var factory = FormTestKit.Factory();

        var (_, html) = await GetAsync(factory, FormTestKit.Path + "?subject=%22%20autofocus%20onfocus%3Dalert(1)%20x%3D%22&name=%3Cscript%3Ealert(1)%3C%2Fscript%3E&email=a%22%3E%3Cimg%20src%3Dx%3E");

        html.ShouldNotContain("<script>alert(1)");
        html.ShouldNotContain("<img src=x>");
        html.ShouldNotContain("\" autofocus onfocus", Case.Sensitive, "the quote is encoded, so the value cannot end its attribute");
        html.ShouldContain("&lt;script&gt;alert(1)&lt;/script&gt;");
        html.ShouldContain("value=\"&quot; autofocus onfocus=alert(1) x=&quot;\"");
    }

    [Fact]
    public async Task Unknown_parameters_change_nothing_and_are_never_echoed()
    {
        await using var factory = FormTestKit.Factory();

        var (_, plain) = await GetAsync(factory, FormTestKit.Path);
        var (_, extra) = await GetAsync(factory, FormTestKit.Path + "?product=orbitly&token=AbC-_0123456789AbC-_0123456789AbC-_01234567&handler=other&Website=spam&ref=1");

        extra.ShouldNotContain("orbitly");
        extra.ShouldNotContain("AbC-_0123456789");
        extra.ShouldNotContain("spam");
        // The same page, token for token (the antiforgery value and the double-send id differ every time).
        static string Same(string page) => System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(page, "value=\"CfDJ[^\"]+\"", "value=\"T\""), "(name=\"Form.SubmitId\" value=\")[^\"]+", "$1I");
        Same(extra).ShouldBe(Same(plain));
        factory.Api.Count(HttpMethod.Get, "/api/public/products/paperplane").ShouldBe(2, "the product is the page's own, never the query's");
    }

    [Fact]
    public async Task A_prefill_longer_than_the_inputs_limit_is_shown_as_it_came_and_judged_on_post()
    {
        await using var factory = FormTestKit.Factory();
        var subject = new string('s', IntakeLimits.SubjectMaxLength + 50);

        var (response, html) = await GetAsync(factory, FormTestKit.Path + "?subject=" + subject);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain($"value=\"{subject}\" maxlength=\"200\"");
    }

    [Fact]
    public async Task The_prefill_values_never_reach_the_api_or_a_log_until_the_visitor_posts_them()
    {
        await using var factory = FormTestKit.Factory();

        await GetAsync(factory, FormTestKit.Path + "?subject=Printer&name=Jane&email=jane%40example.com");

        factory.Api.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/public/products/paperplane");
        factory.Api.Requests.Single().Query.ShouldBeEmpty("the Portal sends the API the product key and nothing from the query");
    }
}
