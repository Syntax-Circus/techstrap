using System.Net;
using System.Text.RegularExpressions;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Forms;
using TechStrap.Portal.Tests.Tickets;

namespace TechStrap.Portal.Tests.Kb;

/// <summary>
/// P09-T13 (the help-center search) at the host: a GET form that works without script, an empty-query prompt, a contact link when nothing matches, plain-text snippets, noindex for a query, paging links that keep the text,
/// and that nothing about the page is ever kept. Review Focus 1 (XSS): the text, every title and every snippet are plain text and encoded, in the results and in the box.
/// </summary>
public sealed class KbSearchHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static PortalFactory WithHits(int page = 1, int total = 2, params PublicKbSearchResultDto[] items)
    {
        var factory = KbTestKit.Factory();
        factory.Api.OnJson(
            HttpMethod.Get,
            KbTestKit.SearchPath,
            KbTestKit.Hits(page, 10, total, items.Length == 0 ? [KbTestKit.Hit(), KbTestKit.Hit("change-email", "Change your email", "Open <b>settings</b>.", "accounts", "Accounts")] : items));
        return factory;
    }

    [Fact]
    public async Task An_empty_query_shows_the_prompt_makes_no_search_call_and_stays_indexable()
    {
        await using var factory = KbTestKit.Factory();
        using var client = FormTestKit.Client(factory);

        var (response, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        KbTestKit.Texts(dom, ".ts-state h2").ShouldBe(["What are you looking for?"]);
        dom.QuerySelector("form[role=search] input[name=q]")!.GetAttribute("value").ShouldBeNullOrEmpty();
        KbTestKit.Meta(dom, "meta[name=robots]").ShouldStartWith("index, follow");
        factory.Api.Requests.Select(request => request.Path).ShouldBe([KbTestKit.ProductPath], "an empty text is never sent to the API");
    }

    [Theory]
    [InlineData("?q=")]
    [InlineData("?q=%20%20")]
    [InlineData("?utm=1")]
    public async Task A_blank_text_is_an_empty_query(string query)
    {
        await using var factory = KbTestKit.Factory();
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search" + query, Ct);

        KbTestKit.Texts(dom, ".ts-state h2").ShouldBe(["What are you looking for?"]);
        factory.Api.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_query_lists_the_results_as_cards_with_a_plain_text_snippet_the_category_and_the_count_and_is_noindex()
    {
        await using var factory = WithHits();
        using var client = FormTestKit.Client(factory);

        var (response, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search?q=reset%20password", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        KbTestKit.Texts(dom, ".ts-kb-card h2").ShouldBe(["Reset your password", "Change your email"]);
        KbTestKit.Links(dom, ".ts-kb-card h2 a").ShouldBe(["/p/paperplane/kb/accounts/reset-password", "/p/paperplane/kb/accounts/change-email"]);
        KbTestKit.Texts(dom, ".ts-kb-card .ts-kb-summary").ShouldBe(["Use the reset link.", "Open <b>settings</b>."]);
        KbTestKit.Texts(dom, ".ts-kb-card .ts-kb-meta").ShouldBe(["Accounts", "Accounts"]);
        KbTestKit.Texts(dom, "p.ts-kb-meta").ShouldContain("2 results");
        dom.QuerySelector("form[role=search] input[name=q]")!.GetAttribute("value").ShouldBe("reset password");
        KbTestKit.Meta(dom, "meta[name=robots]").ShouldBe("noindex, nofollow");
        dom.Title.ShouldBe("Search - Paperplane Help Center");
        dom.QuerySelector("link[rel=canonical]")!.GetAttribute("href").ShouldBe(PortalFactory.PublicUrl + "/p/paperplane/kb/search", "the canonical address has no text");
        var sent = factory.Api.Requests.Single(request => request.Path == KbTestKit.SearchPath);
        sent.Query.ShouldBe("?q=reset%20password&page=1&pageSize=10");
        sent.Client.ShouldBe(ApiClientNames.Read);
    }

    [Fact]
    public async Task A_snippet_with_markup_in_it_is_shown_as_text_never_as_markup()
    {
        await using var factory = WithHits(1, 1, KbTestKit.Hit("x", "<script>alert(1)</script>", "<img src=x onerror=alert(1)> <b>bold</b> &amp;", "a", "<u>Cat</u>"));
        using var client = FormTestKit.Client(factory);

        var (_, html, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search?q=x", Ct);

        html.ShouldNotContain("<script>alert(1)");
        html.ShouldNotContain("<img src=x");
        html.ShouldNotContain("<b>bold</b>");
        html.ShouldNotContain("<u>Cat</u>");
        KbTestKit.Texts(dom, ".ts-kb-card h2")[0].ShouldBe("<script>alert(1)</script>");
        KbTestKit.Texts(dom, ".ts-kb-card .ts-kb-summary")[0].ShouldBe("<img src=x onerror=alert(1)> <b>bold</b> &amp;");
        dom.QuerySelectorAll(".ts-kb-list script, .ts-kb-list img, .ts-kb-list b, .ts-kb-list u").Length.ShouldBe(0);
    }

    [Fact]
    public async Task The_text_the_visitor_typed_is_encoded_in_the_box_the_links_and_the_results()
    {
        await using var factory = WithHits(2, 25);
        const string Typed = "\"><script>alert(1)</script> & a=b#c";
        using var client = FormTestKit.Client(factory);

        var (_, html, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search?q=" + Uri.EscapeDataString(Typed) + "&page=2", Ct);

        html.ShouldNotContain("<script>alert(1)");
        dom.QuerySelector("form[role=search] input[name=q]")!.GetAttribute("value").ShouldBe(Typed);
        dom.QuerySelectorAll("script:not([src])").Length.ShouldBe(0);
        KbTestKit.Links(dom, "nav.ts-pager a").ShouldBe(
        [
            "/p/paperplane/kb/search?q=" + Uri.EscapeDataString(Typed),
            "/p/paperplane/kb/search?q=" + Uri.EscapeDataString(Typed) + "&page=3",
        ]);
        factory.Api.Requests.Single(request => request.Path == KbTestKit.SearchPath).Query.ShouldBe("?q=" + Uri.EscapeDataString(Typed) + "&page=2&pageSize=10");
    }

    [Fact]
    public async Task No_result_shows_a_message_and_a_link_to_contact_support()
    {
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.SearchPath, KbTestKit.Hits(1, 10, 0));
        using var client = FormTestKit.Client(factory);

        var (response, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search?q=zzz", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        KbTestKit.Texts(dom, ".ts-state h2").ShouldBe(["No articles found"]);
        KbTestKit.Links(dom, ".ts-state a").ShouldBe(["/p/paperplane/contact"]);
        dom.QuerySelectorAll(".ts-kb-card").Length.ShouldBe(0);
        KbTestKit.Meta(dom, "meta[name=robots]").ShouldBe("noindex, nofollow");
    }

    [Fact]
    public async Task A_page_past_the_end_of_results_that_exist_is_the_neutral_404_like_a_category_page()
    {
        var neutral = await Seen.NeutralNotFoundAsync(Ct, "/p/nope");
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.SearchPath, KbTestKit.Hits(9, 10, 3));
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync("/p/paperplane/kb/search?q=router&page=9", Ct);
        var seen = await Seen.OfAsync(response, factory.Api.Requests.Count, Ct);

        // The path rule for the search page adds no-store (never kept by anyone), which no other 404 has; everything else is the neutral page byte for byte.
        seen.Status.ShouldBe(HttpStatusCode.NotFound);
        seen.Body.ShouldBe(neutral.Body, "byte for byte");
        seen.Body.ShouldNotContain("--ts-accent");
        seen.Body.ShouldNotContain("ts-product-header");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        Without(seen.HeadersWithoutEnhancedNav).ShouldBe(Without(neutral.HeadersWithoutEnhancedNav));
    }

    private static string Without(string headers) => string.Join("\n", headers.Split('\n').Where(line => !line.StartsWith("Cache-Control:", StringComparison.Ordinal)));

    [Fact]
    public async Task A_later_page_of_a_search_with_no_results_at_all_is_still_the_no_result_message()
    {
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.SearchPath, KbTestKit.Hits(3, 10, 0));
        using var client = FormTestKit.Client(factory);

        var (response, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search?q=zzz&page=3", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        KbTestKit.Texts(dom, ".ts-state h2").ShouldBe(["No articles found"]);
    }

    [Fact]
    public async Task The_paging_links_keep_the_text_and_work_without_script()
    {
        await using var factory = WithHits(2, 25);
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search?q=router&page=2", Ct);

        dom.QuerySelector("a[rel=prev]")!.GetAttribute("href").ShouldBe("/p/paperplane/kb/search?q=router");
        dom.QuerySelector("a[rel=next]")!.GetAttribute("href").ShouldBe("/p/paperplane/kb/search?q=router&page=3");
        dom.QuerySelector(".ts-pager-state")!.TextContent.ShouldBe("Page 2 of 3");
    }

    [Theory]
    [InlineData("&page=abc", 1)]
    [InlineData("&page=-2", 1)]
    [InlineData("&page=99999999999", 1)]
    [InlineData("&page=4", 4)]
    public async Task A_page_value_that_is_not_a_whole_number_is_page_one_and_never_a_500(string page, int expected)
    {
        await using var factory = WithHits(expected, 80);
        using var client = FormTestKit.Client(factory);

        var (response, _, _) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search?q=router" + page, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, page);
        factory.Api.Requests.Single(request => request.Path == KbTestKit.SearchPath).Query.ShouldBe($"?q=router&page={expected}&pageSize=10", page);
    }

    [Fact]
    public async Task A_text_longer_than_the_limit_is_cut_at_two_hundred_characters_before_it_is_sent()
    {
        await using var factory = WithHits();
        using var client = FormTestKit.Client(factory);

        await KbTestKit.GetAsync(client, "/p/paperplane/kb/search?q=" + new string('a', 300), Ct);

        factory.Api.Requests.Single(request => request.Path == KbTestKit.SearchPath).Query.ShouldBe($"?q={new string('a', 200)}&page=1&pageSize=10");
    }

    [Fact]
    public async Task The_page_is_never_kept_by_the_server_or_the_browser()
    {
        await using var factory = WithHits();
        using var client = FormTestKit.Client(factory);

        var (first, _, _) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search?q=router", Ct);
        var (second, _, _) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search?q=router", Ct);

        factory.Api.Count(HttpMethod.Get, KbTestKit.SearchPath).ShouldBe(2);
        factory.Api.Count(HttpMethod.Get, KbTestKit.ProductPath).ShouldBe(2);
        KbTestKit.Header(second, "Age").ShouldBeEmpty();
        first.Headers.CacheControl!.NoStore.ShouldBeTrue();
        KbTestKit.Header(first, "Set-Cookie").ShouldBeEmpty();
    }

    [Fact]
    public async Task The_search_form_is_a_get_form_and_the_page_has_no_inline_script()
    {
        await using var factory = WithHits();
        using var client = FormTestKit.Client(factory);

        var (_, html, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search?q=router", Ct);

        var form = dom.QuerySelector("form[role=search]")!;
        form.GetAttribute("method").ShouldBe("get");
        form.GetAttribute("action").ShouldBe("/p/paperplane/kb/search");
        form.QuerySelectorAll("input[type=hidden]").Length.ShouldBe(0, "no antiforgery token: a GET form sets no cookie");
        Regex.IsMatch(html, @"<script(?![^>]*\bsrc=)").ShouldBeFalse("the CSP allows no inline script");
    }

    [Fact]
    public async Task A_failing_search_is_a_calm_503_and_a_429_is_a_429()
    {
        await using var down = KbTestKit.Factory();
        down.Api.OnStatus(HttpMethod.Get, KbTestKit.SearchPath, HttpStatusCode.GatewayTimeout);
        using var downClient = FormTestKit.Client(down);
        await using var busy = KbTestKit.Factory();
        busy.Api.OnStatus(HttpMethod.Get, KbTestKit.SearchPath, HttpStatusCode.TooManyRequests);
        using var busyClient = FormTestKit.Client(busy);

        var (downResponse, downHtml, _) = await KbTestKit.GetAsync(downClient, "/p/paperplane/kb/search?q=x", Ct);
        var (busyResponse, busyHtml, _) = await KbTestKit.GetAsync(busyClient, "/p/paperplane/kb/search?q=x", Ct);

        downResponse.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        downHtml.ShouldContain("We could not reach our support system.");
        busyResponse.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        busyHtml.ShouldContain("You have sent a lot in a short time.");
    }

    [Fact]
    public async Task An_unknown_product_is_the_neutral_404_and_the_search_is_never_asked_for()
    {
        // The search page's own header rule (never stored) applies to every status on its path, so the neutral page to compare with is the one an unknown product gets at that same path.
        var neutral = await Seen.NeutralNotFoundAsync(Ct, "/p/nope/kb/search?q=router");
        await using var factory = FormTestKit.Factory(product: false);
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/gone", HttpStatusCode.NotFound, "product-not-found", "No such product.");
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync("/p/gone/kb/search?q=router", Ct);
        var seen = await Seen.OfAsync(response, factory.Api.Requests.Count, Ct);

        seen.ShouldBeTheNeutralNotFound(neutral);
        factory.Api.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/public/products/gone");
    }
}
