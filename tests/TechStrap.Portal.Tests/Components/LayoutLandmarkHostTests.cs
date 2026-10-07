using System.Text.RegularExpressions;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Components;
using TechStrap.Portal.Tests.Forms;
using TechStrap.Portal.Tests.Kb;
using TechStrap.Portal.Tests.Tickets;

namespace TechStrap.Portal.Tests.Components;

/// <summary>
/// The page frame at the host (PHASE-09 T16, UX brief, Review Focus 4): a product page starts with a skip link that is the first thing Tab reaches and that, resolved the way a browser resolves it under
/// <c>base href="/"</c>, jumps within the same page to <c>main</c>; <c>main</c> can take focus; there is a navigation landmark for the product's header and one for its footer, each named, and exactly one
/// contentinfo; a neutral page (the root, not-found, error) has none of the product frame, so every neutral 404 stays byte for byte the same.
/// </summary>
public sealed class LayoutLandmarkHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task<(string Html, AngleSharp.Dom.IDocument Dom)> GetAsync(PortalFactory factory, string pathAndQuery)
    {
        using var client = FormTestKit.Client(factory);
        using var response = await client.GetAsync(pathAndQuery, Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);
        return (html, PageKit.Parse(html));
    }

    public static TheoryData<string> ProductPages => ["/p/paperplane", "/p/paperplane/contact", "/p/paperplane/lost-link", "/p/paperplane/contact/received", "/p/paperplane/kb", "/p/paperplane/kb/search", "/t/AbC-_0123456789AbC-_0123456789AbC-_01234567"];

    private static PortalFactory Host()
    {
        var factory = TicketTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoriesPath, new[] { new PublicKbCategoryDto("accounts", "Accounts", "About accounts", 1) });
        return factory;
    }

    [Theory]
    [MemberData(nameof(ProductPages))]
    public async Task A_product_page_starts_with_the_skip_link_which_jumps_within_the_page_to_main(string path)
    {
        await using var factory = Host();

        var (_, dom) = await GetAsync(factory, path);

        var first = PageKit.FirstFocusable(dom).ShouldNotBeNull();
        first.ClassList.ShouldContain("ts-skip-link");
        first.TextContent.ShouldBe(ShellCopy.SkipToMain);
        first.GetAttribute("data-enhance-nav").ShouldBe("false", "Blazor's enhanced navigation would scroll but leave the focus on the link");
        var href = first.GetAttribute("href")!;
        href.ShouldStartWith("/", Case.Sensitive, "root-relative, so the document's base cannot send it to the home page");
        PageKit.IsJumpWithin(dom, PageKit.Origin + path, href).ShouldBeTrue($"{href} must be a jump within {path}");
        PageKit.Resolve(dom, PageKit.Origin + path, href).Fragment.ShouldBe("#main");
        dom.QuerySelectorAll("a.ts-skip-link").Count.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(ProductPages))]
    public async Task Main_is_the_skip_links_target_and_can_take_focus_and_there_is_one_h1_inside_it(string path)
    {
        await using var factory = Host();

        var (_, dom) = await GetAsync(factory, path);

        var main = dom.QuerySelectorAll("main").ShouldHaveSingleItem();
        main.Id.ShouldBe("main");
        main.GetAttribute("tabindex").ShouldBe("-1");
        dom.GetElementById("main").ShouldBeSameAs(main);
        main.QuerySelectorAll("h1").Count.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(ProductPages))]
    public async Task A_product_page_has_a_named_navigation_for_its_header_and_one_for_its_footer_and_one_contentinfo(string path)
    {
        await using var factory = Host();

        var (_, dom) = await GetAsync(factory, path);

        var headerNav = dom.QuerySelector("header.ts-product-header nav").ShouldNotBeNull();
        headerNav.GetAttribute("aria-label").ShouldBe(ShellCopy.NavLabel);
        headerNav.QuerySelectorAll("a").Select(a => a.GetAttribute("href")).ShouldBe(["/p/paperplane", "/p/paperplane/kb", "/p/paperplane/contact"]);
        headerNav.QuerySelectorAll("li a").Select(a => a.TextContent).ShouldBe([ShellCopy.NavHelp, ShellCopy.NavContact]);
        var footerNav = dom.QuerySelector("nav.ts-product-footer").ShouldNotBeNull();
        footerNav.GetAttribute("aria-label").ShouldBe(ShellCopy.FooterNavLabel);
        footerNav.QuerySelector("a")!.GetAttribute("href").ShouldBe("/p/paperplane/lost-link");

        // Every navigation region has its own name, so a screen reader's landmark list tells them apart. (The breadcrumb trail and the pager are navigation regions of their own and are named too.)
        var names = dom.QuerySelectorAll("nav").Select(n => n.GetAttribute("aria-label")).ToList();
        names.ShouldAllBe(name => !string.IsNullOrWhiteSpace(name));
        names.Distinct().Count().ShouldBe(names.Count);
        dom.QuerySelectorAll("footer").ShouldHaveSingleItem().ClassList.ShouldContain("ts-powered");
        dom.QuerySelectorAll("header.ts-product-header").ShouldHaveSingleItem(); // (a message of the conversation has a header element of its own; it is not a banner)
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/nope")]
    [InlineData("/error")]
    public async Task A_neutral_page_has_no_skip_link_header_or_navigation_but_still_a_main_that_takes_focus(string path)
    {
        await using var factory = FormTestKit.Factory(product: false);

        var (_, dom) = await GetAsync(factory, path);

        dom.QuerySelectorAll("a.ts-skip-link").ShouldBeEmpty("it is built from the address, and a neutral page must read the same for every address");
        dom.QuerySelectorAll("header, nav").ShouldBeEmpty();
        var main = dom.QuerySelectorAll("main").ShouldHaveSingleItem();
        main.Id.ShouldBe("main");
        main.GetAttribute("tabindex").ShouldBe("-1");
        dom.QuerySelectorAll("h1").Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_skip_link_keeps_what_the_page_reads_from_its_query_so_a_jump_is_not_a_reload_and_drops_everything_else()
    {
        await using var factory = Host();
        const string Path = "/p/paperplane/contact?subject=Printer%20jam&name=Ada&utm_source=mail&Website=spam";

        var (html, dom) = await GetAsync(factory, Path);

        var href = dom.QuerySelector("a.ts-skip-link")!.GetAttribute("href")!;
        href.ShouldBe("/p/paperplane/contact?subject=Printer%20jam&name=Ada#main");
        html.ShouldNotContain("utm_source");
        html.ShouldNotContain("spam");
        var pageUrl = PageKit.Origin + "/p/paperplane/contact?subject=Printer%20jam&name=Ada";
        PageKit.IsJumpWithin(dom, pageUrl, href).ShouldBeTrue("on the address the visitor sees once the extra parameters are gone, the link is a jump");
    }

    [Fact]
    public async Task A_kept_help_centre_page_never_carries_a_visitors_other_query_parameters_in_its_markup()
    {
        // The output cache keeps one copy for every visitor of /p/{key}/kb/{category}; a campaign tag or any other parameter of the first request must never be in it.
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoryArticlesPath, KbTestKit.Page(1, 10, 1, KbTestKit.Article()));

        var (html, dom) = await GetAsync(factory, "/p/paperplane/kb/accounts?utm_source=newsletter-4711");

        html.ShouldNotContain("newsletter-4711");
        dom.QuerySelector("a.ts-skip-link")!.GetAttribute("href").ShouldBe("/p/paperplane/kb/accounts#main");
    }

    [Fact]
    public async Task The_ticket_page_has_a_jump_to_the_reply_form_that_works_under_the_base_and_lands_on_a_heading_that_can_take_focus()
    {
        await using var factory = TicketTestKit.Factory();
        var path = TicketTestKit.Path;

        var (_, dom) = await GetAsync(factory, path);

        var jump = dom.QuerySelector("a.ts-jump-reply").ShouldNotBeNull();
        jump.TextContent.ShouldBe(TechStrap.Portal.Tickets.TicketCopy.JumpToReply);
        jump.GetAttribute("data-enhance-nav").ShouldBe("false");
        PageKit.IsJumpWithin(dom, PageKit.Origin + path, jump.GetAttribute("href")!).ShouldBeTrue();
        var heading = dom.GetElementById("reply").ShouldNotBeNull();
        heading.TagName.ShouldBe("H2");
        heading.GetAttribute("tabindex").ShouldBe("-1");
        // The jump comes before the conversation it skips over.
        Regex.Match(dom.Body!.InnerHtml, "ts-jump-reply").Index.ShouldBeLessThan(Regex.Match(dom.Body.InnerHtml, "ts-thread").Index);
    }

    [Fact]
    public async Task The_error_summary_still_takes_focus_when_the_page_opens_and_is_announced_once()
    {
        await using var factory = FormTestKit.Factory();
        using var client = FormTestKit.Client(factory);
        var token = await FormTestKit.TokenAsync(client, FormTestKit.Path, Ct);

        using var response = await client.PostAsync(FormTestKit.Path, FormTestKit.ContactForm(token, email: ""), Ct);
        var dom = PageKit.Parse(await response.Content.ReadAsStringAsync(Ct));

        var summary = dom.QuerySelector(".ts-error-summary").ShouldNotBeNull();
        summary.HasAttribute("autofocus").ShouldBeTrue();
        summary.GetAttribute("tabindex").ShouldBe("-1");
        summary.GetAttribute("role").ShouldBe("alert");
        summary.GetAttribute("aria-labelledby").ShouldBe("error-summary-heading");
        dom.GetElementById("error-summary-heading")!.TagName.ShouldBe("H2");
        dom.QuerySelectorAll("[role=alert]").Count(e => e.ClassList.Contains("ts-error-summary")).ShouldBe(1);
    }

    [Fact]
    public async Task Every_form_says_in_words_what_is_required()
    {
        await using var factory = TicketTestKit.Factory();

        var contact = (await GetAsync(factory, "/p/paperplane/contact")).Dom;
        var lostLink = (await GetAsync(factory, "/p/paperplane/lost-link")).Dom;
        var reply = (await GetAsync(factory, TicketTestKit.Path)).Dom;

        contact.QuerySelector(".ts-form-note")!.TextContent.ShouldBe(TechStrap.Portal.Forms.ContactCopy.RequiredNote);
        lostLink.QuerySelector(".ts-form-note")!.TextContent.ShouldBe(TechStrap.Portal.Forms.LostLinkCopy.RequiredNote);
        reply.QuerySelector(".ts-form-note")!.TextContent.ShouldBe(TechStrap.Portal.Tickets.TicketCopy.ReplyNote);
        foreach (var dom in new[] { contact, lostLink, reply })
        {
            dom.QuerySelector(".ts-form-note")!.NextElementSibling!.TagName.ShouldBe("FORM", "the note sits right above the form it describes");
        }
    }
}
