using System.Net;
using System.Text.Json;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Forms;
using TechStrap.Portal.Tests.Tickets;

namespace TechStrap.Portal.Tests.Kb;

/// <summary>
/// P09-T14 (the article page) at the host: the body is the API's HTML byte for byte, the head (title, description, canonical, Open Graph) and the two JSON-LD blocks parse, and Review Focus 1 and 2: a hostile title,
/// category or summary cannot break out of the page or of the JSON-LD, the body is the only markup, and an unpublished article, a wrong category, an unknown slug and an unknown product are the one neutral 404.
/// </summary>
public sealed class KbArticleHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private const string ArticlePath = "/api/public/kb/paperplane/articles/accounts/reset-password";

    // What the API's sanitiser produces: headings, a paragraph with a link, a list, a table, code and an image.
    private const string Body =
        "<h2>Steps</h2>\n<p>Open <a href=\"https://app.example.com/settings\" rel=\"nofollow\">settings</a> &amp; choose <em>Reset</em>. Then wait.</p>\n<ul>\n<li>One</li>\n<li>Two &lt;3</li>\n</ul>\n"
        + "<table>\n<thead><tr><th>A</th><th>B</th></tr></thead>\n<tbody><tr><td>1</td><td>2</td></tr></tbody>\n</table>\n<pre><code class=\"language-bash\">echo &quot;hi&quot;\n</code></pre>\n"
        + "<p><img src=\"https://api.example.com/kb-images/a.png\" alt=\"The settings screen\"></p>\n";

    private static PublishedKbArticleDto Published(string title = "Reset your password", string? summary = "How to reset it", string html = Body, string? product = "paperplane", string category = "accounts", string categoryName = "Accounts", string slug = "reset-password") =>
        new(product, category, categoryName, slug, title, summary, html, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero), KbTestKit.Updated);

    private static PortalFactory With(PublishedKbArticleDto article)
    {
        var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, $"/api/public/kb/paperplane/articles/{article.CategorySlug}/{article.Slug}", article);
        return factory;
    }

    private static JsonDocument[] JsonLd(AngleSharp.Html.Dom.IHtmlDocument dom) =>
        [.. dom.QuerySelectorAll("script[type='application/ld+json']").Select(script => JsonDocument.Parse(script.TextContent))];

    [Fact]
    public async Task The_body_is_the_apis_html_byte_for_byte_inside_one_container()
    {
        await using var factory = With(Published());
        using var client = FormTestKit.Client(factory);

        var (response, html, _) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts/reset-password", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain($"<div class=\"ts-kb-article-body\">{Body}</div>", Case.Sensitive);
        factory.Api.Requests.Select(request => request.Path).ShouldBe([KbTestKit.ProductPath, ArticlePath]);
        factory.Api.Requests.ShouldAllBe(request => request.Client == ApiClientNames.Read);
    }

    [Fact]
    public async Task The_page_has_the_title_the_updated_day_the_trail_and_a_way_to_contact_support()
    {
        await using var factory = With(Published());
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts/reset-password", Ct);

        KbTestKit.Texts(dom, "h1").ShouldBe(["Reset your password"]);
        KbTestKit.Texts(dom, "article.ts-kb-article .ts-kb-meta").ShouldBe(["Updated 5 Oct 2026"]);
        KbTestKit.Texts(dom, "nav.ts-breadcrumbs li").ShouldBe(["Paperplane", "Help centre", "Accounts", "Reset your password"]);
        KbTestKit.Links(dom, "nav.ts-breadcrumbs a").ShouldBe(["/p/paperplane", "/p/paperplane/kb", "/p/paperplane/kb/accounts"]);
        KbTestKit.Texts(dom, "aside.ts-kb-help h2").ShouldBe(["Still need help?"]);
        KbTestKit.Links(dom, "aside.ts-kb-help a").ShouldBe(["/p/paperplane/contact"]);
        dom.QuerySelectorAll(".ts-kb-article-body table, .ts-kb-article-body pre, .ts-kb-article-body img").Length.ShouldBe(3);
    }

    [Fact]
    public async Task The_head_has_a_unique_title_the_summary_as_description_the_canonical_address_and_open_graph_for_an_article()
    {
        await using var factory = With(Published());
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts/reset-password?utm_source=mail", Ct);

        dom.Title.ShouldBe("Reset your password - Paperplane Help Centre");
        KbTestKit.Meta(dom, "meta[name=description]").ShouldBe("How to reset it");
        dom.QuerySelector("link[rel=canonical]")!.GetAttribute("href").ShouldBe(PortalFactory.PublicUrl + "/p/paperplane/kb/accounts/reset-password");
        KbTestKit.Meta(dom, "meta[name=robots]").ShouldStartWith("index, follow");
        KbTestKit.Meta(dom, "meta[property='og:type']").ShouldBe("article");
        KbTestKit.Meta(dom, "meta[property='og:title']").ShouldBe("Reset your password - Paperplane Help Centre");
        KbTestKit.Meta(dom, "meta[property='og:description']").ShouldBe("How to reset it");
        KbTestKit.Meta(dom, "meta[property='og:url']").ShouldBe(PortalFactory.PublicUrl + "/p/paperplane/kb/accounts/reset-password");
        KbTestKit.Meta(dom, "meta[property='og:image']").ShouldBe(PortalFactory.PublicUrl + "/icon-512.png");
        KbTestKit.Meta(dom, "meta[name='twitter:title']").ShouldBe("Reset your password - Paperplane Help Centre");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Without_a_summary_the_description_is_the_first_sentence_of_the_body_as_plain_text(string? summary)
    {
        await using var factory = With(Published(summary: summary));
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts/reset-password", Ct);

        KbTestKit.Meta(dom, "meta[name=description]").ShouldBe("Open settings & choose Reset.");
        using var page = JsonLd(dom)[1];
        page.RootElement.GetProperty("description").GetString().ShouldBe("Open settings & choose Reset.");
    }

    [Fact]
    public async Task With_neither_a_summary_nor_any_text_the_description_names_the_article_and_the_product()
    {
        await using var factory = With(Published(summary: null, html: "<p><img src=\"https://api.example.com/kb-images/a.png\" alt=\"\"></p>"));
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts/reset-password", Ct);

        KbTestKit.Meta(dom, "meta[name=description]").ShouldBe("Reset your password. Help article for Paperplane.");
    }

    [Fact]
    public async Task The_page_carries_a_breadcrumb_list_and_an_article_as_json_that_parses()
    {
        await using var factory = With(Published());
        using var client = FormTestKit.Client(factory);

        var (_, html, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts/reset-password", Ct);

        html.ShouldContain("type=\"application/ld&#x2B;json\"", Case.Sensitive);
        var blocks = JsonLd(dom);
        blocks.Length.ShouldBe(2);
        blocks[0].RootElement.GetProperty("@type").GetString().ShouldBe("BreadcrumbList");
        blocks[0].RootElement.GetProperty("itemListElement").EnumerateArray().Select(item => item.GetProperty("item").GetString()).ShouldBe(
        [
            PortalFactory.PublicUrl + "/p/paperplane",
            PortalFactory.PublicUrl + "/p/paperplane/kb",
            PortalFactory.PublicUrl + "/p/paperplane/kb/accounts",
            PortalFactory.PublicUrl + "/p/paperplane/kb/accounts/reset-password",
        ]);
        blocks[1].RootElement.GetProperty("@type").GetString().ShouldBe("Article");
        blocks[1].RootElement.GetProperty("headline").GetString().ShouldBe("Reset your password");
        blocks[1].RootElement.GetProperty("datePublished").GetDateTimeOffset().ShouldBe(new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        blocks[1].RootElement.GetProperty("mainEntityOfPage").GetProperty("@id").GetString().ShouldBe(PortalFactory.PublicUrl + "/p/paperplane/kb/accounts/reset-password");
        foreach (var block in blocks)
        {
            block.Dispose();
        }
    }

    [Fact]
    public async Task The_json_ld_is_data_not_script_and_the_security_policy_is_unchanged()
    {
        await using var factory = With(Published());
        using var client = FormTestKit.Client(factory);

        var (response, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts/reset-password", Ct);

        var policy = KbTestKit.Header(response, "Content-Security-Policy").Single().Split(';', StringSplitOptions.TrimEntries);
        policy.ShouldContain("script-src 'self'");
        policy.ShouldNotContain(directive => directive.Contains("unsafe-inline", StringComparison.Ordinal) && directive.StartsWith("script-src", StringComparison.Ordinal));
        dom.QuerySelectorAll("script[type='application/ld+json']").ShouldAllBe(script => script.GetAttribute("src") == null);
        dom.QuerySelectorAll("script:not([type='application/ld+json'])").ShouldAllBe(script => script.GetAttribute("src") != null, "every executable script is an external file");
    }

    [Theory]
    [InlineData("</script><img src=x onerror=alert(1)>")]
    [InlineData("<!-- x --> <script>alert(1)</script>")]
    [InlineData("\"><svg onload=alert(1)>")]
    public async Task A_hostile_title_category_and_summary_cannot_break_out_of_the_page_the_head_or_the_json_ld(string hostile)
    {
        await using var factory = With(Published(hostile, hostile, categoryName: hostile));
        using var client = FormTestKit.Client(factory);

        var (_, html, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts/reset-password", Ct);

        // Not one element or event handler of the hostile text exists anywhere in the document.
        dom.QuerySelectorAll("img[src=x], svg, [onerror], [onload]").Length.ShouldBe(0);
        dom.QuerySelectorAll("script:not([src]):not([type='application/ld+json'])").Length.ShouldBe(0);
        html.ShouldNotContain("<img src=x");
        html.ShouldNotContain("<svg onload");
        html.ShouldNotContain("<script>alert(1)");

        // The text is still all there, as text: in the heading, the title, the meta tags and the trail.
        KbTestKit.Texts(dom, "h1").ShouldBe([hostile]);
        dom.Title.ShouldBe(hostile + " - Paperplane Help Centre");
        KbTestKit.Meta(dom, "meta[name=description]").ShouldBe(hostile);
        KbTestKit.Meta(dom, "meta[property='og:title']").ShouldBe(hostile + " - Paperplane Help Centre");
        KbTestKit.Texts(dom, "nav.ts-breadcrumbs li").ShouldBe(["Paperplane", "Help centre", hostile, hostile]);

        // The JSON-LD blocks hold no angle bracket, are valid JSON, and read back as the original text.
        var scripts = dom.QuerySelectorAll("script[type='application/ld+json']").Select(script => script.TextContent).ToList();
        scripts.Count.ShouldBe(2);
        scripts.ShouldAllBe(script => !script.Contains('<') && !script.Contains('>') && !script.Contains('&'));
        using var breadcrumbs = JsonDocument.Parse(scripts[0]);
        breadcrumbs.RootElement.GetProperty("itemListElement")[3].GetProperty("name").GetString().ShouldBe(hostile);
        using var article = JsonDocument.Parse(scripts[1]);
        article.RootElement.GetProperty("headline").GetString().ShouldBe(hostile);
        article.RootElement.GetProperty("description").GetString().ShouldBe(hostile);
    }

    [Fact]
    public async Task A_hostile_product_name_cannot_break_out_of_the_json_ld_either()
    {
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.ProductPath, FormTestKit.Product("</script><img src=x onerror=alert(1)>"));
        factory.Api.OnJson(HttpMethod.Get, ArticlePath, Published());
        using var client = FormTestKit.Client(factory);

        var (_, html, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts/reset-password", Ct);

        html.ShouldNotContain("<img src=x");
        dom.QuerySelectorAll("img[src=x]").Length.ShouldBe(0);
        dom.QuerySelectorAll("script[type='application/ld+json']").Select(script => script.TextContent).ShouldAllBe(script => !script.Contains('<'));
    }

    [Fact]
    public async Task The_summary_is_never_markup_and_the_body_is_the_only_place_that_is()
    {
        await using var factory = With(Published(summary: "<b>bold</b> <img src=x onerror=alert(1)>"));
        using var client = FormTestKit.Client(factory);

        var (_, html, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts/reset-password", Ct);

        html.ShouldNotContain("<img src=x");
        KbTestKit.Meta(dom, "meta[name=description]").ShouldBe("<b>bold</b> <img src=x onerror=alert(1)>");
        dom.QuerySelectorAll("img[src=x]").Length.ShouldBe(0);
        dom.QuerySelectorAll(".ts-kb-article-body em").Length.ShouldBe(1, "the body's own markup is intact");
    }

    [Fact]
    public async Task A_shared_article_is_shown_and_canonical_under_the_product_the_visitor_is_on()
    {
        await using var factory = With(Published(product: null, category: "general", categoryName: "General", slug: "shared-tips", title: "Shared tips"));
        using var client = FormTestKit.Client(factory);

        var (response, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/general/shared-tips", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        dom.QuerySelector("link[rel=canonical]")!.GetAttribute("href").ShouldBe(PortalFactory.PublicUrl + "/p/paperplane/kb/general/shared-tips");
        KbTestKit.Links(dom, "nav.ts-breadcrumbs a").ShouldBe(["/p/paperplane", "/p/paperplane/kb", "/p/paperplane/kb/general"]);
    }

    [Fact]
    public async Task An_unpublished_article_a_wrong_category_and_an_unknown_slug_are_the_neutral_404_byte_for_byte_with_no_theme()
    {
        var neutral = await Seen.NeutralNotFoundAsync(Ct, "/p/nope");
        await using var factory = KbTestKit.Factory();
        foreach (var path in new[] { "/api/public/kb/paperplane/articles/accounts/draft", "/api/public/kb/paperplane/articles/billing/reset-password", "/api/public/kb/paperplane/articles/accounts/no-such" })
        {
            KbTestKit.Problem(factory, path, HttpStatusCode.NotFound, "kb-article-not-found");
        }

        using var client = FormTestKit.Client(factory);
        var answers = new List<Seen>();
        foreach (var path in new[] { "/p/paperplane/kb/accounts/draft", "/p/paperplane/kb/billing/reset-password", "/p/paperplane/kb/accounts/no-such" })
        {
            using var response = await client.GetAsync(path, Ct);
            answers.Add(await Seen.OfAsync(response, factory.Api.Requests.Count, Ct));
        }

        foreach (var seen in answers)
        {
            seen.ShouldBeTheNeutralNotFound(neutral);
            seen.Body.ShouldNotContain("Paperplane");
            seen.Body.ShouldBe(answers[0].Body, "an unpublished article, a wrong category and an unknown slug are indistinguishable");
            seen.Headers.ShouldBe(answers[0].Headers);
        }
    }

    [Theory]
    [InlineData("/p/paperplane/kb/Bad_Cat/reset-password")]
    [InlineData("/p/paperplane/kb/accounts/Bad_Slug")]
    [InlineData("/p/paperplane/kb/accounts/-x")]
    [InlineData("/p/paperplane/kb/accounts/a%2Fb")]
    [InlineData("/p/paperplane/kb/accounts/x--y")]
    public async Task A_category_or_slug_that_is_not_a_slug_is_the_neutral_404_and_the_article_is_never_asked_for(string path)
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
    public async Task A_path_under_the_search_page_is_an_article_route_with_the_reserved_category_and_the_api_refuses_it()
    {
        var neutral = await Seen.NeutralNotFoundAsync(Ct, "/p/nope");
        await using var factory = KbTestKit.Factory();
        KbTestKit.Problem(factory, "/api/public/kb/paperplane/articles/search/anything", HttpStatusCode.NotFound, "kb-article-not-found");
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync("/p/paperplane/kb/search/anything", Ct);
        var seen = await Seen.OfAsync(response, factory.Api.Requests.Count, Ct);

        seen.ShouldBeTheNeutralNotFound(neutral);
        factory.Api.Requests.Select(request => request.Path).ShouldBe([KbTestKit.ProductPath, "/api/public/kb/paperplane/articles/search/anything"]);
        factory.Api.Requests.ShouldAllBe(request => !request.Path.EndsWith("/search", StringComparison.Ordinal), "the search endpoint is never called for it");
    }

    [Fact]
    public async Task An_unknown_product_is_the_neutral_404_and_the_article_is_never_asked_for()
    {
        var neutral = await Seen.NeutralNotFoundAsync(Ct, "/p/nope");
        await using var factory = FormTestKit.Factory(product: false);
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/gone", HttpStatusCode.NotFound, "product-not-found", "No such product.");
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync("/p/gone/kb/accounts/reset-password", Ct);
        var seen = await Seen.OfAsync(response, factory.Api.Requests.Count, Ct);

        seen.ShouldBeTheNeutralNotFound(neutral);
        factory.Api.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/public/products/gone");
    }

    [Fact]
    public async Task A_failing_article_call_is_a_calm_503_and_a_429_is_a_429()
    {
        await using var down = KbTestKit.Factory();
        down.Api.OnStatus(HttpMethod.Get, ArticlePath, HttpStatusCode.ServiceUnavailable);
        using var downClient = FormTestKit.Client(down);
        await using var busy = KbTestKit.Factory();
        busy.Api.OnStatus(HttpMethod.Get, ArticlePath, HttpStatusCode.TooManyRequests);
        using var busyClient = FormTestKit.Client(busy);

        var (downResponse, downHtml, _) = await KbTestKit.GetAsync(downClient, "/p/paperplane/kb/accounts/reset-password", Ct);
        var (busyResponse, busyHtml, _) = await KbTestKit.GetAsync(busyClient, "/p/paperplane/kb/accounts/reset-password", Ct);

        downResponse.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        downHtml.ShouldContain("We could not reach our support system.");
        busyResponse.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        busyHtml.ShouldContain("You have sent a lot in a short time.");
    }

    [Fact]
    public async Task An_article_is_kept_for_a_minute_a_404_is_not_and_no_page_sets_a_cookie()
    {
        await using var factory = With(Published());
        KbTestKit.Problem(factory, "/api/public/kb/paperplane/articles/accounts/gone", HttpStatusCode.NotFound, "kb-article-not-found");
        using var client = FormTestKit.Client(factory);

        using var first = await client.GetAsync("/p/paperplane/kb/accounts/reset-password", Ct);
        using var second = await client.GetAsync("/p/paperplane/kb/accounts/reset-password", Ct);
        using var goneFirst = await client.GetAsync("/p/paperplane/kb/accounts/gone", Ct);
        using var goneSecond = await client.GetAsync("/p/paperplane/kb/accounts/gone", Ct);

        factory.Api.Count(HttpMethod.Get, ArticlePath).ShouldBe(1);
        factory.Api.Count(HttpMethod.Get, "/api/public/kb/paperplane/articles/accounts/gone").ShouldBe(2);
        first.Headers.CacheControl!.ToString().ShouldBe("public, max-age=60");
        KbTestKit.Header(second, "Age").ShouldNotBeEmpty();
        goneFirst.Headers.CacheControl?.Public.ShouldNotBe(true);
        foreach (var response in new[] { first, second, goneFirst, goneSecond })
        {
            KbTestKit.Header(response, "Set-Cookie").ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task The_body_html_reaches_the_page_even_when_it_looks_odd_so_the_portal_never_rewrites_what_the_api_decided()
    {
        const string Odd = "<p>&nbsp;&copy; 2026 &#x2B; &amp;amp; tail</p>\n<blockquote>\n<p>Quote</p>\n</blockquote>\n";
        await using var factory = With(Published(html: Odd));
        using var client = FormTestKit.Client(factory);

        var (_, html, _) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts/reset-password", Ct);

        html.ShouldContain($"<div class=\"ts-kb-article-body\">{Odd}</div>", Case.Sensitive);
    }

    [Fact]
    public async Task The_product_logo_is_the_open_graph_image_when_it_has_an_acceptable_one()
    {
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.ProductPath, FormTestKit.Product() with { LogoPath = "https://cdn.example.com/paperplane.png" });
        factory.Api.OnJson(HttpMethod.Get, ArticlePath, Published());
        using var client = FormTestKit.Client(factory);

        var (_, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb/accounts/reset-password", Ct);

        KbTestKit.Meta(dom, "meta[property='og:image']").ShouldBe("https://cdn.example.com/paperplane.png");
        using var article = JsonLd(dom)[1];
        article.RootElement.GetProperty("image").GetString().ShouldBe("https://cdn.example.com/paperplane.png");
    }
}
