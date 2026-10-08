using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using AngleSharp.Html.Dom;
using Microsoft.AspNetCore.Mvc.Testing;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Tests.Kb;

namespace TechStrap.Portal.Tests.Seo;

/// <summary>
/// P11e-T06 through the real pipeline: the canonical address, the JSON-LD addresses, the links and the sitemap of a page are those of the host the page is served on. A product host serves clean paths on its stored
/// host; the default host serves <c>/p/{key}</c> paths on the configured public URL; a hostile Host header is neither (Review Focus 1).
/// </summary>
public sealed class SeoProductHostTests
{
    private const string DragonHost = "support.dragonpoop.com";
    private const string ArticlePathOnHost = "/kb/accounts/reset-password";
    private static readonly DateTimeOffset Published = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    private static PortalFactory Host()
    {
        var factory = new PortalFactory();
        factory.Api.OnJson(
            HttpMethod.Get,
            "/api/public/products",
            new[] { new PublicProductSummaryDto("dragon-poop", "Dragon Poop", DragonHost), new PublicProductSummaryDto("who-flung-poo", "Who Flung Poo") });
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/dragon-poop", new PublicProductDto("dragon-poop", "Dragon Poop", null, "#F59E0B", "#000000", "#9D6507", DragonHost));
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/who-flung-poo", new PublicProductDto("who-flung-poo", "Who Flung Poo", null, "#2563EB", "#FFFFFF", "#2563EB"));
        foreach (var key in new[] { "dragon-poop", "who-flung-poo" })
        {
            factory.Api.OnJson(HttpMethod.Get, $"/api/public/kb/{key}/sitemap", new[] { new KbSitemapEntryDto(key, "accounts", "reset-password", KbTestKit.Updated) });
            factory.Api.OnJson(
                HttpMethod.Get,
                $"/api/public/kb/{key}/articles/accounts/reset-password",
                new PublishedKbArticleDto(key, "accounts", "Accounts", "reset-password", "Reset your password", "How to reset it", "<p>Open settings.</p>", Published, KbTestKit.Updated));
        }

        return factory;
    }

    private static HttpClient Client(PortalFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<(HttpResponseMessage Response, string Body)> GetAsync(CancellationToken ct, HttpClient client, string path, string host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = host;
        var response = await client.SendAsync(request, ct);
        return (response, await response.Content.ReadAsStringAsync(ct));
    }

    // Every string of every JSON-LD block that is an address.
    private static List<string> JsonLdUrls(IHtmlDocument dom)
    {
        var urls = new List<string>();
        foreach (var script in dom.QuerySelectorAll("script[type='application/ld+json']"))
        {
            using var document = JsonDocument.Parse(script.TextContent);
            Collect(document.RootElement, urls);
        }

        return urls;

        static void Collect(JsonElement element, List<string> urls)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String when element.GetString() is { } text && text.StartsWith("http", StringComparison.Ordinal) && !text.StartsWith("https://schema.org", StringComparison.Ordinal):
                    urls.Add(text);
                    break;
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        Collect(property.Value, urls);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        Collect(item, urls);
                    }

                    break;
            }
        }
    }

    private static string[] Locations(string xml) => [.. XDocument.Parse(xml).Descendants().Where(element => element.Name.LocalName == "loc").Select(element => element.Value)];

    [Fact(Timeout = 30_000)]
    public async Task On_a_product_host_the_canonical_the_json_ld_and_every_link_are_on_that_host_with_clean_paths()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Host();
        using var client = Client(factory);

        var (response, html) = await GetAsync(ct, client, ArticlePathOnHost, DragonHost);
        var dom = KbTestKit.Parse(html);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var expected = "https://support.dragonpoop.com" + ArticlePathOnHost;
        dom.QuerySelector("link[rel=canonical]")!.GetAttribute("href").ShouldBe(expected);
        KbTestKit.Meta(dom, "meta[property='og:url']").ShouldBe(expected);
        var urls = JsonLdUrls(dom);
        urls.ShouldContain(expected);
        urls.ShouldContain("https://support.dragonpoop.com/");
        urls.ShouldContain("https://support.dragonpoop.com/kb");
        urls.Where(url => !url.EndsWith(".png", StringComparison.Ordinal)).ShouldAllBe(url => url.StartsWith("https://support.dragonpoop.com/", StringComparison.Ordinal));
        var hrefs = KbTestKit.Links(dom, "a[href]");
        hrefs.ShouldContain("/contact");
        hrefs.ShouldContain("/kb");
        hrefs.ShouldContain("/lost-link");
        hrefs.ShouldNotContain(href => href.Contains("/p/", StringComparison.Ordinal), "a product host links the clean paths: " + string.Join(" ", hrefs));
        html.ShouldNotContain("/p/dragon-poop");
    }

    [Fact(Timeout = 30_000)]
    public async Task On_the_default_host_the_canonical_keeps_the_prefix_path_on_the_public_url()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Host();
        using var client = Client(factory);

        var (response, html) = await GetAsync(ct, client, "/p/who-flung-poo" + ArticlePathOnHost, "portal.test");
        var dom = KbTestKit.Parse(html);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var expected = PortalFactory.PublicUrl + "/p/who-flung-poo" + ArticlePathOnHost;
        dom.QuerySelector("link[rel=canonical]")!.GetAttribute("href").ShouldBe(expected);
        JsonLdUrls(dom).ShouldContain(expected);
        KbTestKit.Links(dom, "a[href]").ShouldContain("/p/who-flung-poo/contact");
    }

    [Theory(Timeout = 30_000)]
    [InlineData("evil.example")]
    [InlineData("support.dragonpoop.com.evil")]
    public async Task A_hostile_host_leaves_every_link_the_canonical_and_the_json_ld_on_the_public_url(string host)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Host();
        using var client = Client(factory);

        var (response, html) = await GetAsync(ct, client, "/p/who-flung-poo" + ArticlePathOnHost, host);
        var dom = KbTestKit.Parse(html);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldNotContain("evil");
        var expected = PortalFactory.PublicUrl + "/p/who-flung-poo" + ArticlePathOnHost;
        dom.QuerySelector("link[rel=canonical]")!.GetAttribute("href").ShouldBe(expected);
        KbTestKit.Meta(dom, "meta[property='og:url']").ShouldBe(expected);
        var hrefs = KbTestKit.Links(dom, "[href]");
        hrefs.ShouldNotBeEmpty();
        hrefs.Where(href => href.StartsWith("http", StringComparison.Ordinal)).ShouldAllBe(href => href.StartsWith(PortalFactory.PublicUrl + "/", StringComparison.Ordinal) || href.StartsWith("https://github.com/", StringComparison.Ordinal), "the powered-by link is the only address off the public URL");
        KbTestKit.Links(dom, "a[href^='/']").ShouldAllBe(href => href.StartsWith("/p/who-flung-poo", StringComparison.Ordinal));
        var urls = JsonLdUrls(dom);
        urls.ShouldContain(expected);
        urls.ShouldAllBe(url => url.StartsWith(PortalFactory.PublicUrl + "/", StringComparison.Ordinal));
        urls.Where(url => url.Contains("/kb", StringComparison.Ordinal)).ShouldAllBe(url => url.Contains("/p/who-flung-poo/", StringComparison.Ordinal));
    }

    [Fact(Timeout = 30_000)]
    public async Task The_sitemap_of_a_product_host_lists_only_its_product_with_clean_paths()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Host();
        using var client = Client(factory);

        var (response, xml) = await GetAsync(ct, client, "/sitemap.xml", DragonHost);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Locations(xml).ShouldBe(
        [
            "https://support.dragonpoop.com/",
            "https://support.dragonpoop.com/kb",
            "https://support.dragonpoop.com/kb/accounts",
            "https://support.dragonpoop.com/kb/accounts/reset-password",
        ]);
        factory.Api.Count(HttpMethod.Get, "/api/public/kb/who-flung-poo/sitemap").ShouldBe(0);
    }

    [Fact(Timeout = 30_000)]
    public async Task The_sitemap_of_the_default_host_lists_the_root_and_only_the_products_without_a_host()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Host();
        using var client = Client(factory);

        var (response, xml) = await GetAsync(ct, client, "/sitemap.xml", "portal.test");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Locations(xml).ShouldBe(
        [
            PortalFactory.PublicUrl + "/",
            PortalFactory.PublicUrl + "/p/who-flung-poo",
            PortalFactory.PublicUrl + "/p/who-flung-poo/kb",
            PortalFactory.PublicUrl + "/p/who-flung-poo/kb/accounts",
            PortalFactory.PublicUrl + "/p/who-flung-poo/kb/accounts/reset-password",
        ]);
        xml.ShouldNotContain("dragon");
    }

    [Fact(Timeout = 30_000)]
    public async Task Each_hosts_sitemap_is_cached_apart_and_a_hostile_host_gets_the_default_one()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Host();
        using var client = Client(factory);

        var (_, onDragon) = await GetAsync(ct, client, "/sitemap.xml", DragonHost);
        var (_, onDefault) = await GetAsync(ct, client, "/sitemap.xml", "portal.test");
        var (_, onHostile) = await GetAsync(ct, client, "/sitemap.xml", "evil.example");
        var (_, onDragonAgain) = await GetAsync(ct, client, "/sitemap.xml", DragonHost);

        onDragon.ShouldNotBe(onDefault);
        onHostile.ShouldBe(onDefault);
        onDragonAgain.ShouldBe(onDragon);
        factory.Api.Count(HttpMethod.Get, "/api/public/kb/dragon-poop/sitemap").ShouldBe(1);
        factory.Api.Count(HttpMethod.Get, "/api/public/kb/who-flung-poo/sitemap").ShouldBe(1, "the hostile host shares the default host's entry");
    }
}
