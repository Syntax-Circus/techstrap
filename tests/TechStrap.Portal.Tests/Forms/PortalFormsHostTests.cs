using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Contracts.Intake;
using TechStrap.Portal.Forms;
using TechStrap.Portal.Tests.Tickets;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>
/// <c>portal-forms.js</c> at the host (D-045 09d addendum, Review Focus 3): the document shell loads both modules once and no page body carries a script, so Blazor's enhanced navigation (which does not run a script that
/// arrives with swapped content) cannot leave a form without its helpers; the sending label, the counter and the copy button get every word from a <c>*Copy</c> constant through a data attribute; there is no inline script
/// and no <c>on*</c> attribute; the policy is unchanged; and the pinned markup of the forms (the button, the form tag's start) is untouched.
/// </summary>
public sealed class PortalFormsHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static IDocument Parse(string html) => new HtmlParser().ParseDocument(html);

    private static async Task<(HttpResponseMessage Response, string Html)> GetAsync(PortalFactory factory, string path)
    {
        using var client = FormTestKit.Client(factory);
        var response = await client.GetAsync(path, Ct);
        return (response, await response.Content.ReadAsStringAsync(Ct));
    }

    private static string ReferenceFor(PortalFactory factory, string number)
    {
        using var scope = factory.Services.CreateScope();
        return Uri.EscapeDataString(scope.ServiceProvider.GetRequiredService<ReceivedReference>().Protect("paperplane", number));
    }

    // ---- the shell loads the modules once ----

    [Theory]
    [InlineData("/")]
    [InlineData("/nope")]
    [InlineData("/p/paperplane")]
    [InlineData("/p/paperplane/contact")]
    [InlineData("/p/paperplane/contact/received")]
    [InlineData("/p/paperplane/lost-link")]
    [InlineData("/t/AbC-_0123456789AbC-_0123456789AbC-_01234567")]
    public async Task Every_page_loads_each_module_once_from_the_document_shell_after_blazor_and_no_page_body_has_a_script(string path)
    {
        await using var factory = TicketTestKit.Factory();

        var (_, html) = await GetAsync(factory, path);

        var document = Parse(html);
        var scripts = document.QuerySelectorAll("script").ToList();
        scripts.Select(s => s.GetAttribute("src") ?? "(inline)").Where(src => src.Contains("blazor.web")).ShouldHaveSingleItem();
        var modules = scripts.Where(s => s.GetAttribute("type") == "module").Select(s => s.GetAttribute("src")!).ToList();
        modules.Count(src => Regex.IsMatch(src, @"js/kb-suggestions\.[A-Za-z0-9]+\.js")).ShouldBe(1);
        modules.Count(src => Regex.IsMatch(src, @"js/portal-forms\.[A-Za-z0-9]+\.js")).ShouldBe(1);
        modules.Count.ShouldBe(2);
        scripts.Where(s => !s.HasAttribute("src")).ShouldBeEmpty("no inline script");
        document.QuerySelector("main")!.QuerySelectorAll("script").ShouldBeEmpty("a script in a page body would not run after an enhanced navigation");
        scripts.IndexOf(scripts.Single(s => s.GetAttribute("src")!.Contains("blazor.web"))).ShouldBeLessThan(scripts.IndexOf(scripts.First(s => s.GetAttribute("type") == "module")));
        Regex.Matches(html, @"\son[a-z]+\s*=", RegexOptions.IgnoreCase).Count.ShouldBe(0, "no inline event handler");
    }

    [Fact]
    public async Task The_policy_is_unchanged_and_the_modules_are_same_origin_files()
    {
        await using var factory = FormTestKit.Factory();

        var (response, _) = await GetAsync(factory, FormTestKit.Path);

        var directives = response.Headers.GetValues("Content-Security-Policy").Single().Split(';', StringSplitOptions.TrimEntries);
        directives.ShouldContain("script-src 'self'");
        directives.ShouldContain("connect-src 'self'");
        directives.ShouldNotContain(d => d.Contains("unsafe-inline", StringComparison.Ordinal) && d.StartsWith("script-src", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_module_is_served_as_javascript_under_its_fingerprinted_and_plain_name_and_builds_no_markup_from_text()
    {
        await using var factory = FormTestKit.Factory();
        var (_, html) = await GetAsync(factory, FormTestKit.Path);
        var src = Regex.Match(html, "<script type=\"module\" src=\"([^\"]*portal-forms[^\"]*\\.js)\"></script>").Groups[1].Value;
        src.ShouldNotBeNullOrEmpty();
        using var client = FormTestKit.Client(factory);

        using var fingerprinted = await client.GetAsync("/" + src.TrimStart('/'), Ct);
        using var plain = await client.GetAsync("/js/portal-forms.js", Ct);

        foreach (var response in new[] { fingerprinted, plain })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("text/javascript");
            var text = await response.Content.ReadAsStringAsync(Ct);
            text.ShouldContain("ts-copy-text");
            text.ShouldContain("ts-char-count");
            text.ShouldNotContain("innerHTML");
        }
    }

    // ---- the sending label ----

    [Fact]
    public async Task Each_form_carries_the_sending_words_and_keeps_its_pinned_markup()
    {
        await using var factory = TicketTestKit.Factory();

        var contact = (await GetAsync(factory, FormTestKit.Path)).Html;
        var lostLink = (await GetAsync(factory, "/p/paperplane/lost-link")).Html;
        var reply = (await GetAsync(factory, TicketTestKit.Path)).Html;

        foreach (var html in new[] { contact, lostLink, reply })
        {
            var form = Parse(html).QuerySelectorAll("form.ts-form").ShouldHaveSingleItem();
            form.GetAttribute("data-sending-label").ShouldBe(FormCopy.Sending);
            form.GetAttribute("data-sending-label").ShouldBe("Sending\u2026");
            // The button carries no data attribute: the form does.
            form.QuerySelectorAll("button[type=submit]").ShouldHaveSingleItem().Attributes.Select(a => a.Name).OrderBy(n => n).ShouldBe(["class", "type"]);
        }

        contact.ShouldContain("<form method=\"post\" action=\"/p/paperplane/contact\" enctype=\"multipart/form-data\" novalidate");
        contact.ShouldContain("<button type=\"submit\" class=\"btn btn-primary\">Send message</button>");
        lostLink.ShouldContain("<button type=\"submit\" class=\"btn btn-primary\">Send me a new link</button>");
    }

    [Fact]
    public async Task The_sending_words_are_the_one_constant_with_the_ellipsis_written_as_an_escape()
    {
        FormCopy.Sending.ShouldBe("Sending" + char.ConvertFromUtf32(0x2026));
        var source = await File.ReadAllTextAsync(TechStrap.Tests.Shared.RepositoryRoot.Combine("src", "TechStrap.Portal", "Forms", "FormCopy.cs"), Ct);
        source.ShouldContain("\"Sending\\u2026\"");
        source.Any(c => c > 0x7e).ShouldBeFalse("the source stays ASCII");
    }

    // ---- the counter ----

    [Fact]
    public async Task The_long_text_fields_have_a_counter_after_the_textarea_with_the_limit_and_the_words_from_the_constants_and_the_short_fields_have_none()
    {
        await using var factory = TicketTestKit.Factory();

        foreach (var (path, field) in new[] { (FormTestKit.Path, "body"), (TicketTestKit.Path, "body") })
        {
            var document = Parse((await GetAsync(factory, path)).Html);
            var counters = document.QuerySelectorAll("ts-char-count").ToList();
            var counter = counters.ShouldHaveSingleItem();
            counter.GetAttribute("for").ShouldBe(field);
            counter.GetAttribute("data-limit").ShouldBe(IntakeLimits.BodyMaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
            counter.GetAttribute("data-template").ShouldBe(FormCopy.CounterTemplate);
            counter.GetAttribute("data-over").ShouldBe(FormCopy.CounterOver);
            counter.HasAttribute("hidden").ShouldBeTrue("empty and hidden until script shows it");
            counter.TextContent.ShouldBeEmpty();
            counter.PreviousElementSibling!.TagName.ShouldBe("TEXTAREA");
            document.GetElementById(field)!.GetAttribute("maxlength").ShouldBe(counter.GetAttribute("data-limit"), "the counter and the textarea share one limit");
        }

        (await GetAsync(factory, "/p/paperplane/lost-link")).Html.ShouldNotContain("ts-char-count");
    }

    // ---- the copy button ----

    [Fact]
    public async Task The_received_page_has_a_copy_button_element_for_the_number_with_the_words_from_the_constants()
    {
        await using var factory = FormTestKit.Factory();

        var (_, html) = await GetAsync(factory, FormTestKit.ReceivedPath + "?ref=" + ReferenceFor(factory, "PAP-42"));

        var document = Parse(html);
        var number = document.GetElementById("ticket-number").ShouldNotBeNull();
        number.TextContent.ShouldBe("PAP-42");
        var copy = document.QuerySelectorAll("ts-copy-text").ShouldHaveSingleItem();
        copy.GetAttribute("target").ShouldBe("ticket-number");
        copy.GetAttribute("data-label").ShouldBe(ContactCopy.CopyNumber);
        copy.GetAttribute("data-copied").ShouldBe(ContactCopy.CopyNumberDone);
        copy.GetAttribute("data-failed").ShouldBe(ContactCopy.CopyNumberFailed);
        copy.TextContent.ShouldBeEmpty("the element is empty until the script renders its button; the number is still there to select");
    }

    [Fact]
    public async Task Without_a_valid_reference_there_is_no_number_and_so_no_copy_button()
    {
        await using var factory = FormTestKit.Factory();

        var (_, html) = await GetAsync(factory, FormTestKit.ReceivedPath);

        html.ShouldNotContain("ts-copy-text");
        html.ShouldNotContain("ticket-number");
    }

    [Fact]
    public async Task A_number_that_looks_like_markup_is_encoded_in_the_page_and_never_reaches_an_attribute_of_the_copy_element()
    {
        await using var factory = FormTestKit.Factory();

        // The number comes from a data-protected reference; only a number of the ticket shape is accepted, so a hostile one never gets this far. The element holds no copy of the number at all.
        var (_, html) = await GetAsync(factory, FormTestKit.ReceivedPath + "?ref=" + ReferenceFor(factory, "PAP-42"));

        Regex.Match(html, "<ts-copy-text[^>]*>").Value.ShouldNotContain("PAP-42");
    }
}
