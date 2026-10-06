using System.Net;
using System.Text.RegularExpressions;
using TechStrap.Portal.Tests.Forms;

namespace TechStrap.Portal.Tests.Suggestions;

/// <summary>
/// P09-T07 at the host: the contact page carries the <c>ts-kb-suggestions</c> element beside the subject with its fallback inside, loads the module as a plain script file (no CSP change), the module is served as
/// JavaScript, and no other page loads it. The form itself never depends on the element: every post test of the contact page runs without script.
/// </summary>
public sealed class ContactSuggestionsHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task<(HttpResponseMessage Response, string Html)> GetAsync(PortalFactory factory, string path)
    {
        using var client = FormTestKit.Client(factory);
        var response = await client.GetAsync(path, Ct);
        return (response, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task The_element_sits_after_the_subject_field_with_its_field_its_url_a_polite_live_region_and_the_fallback_link_inside()
    {
        await using var factory = FormTestKit.Factory();

        var (_, html) = await GetAsync(factory, FormTestKit.Path);

        html.ShouldContain("<ts-kb-suggestions field=\"subject\" src=\"/p/paperplane/suggest\" class=\"ts-suggestions\" aria-live=\"polite\"><a href=\"/p/paperplane/kb/search\">Search the help articles first</a></ts-kb-suggestions>");
        html.IndexOf("id=\"subject\"", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("<ts-kb-suggestions", StringComparison.Ordinal));
        html.IndexOf("<ts-kb-suggestions", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("id=\"body\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_element_url_is_built_from_the_product_key_and_the_prefill_never_reaches_it()
    {
        await using var factory = FormTestKit.Factory();

        var (_, html) = await GetAsync(factory, FormTestKit.Path + "?subject=Printer&name=Jane&email=jane%40example.com");

        var element = Regex.Match(html, "<ts-kb-suggestions[^>]*>").Value;
        element.ShouldContain("src=\"/p/paperplane/suggest\"");
        element.ShouldNotContain("Printer");
        element.ShouldNotContain("jane");
    }

    [Fact]
    public async Task The_page_loads_the_module_as_a_script_file_and_has_no_inline_script()
    {
        await using var factory = FormTestKit.Factory();

        var (response, html) = await GetAsync(factory, FormTestKit.Path);

        var script = Regex.Match(html, "<script type=\"module\" src=\"([^\"]*kb-suggestions[^\"]*\\.js)\"></script>");
        script.Success.ShouldBeTrue("the contact page must load the module");
        Regex.Matches(html, "<script(?![^>]*\\bsrc=)").Count.ShouldBe(0, "no inline script");
        var directives = response.Headers.GetValues("Content-Security-Policy").Single().Split(';', StringSplitOptions.TrimEntries);
        directives.ShouldContain("script-src 'self'", "the module is a same-origin file: the policy is unchanged");
        directives.ShouldContain("connect-src 'self'");
    }

    [Fact]
    public async Task The_module_is_served_as_javascript_with_the_element_in_it_and_no_markup_from_text()
    {
        await using var factory = FormTestKit.Factory();
        var (_, html) = await GetAsync(factory, FormTestKit.Path);
        var src = Regex.Match(html, "<script type=\"module\" src=\"([^\"]*kb-suggestions[^\"]*\\.js)\"></script>").Groups[1].Value;
        using var client = FormTestKit.Client(factory);

        using var fingerprinted = await client.GetAsync("/" + src.TrimStart('/'), Ct);
        using var plain = await client.GetAsync("/js/kb-suggestions.js", Ct);

        foreach (var response in new[] { fingerprinted, plain })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("text/javascript");
            var text = await response.Content.ReadAsStringAsync(Ct);
            text.ShouldContain("ts-kb-suggestions");
            text.ShouldNotContain("innerHTML");
        }
    }

    [Theory]
    [InlineData("/p/paperplane")]
    [InlineData("/p/paperplane/contact/received")]
    [InlineData("/p/paperplane/lost-link")]
    public async Task No_other_page_loads_the_module(string path)
    {
        await using var factory = FormTestKit.Factory();

        var (_, html) = await GetAsync(factory, path);

        html.ShouldNotContain("kb-suggestions");
    }

    [Fact]
    public async Task The_form_still_posts_with_the_element_present_because_the_element_is_not_part_of_the_post()
    {
        await using var factory = FormTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created(), HttpStatusCode.Created);
        using var client = FormTestKit.Client(factory);
        var token = await FormTestKit.TokenAsync(client, FormTestKit.Path, Ct);

        using var response = await client.PostAsync(FormTestKit.Path, FormTestKit.ContactForm(token), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        factory.Api.Requests.Where(r => r.Path.Contains("/kb/")).ShouldBeEmpty("the page never searches by itself");
    }
}
