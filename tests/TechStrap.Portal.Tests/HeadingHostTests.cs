using System.Net;
using AngleSharp.Dom;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Tests.Components;
using TechStrap.Portal.Tests.Forms;
using TechStrap.Portal.Tests.Kb;
using TechStrap.Portal.Tests.Tickets;

namespace TechStrap.Portal.Tests;

/// <summary>
/// Every Portal page has exactly one <c>h1</c> (PHASE-09 T16, UX brief, Review Focus 4), and the help-centre search page's is not the home's. The Admin has the same test for its two odd pages; the Portal's check
/// covers every kind of page, including the states (an API failure, an empty result, a ticket that cannot be loaded) and the pages whose body is HTML an author wrote.
/// </summary>
public sealed class HeadingHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task<IDocument> GetAsync(PortalFactory factory, string path, HttpStatusCode? expected = null)
    {
        using var client = FormTestKit.Client(factory);
        using var response = await client.GetAsync(path, Ct);
        if (expected is { } status)
        {
            response.StatusCode.ShouldBe(status);
        }

        return PageKit.Parse(await response.Content.ReadAsStringAsync(Ct));
    }

    private static string[] H1(IDocument dom) => [.. dom.QuerySelectorAll("h1").Select(h => h.TextContent.Trim())];

    private static PortalFactory KbHost()
    {
        var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoriesPath, new[] { new PublicKbCategoryDto("accounts", "Accounts", "About accounts", 2) });
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoryArticlesPath, KbTestKit.Page(1, 10, 1, KbTestKit.Article()));
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.SearchPath, KbTestKit.Hits(1, 10, 1, KbTestKit.Hit()));
        factory.Api.OnJson(HttpMethod.Get, "/api/public/kb/paperplane/articles/accounts/reset-password",
            new PublishedKbArticleDto("paperplane", "accounts", "Accounts", "reset-password", "Reset your password", "How", "<h1>A title in the body</h1><h2>Steps</h2><p>Go.</p>", KbTestKit.Updated, KbTestKit.Updated));
        return factory;
    }

    public static TheoryData<string, string> Pages => new()
    {
        { "/", "Support" },
        { "/p/paperplane", "How can we help?" },
        { "/p/paperplane/contact", "Contact support" },
        { "/p/paperplane/contact/received", "We have received your request." },
        { "/p/paperplane/lost-link", "Lost your ticket link?" },
        { "/p/paperplane/lost-link?sent=1", "Lost your ticket link?" },
        { "/p/paperplane/kb", "Help centre" },
        { "/p/paperplane/kb/accounts", "Accounts" },
        { "/p/paperplane/kb/search", "Search the help centre" },
        { "/p/paperplane/kb/search?q=reset", "Search the help centre" },
        { "/p/paperplane/kb/accounts/reset-password", "Reset your password" },
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task A_page_has_exactly_one_h1_and_it_is_the_pages_own_title(string path, string heading)
    {
        await using var factory = KbHost();

        var dom = await GetAsync(factory, path, HttpStatusCode.OK);

        H1(dom).ShouldBe([heading]);
    }

    [Fact]
    public async Task The_search_pages_h1_is_not_the_help_centre_homes()
    {
        await using var factory = KbHost();

        var home = H1(await GetAsync(factory, "/p/paperplane/kb")).Single();
        var search = H1(await GetAsync(factory, "/p/paperplane/kb/search?q=reset")).Single();
        var empty = H1(await GetAsync(factory, "/p/paperplane/kb/search")).Single();

        search.ShouldNotBe(home);
        empty.ShouldBe(search);
    }

    [Fact]
    public async Task An_empty_search_result_and_an_empty_help_centre_keep_one_h1_and_make_the_states_h2()
    {
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.SearchPath, KbTestKit.Hits(1, 10, 0));
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoriesPath, Array.Empty<PublicKbCategoryDto>());

        var none = await GetAsync(factory, "/p/paperplane/kb/search?q=zzz");
        var empty = await GetAsync(factory, "/p/paperplane/kb");

        foreach (var dom in new[] { none, empty })
        {
            dom.QuerySelectorAll("h1").Length.ShouldBe(1);
            dom.QuerySelectorAll(".ts-state h2").Length.ShouldBe(1);
            dom.QuerySelectorAll(".ts-state h1").ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task An_h1_in_an_article_body_is_shown_as_an_h2_so_the_page_keeps_one()
    {
        await using var factory = KbHost();

        var dom = await GetAsync(factory, "/p/paperplane/kb/accounts/reset-password", HttpStatusCode.OK);

        H1(dom).ShouldBe(["Reset your password"]);
        dom.QuerySelectorAll(".ts-kb-article-body h2").Select(h => h.TextContent).ShouldBe(["A title in the body", "Steps"]);
        dom.QuerySelectorAll("h2").Select(h => h.TextContent).ShouldContain("Still need help?");
    }

    [Theory]
    [InlineData("Open", "PAP-42 Printer jam")]
    [InlineData("Closed", "PAP-42 Printer jam")]
    public async Task The_ticket_page_has_one_h1_even_when_a_message_has_its_own(string status, string heading)
    {
        await using var factory = TicketTestKit.Factory(TicketTestKit.Ticket(status, agentBody: "<h1>Agent heading</h1><p>Hello.</p>"));

        var dom = await GetAsync(factory, TicketTestKit.Path, HttpStatusCode.OK);

        H1(dom).Single().ShouldContain("PAP-42");
        dom.QuerySelectorAll(".ts-message-body h1").ShouldBeEmpty();
        dom.QuerySelectorAll(".ts-message-body h2").Select(h => h.TextContent).ShouldContain("Agent heading");
        dom.QuerySelectorAll("h2#reply").Count.ShouldBe(1);
        heading.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task The_unavailable_states_have_exactly_one_h1_and_it_is_the_state_s_own(HttpStatusCode api)
    {
        await using var factory = FormTestKit.Factory(product: false);
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/paperplane", api, "x", "no");

        var product = await GetAsync(factory, "/p/paperplane");
        await using var ticketFactory = FormTestKit.Factory();
        ticketFactory.Api.OnProblem(HttpMethod.Get, TicketTestKit.TicketApi, api, "x", "no");
        var ticket = await GetAsync(ticketFactory, TicketTestKit.Path);

        foreach (var dom in new[] { product, ticket })
        {
            H1(dom).ShouldBe(["This page could not be loaded."]);
            dom.QuerySelectorAll(".ts-state h1").Length.ShouldBe(1);
        }
    }

    [Theory]
    [InlineData("/nope")]
    [InlineData("/p/nobody/contact")]
    [InlineData("/t/short")]
    public async Task The_not_found_page_has_exactly_one_h1(string path)
    {
        await using var factory = FormTestKit.Factory(product: false);
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/nobody", HttpStatusCode.NotFound, "product-not-found", "No such product.");

        var dom = await GetAsync(factory, path, HttpStatusCode.NotFound);

        H1(dom).ShouldBe(["Page not found"]);
    }

    [Fact]
    public async Task The_error_and_style_guide_pages_have_exactly_one_h1()
    {
        await using var factory = FormTestKit.Factory(product: false);

        H1(await GetAsync(factory, "/error")).Length.ShouldBe(1);
        H1(await GetAsync(factory, "/_styleguide", HttpStatusCode.OK)).Length.ShouldBe(1);
    }

    [Fact]
    public async Task A_form_shown_again_after_an_error_has_one_h1_and_the_summary_is_an_h2()
    {
        await using var factory = FormTestKit.Factory();
        using var client = FormTestKit.Client(factory);
        var token = await FormTestKit.TokenAsync(client, FormTestKit.Path, Ct);

        using var response = await client.PostAsync(FormTestKit.Path, FormTestKit.ContactForm(token, email: ""), Ct);
        var dom = PageKit.Parse(await response.Content.ReadAsStringAsync(Ct));

        H1(dom).ShouldBe(["Contact support"]);
        dom.QuerySelectorAll(".ts-error-summary h2").Length.ShouldBe(1);
    }
}
