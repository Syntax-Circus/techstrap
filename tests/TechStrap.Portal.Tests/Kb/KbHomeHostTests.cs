using System.Net;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Forms;
using TechStrap.Portal.Tests.Tickets;

namespace TechStrap.Portal.Tests.Kb;

/// <summary>
/// P09-T12 (the help-centre home) at the host, with a stub API behind the Portal: the categories with their counts and descriptions, the empty state, the head, the neutral 404 for an unknown product (no KB call is made)
/// and the calm failure states. Review Focus 1 (names and descriptions are plain text and encoded) and 2 (an unknown product tells nothing).
/// </summary>
public sealed class KbHomeHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static PublicKbCategoryDto[] Categories() =>
    [
        new("accounts", "Accounts", "Sign-in, passwords and security", 4),
        new("billing", "Billing", null, 1),
    ];

    [Fact]
    public async Task The_home_lists_each_category_with_its_description_and_article_count_and_a_search_box()
    {
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoriesPath, Categories());
        using var client = FormTestKit.Client(factory);

        var (response, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        KbTestKit.Texts(dom, "h1").ShouldBe(["Help centre"]);
        KbTestKit.Texts(dom, ".ts-kb-list h2").ShouldBe(["Accounts", "Billing"]);
        KbTestKit.Links(dom, ".ts-kb-list h2 a").ShouldBe(["/p/paperplane/kb/accounts", "/p/paperplane/kb/billing"]);
        KbTestKit.Texts(dom, ".ts-kb-list .ts-kb-summary").ShouldBe(["Sign-in, passwords and security"]);
        KbTestKit.Texts(dom, ".ts-kb-list .ts-kb-meta").ShouldBe(["4 articles", "1 article"]);
        KbTestKit.Texts(dom, "nav.ts-breadcrumbs li").ShouldBe(["Paperplane", "Help centre"]);
        KbTestKit.Links(dom, "nav.ts-breadcrumbs a").ShouldBe(["/p/paperplane"]);
        dom.QuerySelector("nav.ts-breadcrumbs li[aria-current=page]")!.TextContent.ShouldBe("Help centre");
        var form = dom.QuerySelector("form[role=search]")!;
        form.GetAttribute("method").ShouldBe("get");
        form.GetAttribute("action").ShouldBe("/p/paperplane/kb/search");
        form.QuerySelector("input[name=q]")!.GetAttribute("maxlength").ShouldBe("200");
        factory.Api.Requests.Select(request => request.Path).ShouldBe([KbTestKit.ProductPath, KbTestKit.CategoriesPath]);
        factory.Api.Requests.ShouldAllBe(request => request.Client == ApiClientNames.Read);
    }

    [Fact]
    public async Task A_name_and_a_description_are_plain_text_and_encoded_never_markup()
    {
        await using var factory = KbTestKit.Factory();
        const string Name = "<script>alert(1)</script> & <b>\"quoted\"</b>";
        const string Description = "<img src=x onerror=alert(1)> <a href=\"javascript:alert(2)\">click</a>";
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoriesPath, new[] { new PublicKbCategoryDto("accounts", Name, Description, 2) });
        using var client = FormTestKit.Client(factory);

        var (_, html, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb", Ct);

        html.ShouldNotContain("<script>alert(1)");
        html.ShouldNotContain("<img src=x");
        html.ShouldNotContain("<b>\"quoted\"");
        html.ShouldContain("&lt;script&gt;alert(1)&lt;/script&gt;");
        KbTestKit.Texts(dom, ".ts-kb-list h2")[0].ShouldBe(Name);
        KbTestKit.Texts(dom, ".ts-kb-summary")[0].ShouldBe(Description);
        dom.QuerySelectorAll(".ts-kb-list script, .ts-kb-list img, .ts-kb-list b").Length.ShouldBe(0);
        dom.QuerySelectorAll("a[href^=javascript]").Length.ShouldBe(0);
    }

    [Fact]
    public async Task A_product_with_no_category_shows_the_empty_state_with_a_way_to_contact_support_and_is_not_an_error()
    {
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoriesPath, Array.Empty<PublicKbCategoryDto>());
        using var client = FormTestKit.Client(factory);

        var (response, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        KbTestKit.Texts(dom, ".ts-state h2").ShouldBe(["No articles yet"]);
        KbTestKit.Links(dom, ".ts-state a").ShouldBe(["/p/paperplane/contact"]);
        dom.QuerySelectorAll(".ts-kb-list").Length.ShouldBe(0);
    }

    [Fact]
    public async Task The_head_has_a_unique_title_a_description_the_canonical_address_open_graph_and_allows_indexing()
    {
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoriesPath, Categories());
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb?utm_source=mail", Ct);

        dom.Title.ShouldBe("Paperplane Help Centre");
        KbTestKit.Meta(dom, "meta[name=description]").ShouldBe("Help articles and answers for Paperplane.");
        dom.QuerySelector("link[rel=canonical]")!.GetAttribute("href").ShouldBe(PortalFactory.PublicUrl + "/p/paperplane/kb", "the canonical address has no query and comes from the public URL, never the Host header");
        KbTestKit.Meta(dom, "meta[name=robots]").ShouldStartWith("index, follow");
        KbTestKit.Meta(dom, "meta[property='og:title']").ShouldBe("Paperplane Help Centre");
        KbTestKit.Meta(dom, "meta[property='og:url']").ShouldBe(PortalFactory.PublicUrl + "/p/paperplane/kb");
        KbTestKit.Meta(dom, "meta[property='og:image']").ShouldBe(PortalFactory.PublicUrl + "/icon-512.png", "a product with no logo uses the Portal's own image, never the bare site address");
        KbTestKit.Meta(dom, "meta[property='og:image:alt']").ShouldBe("Paperplane");
    }

    [Fact]
    public async Task The_open_graph_image_is_the_products_logo_when_it_has_an_acceptable_one()
    {
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.ProductPath, FormTestKit.Product() with { LogoPath = "https://cdn.example.com/paperplane.png" });
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoriesPath, Categories());
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb", Ct);

        KbTestKit.Meta(dom, "meta[property='og:image']").ShouldBe("https://cdn.example.com/paperplane.png");
    }

    [Fact]
    public async Task The_page_is_themed_with_the_products_header_and_sets_no_cookie()
    {
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoriesPath, Categories());
        using var client = FormTestKit.Client(factory);

        var (response, html, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb", Ct);

        html.ShouldContain("--ts-accent:#F59E0B");
        dom.QuerySelector("header.ts-product-header .ts-product-name")!.TextContent.Trim().ShouldBe("Paperplane");
        KbTestKit.Header(response, "Set-Cookie").ShouldBeEmpty("a cookie would stop the response being kept (and would need a notice)");
    }

    [Theory]
    [InlineData("/p/gone/kb")]
    [InlineData("/p/BAD_KEY/kb")]
    [InlineData("/p/-x/kb")]
    public async Task An_unknown_or_malformed_product_is_the_neutral_404_and_no_kb_call_is_made(string path)
    {
        var neutral = await Seen.NeutralNotFoundAsync(Ct, "/p/nope");
        await using var factory = FormTestKit.Factory(product: false);
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/gone", HttpStatusCode.NotFound, "product-not-found", "No such product.");
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync(path, Ct);
        var seen = await Seen.OfAsync(response, factory.Api.Requests.Count, Ct);

        seen.ShouldBeTheNeutralNotFound(neutral);
        factory.Api.Requests.ShouldAllBe(request => request.Path.StartsWith("/api/public/products/", StringComparison.Ordinal), "no KB endpoint is called for a product that is not there");
    }

    [Fact]
    public async Task A_failing_categories_call_is_a_calm_503_in_the_products_theme_and_a_429_is_a_429_and_the_apis_words_are_never_shown()
    {
        await using var down = KbTestKit.Factory();
        KbTestKit.Problem(down, KbTestKit.CategoriesPath, HttpStatusCode.InternalServerError, "kb-secret-table");
        using var downClient = FormTestKit.Client(down);
        await using var busy = KbTestKit.Factory();
        busy.Api.OnStatus(HttpMethod.Get, KbTestKit.CategoriesPath, HttpStatusCode.TooManyRequests);
        using var busyClient = FormTestKit.Client(busy);

        var (downResponse, downHtml, downDom) = await KbTestKit.GetAsync(downClient, "/p/paperplane/kb", Ct);
        var (busyResponse, busyHtml, _) = await KbTestKit.GetAsync(busyClient, "/p/paperplane/kb", Ct);

        downResponse.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        downHtml.ShouldContain(ProblemCopy.ApiUnavailable);
        downHtml.ShouldNotContain("kb-secret-table");
        downHtml.ShouldNotContain("Whatever the API says");
        downDom.QuerySelector("header.ts-product-header")!.TextContent.ShouldContain("Paperplane");
        busyResponse.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        busyHtml.ShouldContain(ProblemCopy.RateLimited);
        KbTestKit.Header(downResponse, "Cache-Control").ShouldNotContain("public, max-age=60");
    }
}
