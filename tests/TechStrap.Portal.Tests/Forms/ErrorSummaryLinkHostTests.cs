using System.Net;
using TechStrap.Portal.Tests.Components;
using TechStrap.Portal.Tests.Tickets;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>
/// The error summary's links at the host (Review Focus 4). The document's <c>base</c> is <c>/</c>, so a bare <c>#email</c> is the home page: the Admin hit the same bug. Here every link of every form's summary is
/// resolved the way a browser resolves it, and has to stay on the page the post was answered at (a jump, not a page load) and land on an element that exists; and the summary opts out of Blazor's enhanced navigation,
/// which scrolls to a fragment but leaves the focus where it was (proved in a browser in the 09d spike).
/// </summary>
public sealed class ErrorSummaryLinkHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static void AssertLinksJumpToTheirFields(string pageUrl, string html, params string[] expectedFields)
    {
        var dom = PageKit.Parse(html);
        var summary = dom.QuerySelector(".ts-error-summary").ShouldNotBeNull();
        summary.GetAttribute("data-enhance-nav").ShouldBe("false");
        var links = summary.QuerySelectorAll("a").ToList();
        links.Select(a => PageKit.Resolve(dom, pageUrl, a.GetAttribute("href")!).Fragment.TrimStart('#')).ShouldBe(expectedFields);
        foreach (var link in links)
        {
            var href = link.GetAttribute("href")!;
            PageKit.IsJumpWithin(dom, pageUrl, href).ShouldBeTrue($"{href} must stay on {pageUrl}");
            href.ShouldStartWith("/", Case.Sensitive, "never a bare fragment");
            var target = dom.GetElementById(PageKit.Resolve(dom, pageUrl, href).Fragment.TrimStart('#'));
            target.ShouldNotBeNull($"{href} must land on an element of the page");
        }
    }

    [Fact]
    public async Task The_contact_forms_links_jump_to_its_four_fields()
    {
        await using var factory = FormTestKit.Factory();
        using var client = FormTestKit.Client(factory);
        var token = await FormTestKit.TokenAsync(client, FormTestKit.Path, Ct);

        using var response = await client.PostAsync(FormTestKit.Path, FormTestKit.ContactForm(token, email: "", name: "", subject: "", body: ""), Ct);

        AssertLinksJumpToTheirFields(PageKit.Origin + FormTestKit.Path, await response.Content.ReadAsStringAsync(Ct), "name", "email", "subject", "body");
    }

    [Fact]
    public async Task The_contact_forms_attachment_link_jumps_to_the_file_input()
    {
        await using var factory = FormTestKit.Factory();
        using var client = FormTestKit.Client(factory);
        var token = await FormTestKit.TokenAsync(client, FormTestKit.Path, Ct);
        var files = Enumerable.Range(0, 6).Select(i => new PostedFile($"f{i}.txt", [1, 2, 3])).ToArray();

        using var response = await client.PostAsync(FormTestKit.Path, FormTestKit.ContactForm(token, files: files), Ct);

        AssertLinksJumpToTheirFields(PageKit.Origin + FormTestKit.Path, await response.Content.ReadAsStringAsync(Ct), "attachments");
    }

    [Fact]
    public async Task The_lost_link_forms_link_jumps_to_its_field()
    {
        await using var factory = FormTestKit.Factory();
        using var client = FormTestKit.Client(factory);
        var html = await client.GetStringAsync("/p/paperplane/lost-link", Ct);
        var form = new MultipartFormDataContent { { new StringContent("lost-link"), "_handler" }, { new StringContent(FormTestKit.TokenFrom(html)), "__RequestVerificationToken" }, { new StringContent("not an address"), "Form.Email" } };

        using var response = await client.PostAsync("/p/paperplane/lost-link", form, Ct);

        AssertLinksJumpToTheirFields(PageKit.Origin + "/p/paperplane/lost-link", await response.Content.ReadAsStringAsync(Ct), "email");
    }

    [Fact]
    public async Task The_reply_forms_link_jumps_to_its_field_on_the_ticket_page_whose_address_holds_the_token()
    {
        await using var factory = TicketTestKit.Factory();
        using var client = TicketTestKit.Client(factory);
        var token = await FormTestKit.TokenAsync(client, TicketTestKit.Path, Ct);

        using var response = await client.PostAsync(TicketTestKit.Path, TicketTestKit.ReplyForm(token, body: ""), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssertLinksJumpToTheirFields(PageKit.Origin + TicketTestKit.Path, await response.Content.ReadAsStringAsync(Ct), "body");
    }

    [Fact]
    public void A_summary_link_that_was_a_bare_fragment_would_have_left_the_page()
    {
        // The control for the tests above: the same resolution says a bare #email is the home page, so the tests would have caught the old markup.
        var dom = PageKit.Parse("<html><head><base href=\"/\"></head><body><a href=\"#email\">x</a></body></html>");

        PageKit.IsJumpWithin(dom, PageKit.Origin + FormTestKit.Path, "#email").ShouldBeFalse();
        PageKit.Resolve(dom, PageKit.Origin + FormTestKit.Path, "#email").AbsolutePath.ShouldBe("/");
    }
}
