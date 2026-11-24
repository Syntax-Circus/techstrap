using System.Net;
using System.Text.Json;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Seo;
using TechStrap.Portal.Tests.Forms;

namespace TechStrap.Portal.Tests.Kb;

/// <summary>
/// The product home (what <c>/</c> is on a product host) and the help-center home at the host: a complete SEO head (description in range, canonical, Open Graph, structured data that parses), enough visible text for a
/// search engine not to call the page thin, and a product home that stays a 200 with its full head when the KB cannot be read (the Bing findings of 2026-11-24).
/// </summary>
public sealed class ProductHomeHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static PublicKbCategoryDto[] Categories() =>
    [
        new("accounts", "Accounts", "Sign-in, passwords, two-step verification and account security", 4),
        new("billing", "Billing", "Invoices, payment methods, refunds and changes to your plan", 3),
        new("troubleshooting", "Troubleshooting", "Fixes for the problems people run into most often", 5),
    ];

    private static PublicKbArticleSummaryDto[] Recent() =>
    [
        KbTestKit.Article("reset-password", "Reset your password", "Use the reset link on the sign-in page to choose a new password."),
        KbTestKit.Article("update-card", "Update your payment card", "Change the card on file from the billing page of your account.", "billing", "Billing"),
        KbTestKit.Article("cannot-sign-in", "You cannot sign in", "Check the address you typed, then try the reset link before contacting support.", "troubleshooting", "Troubleshooting"),
    ];

    private static async Task<(HttpResponseMessage Response, AngleSharp.Html.Dom.IHtmlDocument Dom)> SeededAsync(string path)
    {
        var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoriesPath, Categories());
        KbTestKit.StubRecentArticles(factory, Recent());
        var client = FormTestKit.Client(factory);
        var (response, _, dom) = await KbTestKit.GetAsync(client, path, Ct);
        return (response, dom);
    }

    private static string[] JsonLdTypes(AngleSharp.Html.Dom.IHtmlDocument dom) =>
        [.. dom.QuerySelectorAll("script[type='application/ld+json']").Select(script => JsonDocument.Parse(script.TextContent).RootElement.GetProperty("@type").GetString()!)];

    private static int Words(AngleSharp.Html.Dom.IHtmlDocument dom) =>
        dom.Body!.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    [Theory]
    [InlineData("/p/paperplane")]
    [InlineData("/p/paperplane/kb")]
    public async Task A_home_page_has_a_complete_head_with_a_description_in_range(string path)
    {
        var (response, dom) = await SeededAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        KbTestKit.Meta(dom, "meta[name=description]").Length.ShouldBeInRange(SeoText.DescriptionMinLength, SeoText.DescriptionMaxLength);
        dom.QuerySelector("link[rel=canonical]")!.GetAttribute("href").ShouldEndWith(path);
        KbTestKit.Meta(dom, "meta[property='og:title']").ShouldNotBeEmpty();
        KbTestKit.Meta(dom, "meta[property='og:description']").ShouldNotBeEmpty();
        KbTestKit.Meta(dom, "meta[property='og:image']").ShouldNotBeEmpty();
        KbTestKit.Meta(dom, "meta[name='twitter:card']").ShouldNotBeEmpty();
        dom.QuerySelectorAll("h1").Length.ShouldBe(1);
    }

    [Fact]
    public async Task The_product_home_has_real_content_and_a_website_with_search()
    {
        var (_, dom) = await SeededAsync("/p/paperplane");

        Words(dom).ShouldBeGreaterThanOrEqualTo(150);
        KbTestKit.Texts(dom, "main h2").ShouldBe(["Browse by topic", "Popular and recent articles", "Still need help?"]);
        KbTestKit.Links(dom, ".ts-kb-featured a").Length.ShouldBe(3);
        JsonLdTypes(dom).ShouldContain("WebSite");
        var site = JsonDocument.Parse(dom.QuerySelector("script[type='application/ld+json']")!.TextContent).RootElement;
        site.GetProperty("potentialAction").GetProperty("target").GetString().ShouldEndWith("/kb/search?q={search_term_string}");
    }

    [Fact]
    public async Task The_help_center_home_has_an_intro_the_newest_articles_and_structured_data()
    {
        var (_, dom) = await SeededAsync("/p/paperplane/kb");

        dom.QuerySelector(".ts-help-intro")!.TextContent.Length.ShouldBeGreaterThan(40);
        KbTestKit.Texts(dom, ".ts-kb-featured h2").ShouldBe(["Popular and recent articles"]);
        JsonLdTypes(dom).ShouldBe(["BreadcrumbList", "CollectionPage"]);
        Words(dom).ShouldBeGreaterThanOrEqualTo(100);
    }

    [Fact]
    public async Task A_product_home_stays_a_200_with_a_full_head_when_the_kb_cannot_be_read()
    {
        await using var factory = KbTestKit.Factory();
        KbTestKit.Problem(factory, KbTestKit.CategoriesPath, HttpStatusCode.ServiceUnavailable, "unavailable");
        KbTestKit.Problem(factory, KbTestKit.RecentArticlesPath, HttpStatusCode.ServiceUnavailable, "unavailable");
        using var client = FormTestKit.Client(factory);

        var (response, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        KbTestKit.Meta(dom, "meta[name=description]").Length.ShouldBeInRange(SeoText.DescriptionMinLength, SeoText.DescriptionMaxLength);
        dom.QuerySelector("link[rel=canonical]").ShouldNotBeNull();
        JsonLdTypes(dom).ShouldContain("WebSite");
        dom.QuerySelectorAll("h1").Length.ShouldBe(1);
        dom.QuerySelector(".ts-help-contact a")!.GetAttribute("href").ShouldBe("/p/paperplane/contact");
    }

    [Fact]
    public async Task The_help_center_home_leaves_the_article_list_out_when_only_that_call_fails()
    {
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoriesPath, Categories());
        KbTestKit.Problem(factory, KbTestKit.RecentArticlesPath, HttpStatusCode.ServiceUnavailable, "unavailable");
        using var client = FormTestKit.Client(factory);

        var (response, _, dom) = await KbTestKit.GetAsync(client, "/p/paperplane/kb", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        dom.QuerySelector(".ts-kb-featured").ShouldBeNull();
        KbTestKit.Texts(dom, ".ts-kb-list h2").ShouldBe(["Accounts", "Billing", "Troubleshooting"]);
    }

    [Fact]
    public async Task An_owner_written_description_and_intro_are_used_when_long_enough_and_encoded()
    {
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.ProductPath, Product("Quick answers for every Paperplane customer: how to sign in, fix a failed delivery, manage your plan and reach a real person on the support team.", "<b>Hello</b> & welcome"));
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoriesPath, Categories());
        using var client = FormTestKit.Client(factory);

        var (_, html, dom) = await KbTestKit.GetAsync(client, "/p/paperplane", Ct);

        KbTestKit.Meta(dom, "meta[name=description]").ShouldStartWith("Quick answers for every Paperplane customer");
        dom.QuerySelector(".ts-help-intro")!.TextContent.ShouldBe("<b>Hello</b> & welcome");
        html.ShouldNotContain("<b>Hello</b>");
    }

    private static object Product(string seoDescription, string intro) => new
    {
        key = "paperplane",
        displayName = "Paperplane",
        seoDescription,
        helpCenterIntro = intro,
    };
}
