using System.Net;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Forms;
using TechStrap.Portal.Tests.Tickets;

namespace TechStrap.Portal.Tests.Kb;

/// <summary>
/// P09-T12 (a category page) at the host: the paged list, links that work without script, the head, and Review Focus 2: an unknown category, a category of another product, an empty one, a slug that is not a slug and a
/// page past the end are the one neutral 404, byte for byte, with no theme in it. Review Focus 1: every title and summary is plain text and encoded.
/// </summary>
public sealed class KbCategoryHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static PortalFactory WithList(int page = 1, int total = 3, params PublicKbArticleSummaryDto[] items)
    {
        var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoryArticlesPath, KbTestKit.Page(page, 10, total, items.Length == 0 ? [KbTestKit.Article(), KbTestKit.Article("change-email", "Change your email", null), KbTestKit.Article("shared-tips", "Shared tips", "For everyone", product: null)] : items));
        return factory;
    }

    [Fact]
    public async Task A_category_lists_its_articles_as_links_under_the_visitors_product_with_summary_and_day()
    {
        await using var factory = WithList();
        using var client = FormTestKit.Client(factory);

        var (response, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        KbTestKit.Texts(dom, "h1").ShouldBe(["Accounts"]);
        KbTestKit.Texts(dom, ".ts-kb-card h2").ShouldBe(["Reset your password", "Change your email", "Shared tips"]);
        KbTestKit.Links(dom, ".ts-kb-card h2 a").ShouldBe(
            ["/p/paperplane/kb/accounts/reset-password", "/p/paperplane/kb/accounts/change-email", "/p/paperplane/kb/accounts/shared-tips"],
            "a shared article is linked under the product the visitor is on");
        KbTestKit.Texts(dom, ".ts-kb-card .ts-kb-summary").ShouldBe(["How to reset it", "For everyone"]);
        KbTestKit.Texts(dom, ".ts-kb-card .ts-kb-meta").ShouldBe(["Updated 5 Oct 2026", "Updated 5 Oct 2026", "Updated 5 Oct 2026"]);
        KbTestKit.Texts(dom, "nav.ts-breadcrumbs li").ShouldBe(["Paperplane", "Help centre", "Accounts"]);
        KbTestKit.Links(dom, "nav.ts-breadcrumbs a").ShouldBe(["/p/paperplane", "/p/paperplane/kb"]);
        dom.QuerySelectorAll("nav.ts-pager").Length.ShouldBe(0, "one page needs no pager");
        var sent = factory.Api.Requests.Single(request => request.Path == KbTestKit.CategoryArticlesPath);
        sent.Query.ShouldBe("?page=1&pageSize=10");
        sent.Client.ShouldBe(ApiClientNames.Read);
    }

    [Fact]
    public async Task A_title_and_a_summary_are_plain_text_and_encoded_never_markup()
    {
        await using var factory = WithList(1, 1, KbTestKit.Article("x", "<script>alert(1)</script> & <i>title</i>", "<img src=x onerror=alert(1)> \"summary\""));
        using var client = FormTestKit.Client(factory);

        var (_, html, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts", Ct);

        html.ShouldNotContain("<script>alert(1)");
        html.ShouldNotContain("<img src=x");
        html.ShouldNotContain("<i>title</i>");
        KbTestKit.Texts(dom, ".ts-kb-card h2")[0].ShouldBe("<script>alert(1)</script> & <i>title</i>");
        KbTestKit.Texts(dom, ".ts-kb-card .ts-kb-summary")[0].ShouldBe("<img src=x onerror=alert(1)> \"summary\"");
        dom.QuerySelectorAll(".ts-kb-card script, .ts-kb-card img, .ts-kb-card i").Length.ShouldBe(0);
    }

    [Fact]
    public async Task A_middle_page_links_to_the_previous_and_next_page_with_plain_links_and_page_one_has_no_query()
    {
        await using var factory = WithList(2, 25);
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts?page=2", Ct);

        KbTestKit.Links(dom, "nav.ts-pager a").ShouldBe(["/p/paperplane/kb/accounts", "/p/paperplane/kb/accounts?page=3"]);
        dom.QuerySelector("nav.ts-pager a[rel=prev]")!.GetAttribute("href").ShouldBe("/p/paperplane/kb/accounts");
        dom.QuerySelector("nav.ts-pager a[rel=next]")!.GetAttribute("href").ShouldBe("/p/paperplane/kb/accounts?page=3");
        dom.QuerySelector("nav.ts-pager .ts-pager-state")!.TextContent.ShouldBe("Page 2 of 3");
        dom.QuerySelectorAll("nav.ts-pager button, nav.ts-pager [onclick]").Length.ShouldBe(0, "paging works without script");
        factory.Api.Requests.Single(request => request.Path == KbTestKit.CategoryArticlesPath).Query.ShouldBe("?page=2&pageSize=10");
    }

    [Theory]
    [InlineData(1, 25, "/p/paperplane/kb/accounts?page=2", null)]
    [InlineData(3, 25, null, "/p/paperplane/kb/accounts?page=2")]
    public async Task The_first_page_has_no_previous_link_and_the_last_has_no_next_link(int page, int total, string? next, string? previous)
    {
        await using var factory = WithList(page, total);
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, $"/p/paperplane/kb/accounts?page={page}", Ct);

        (dom.QuerySelector("a[rel=next]")?.GetAttribute("href")).ShouldBe(next);
        (dom.QuerySelector("a[rel=prev]")?.GetAttribute("href")).ShouldBe(previous);
    }

    [Theory]
    [InlineData("?page=abc", "?page=1&pageSize=10")]
    [InlineData("?page=-3", "?page=1&pageSize=10")]
    [InlineData("?page=0", "?page=1&pageSize=10")]
    [InlineData("?page=", "?page=1&pageSize=10")]
    [InlineData("?page=1.5", "?page=1&pageSize=10")]
    [InlineData("?page=99999999999", "?page=1&pageSize=10")]
    [InlineData("?page=2&page=3", "?page=2&pageSize=10")]
    [InlineData("?PAGE=2", "?page=2&pageSize=10")]
    [InlineData("?utm=1", "?page=1&pageSize=10")]
    public async Task A_page_value_that_is_not_a_whole_number_is_page_one_and_never_a_500(string query, string expectedApiQuery)
    {
        await using var factory = WithList();
        using var client = FormTestKit.Client(factory);

        var (response, _, _) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts" + query, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, query);
        factory.Api.Requests.Single(request => request.Path == KbTestKit.CategoryArticlesPath).Query.ShouldBe(expectedApiQuery, query);
    }

    [Fact]
    public async Task The_head_is_unique_per_page_and_the_canonical_address_names_the_page()
    {
        await using var factory = WithList(2, 25);
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts?page=2&utm=x", Ct);

        dom.Title.ShouldBe("Accounts (page 2) - Paperplane Help Centre");
        KbTestKit.Meta(dom, "meta[name=description]").ShouldBe("Help articles about Accounts for Paperplane.");
        dom.QuerySelector("link[rel=canonical]")!.GetAttribute("href").ShouldBe(PortalFactory.PublicUrl + "/p/paperplane/kb/accounts?page=2");
        KbTestKit.Meta(dom, "meta[name=robots]").ShouldStartWith("index, follow");
        KbTestKit.Meta(dom, "meta[property='og:type']").ShouldBe("website");
    }

    [Fact]
    public async Task The_first_page_title_has_no_page_number_and_the_canonical_has_no_query()
    {
        await using var factory = WithList();
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts", Ct);

        dom.Title.ShouldBe("Accounts - Paperplane Help Centre");
        dom.QuerySelector("link[rel=canonical]")!.GetAttribute("href").ShouldBe(PortalFactory.PublicUrl + "/p/paperplane/kb/accounts");
    }

    [Theory]
    [InlineData("/p/paperplane/kb/accounts")]
    [InlineData("/p/paperplane/kb/accounts/")]
    [InlineData("/P/paperplane/KB/accounts")]
    public async Task A_category_answers_the_same_whatever_the_case_of_the_fixed_segments_or_a_trailing_slash(string path)
    {
        await using var factory = WithList();
        using var client = FormTestKit.Client(factory);

        var (response, _, dom) = await KbTestKit.GetAsync(client, path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        KbTestKit.Texts(dom, "h1").ShouldBe(["Accounts"]);
    }

    [Fact]
    public async Task An_unknown_invisible_or_empty_category_is_the_neutral_404_byte_for_byte_with_no_theme()
    {
        var neutral = await Seen.NeutralNotFoundAsync(Ct, "/p/nope");
        await using var factory = KbTestKit.Factory();
        KbTestKit.Problem(factory, "/api/public/kb/paperplane/categories/gone/articles", HttpStatusCode.NotFound);
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync("/p/paperplane/kb/gone", Ct);
        var seen = await Seen.OfAsync(response, factory.Api.Requests.Count, Ct);

        seen.ShouldBeTheNeutralNotFound(neutral);
        seen.Body.ShouldNotContain("Paperplane");
        factory.Api.Requests.Select(request => request.Path).ShouldBe([KbTestKit.ProductPath, "/api/public/kb/paperplane/categories/gone/articles"]);
    }

    [Theory]
    [InlineData("/p/paperplane/kb/Bad_Slug")]
    [InlineData("/p/paperplane/kb/UPPER")]
    [InlineData("/p/paperplane/kb/-x")]
    [InlineData("/p/paperplane/kb/x--y")]
    [InlineData("/p/paperplane/kb/a%20b")]
    [InlineData("/p/paperplane/kb/a%3Fpage%3D2")]
    public async Task A_category_slug_that_is_not_a_slug_is_the_neutral_404_and_the_category_is_never_asked_for(string path)
    {
        var neutral = await Seen.NeutralNotFoundAsync(Ct, "/p/nope");
        await using var factory = KbTestKit.Factory();
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync(path, Ct);
        var seen = await Seen.OfAsync(response, factory.Api.Requests.Count, Ct);

        seen.ShouldBeTheNeutralNotFound(neutral);
        factory.Api.Requests.Select(request => request.Path).ShouldBe([KbTestKit.ProductPath], path);
    }

    [Fact]
    public async Task A_page_past_the_end_is_the_neutral_404_and_not_an_empty_page_for_a_crawler_to_find()
    {
        var neutral = await Seen.NeutralNotFoundAsync(Ct, "/p/nope");
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoryArticlesPath, KbTestKit.Page(9, 10, 3));
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync("/p/paperplane/kb/accounts?page=9", Ct);
        var seen = await Seen.OfAsync(response, factory.Api.Requests.Count, Ct);

        seen.ShouldBeTheNeutralNotFound(neutral);
    }

    [Fact]
    public async Task The_search_page_is_not_a_category_the_literal_segment_wins()
    {
        await using var factory = KbTestKit.Factory();
        using var client = FormTestKit.Client(factory);

        var (response, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/search", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        KbTestKit.Texts(dom, ".ts-state h2").ShouldBe(["What are you looking for?"]);
        factory.Api.Requests.ShouldAllBe(request => !request.Path.Contains("/categories/", StringComparison.Ordinal), "search is not asked for as a category");
    }

    [Fact]
    public async Task A_failing_list_call_is_a_calm_503_and_a_429_is_a_429()
    {
        await using var down = KbTestKit.Factory();
        down.Api.OnStatus(HttpMethod.Get, KbTestKit.CategoryArticlesPath, HttpStatusCode.BadGateway);
        using var downClient = FormTestKit.Client(down);
        await using var busy = KbTestKit.Factory();
        busy.Api.OnStatus(HttpMethod.Get, KbTestKit.CategoryArticlesPath, HttpStatusCode.TooManyRequests);
        using var busyClient = FormTestKit.Client(busy);

        var (downResponse, downHtml, _) = await KbTestKit.GetAsync(downClient, "/p/paperplane/kb/accounts", Ct);
        var (busyResponse, busyHtml, _) = await KbTestKit.GetAsync(busyClient, "/p/paperplane/kb/accounts", Ct);

        downResponse.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        downHtml.ShouldContain("We could not reach our support system.");
        busyResponse.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        busyHtml.ShouldContain("You have sent a lot in a short time.");
    }

    [Fact]
    public async Task An_unknown_product_is_the_neutral_404_and_the_category_is_never_asked_for()
    {
        var neutral = await Seen.NeutralNotFoundAsync(Ct, "/p/nope");
        await using var factory = FormTestKit.Factory(product: false);
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/gone", HttpStatusCode.NotFound, "product-not-found", "No such product.");
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync("/p/gone/kb/accounts", Ct);
        var seen = await Seen.OfAsync(response, factory.Api.Requests.Count, Ct);

        seen.ShouldBeTheNeutralNotFound(neutral);
        factory.Api.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/public/products/gone");
    }
}
