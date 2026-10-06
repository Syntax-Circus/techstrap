using System.Net;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Seo;
using TechStrap.Portal.Settings;
using TechStrap.Portal.Tests.Forms;

namespace TechStrap.Portal.Tests.Seo;

/// <summary>
/// P09-T04 and T15 at the host: the sitemap the Portal serves (PHASE-09c). Absolute addresses from the public URL, exactly the articles the API returned (it returns published ones only, pinned in the API tests), a shared
/// article under each product, XML-safe addresses, the single-flight cache, a failure that is remembered for a minute while the last good sitemap is served, a crawler that goes away without poisoning the cache, and the
/// 50,000-address cap. Review Focus 4.
/// </summary>
public sealed class SitemapHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Updated = new(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);

    private static PortalFactory Factory(string environment = "Development", Action<IServiceCollection>? configure = null, IReadOnlyDictionary<string, string?>? settings = null)
    {
        var factory = FormTestKit.Factory(environment, configure, settings, product: false);
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products", new[] { new PublicProductSummaryDto("acme", "Acme"), new PublicProductSummaryDto("orbitly", "Orbitly") });
        factory.Api.OnJson(
            HttpMethod.Get,
            "/api/public/kb/acme/sitemap",
            new[] { new KbSitemapEntryDto(null, "general", "shared-tips", Updated), new KbSitemapEntryDto("acme", "accounts", "reset-password", Updated.AddDays(-30)) });
        factory.Api.OnJson(HttpMethod.Get, "/api/public/kb/orbitly/sitemap", new[] { new KbSitemapEntryDto(null, "general", "shared-tips", Updated) });
        return factory;
    }

    private static async Task<(HttpResponseMessage Response, XDocument Xml)> GetAsync(HttpClient client)
    {
        var response = await client.GetAsync("/sitemap.xml", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response, XDocument.Parse(text));
    }

    private static string[] Locations(XDocument xml) => [.. xml.Descendants().Where(element => element.Name.LocalName == "loc").Select(element => element.Value)];

    [Fact]
    public async Task The_sitemap_lists_the_root_each_products_home_help_centre_categories_and_articles_with_absolute_addresses_and_days()
    {
        await using var factory = Factory();
        using var client = FormTestKit.Client(factory);

        var (response, xml) = await GetAsync(client);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/xml");
        response.Headers.CacheControl!.ToString().ShouldBe("public, max-age=300");
        xml.Root!.Name.NamespaceName.ShouldBe("http://www.sitemaps.org/schemas/sitemap/0.9");
        Locations(xml).ShouldBe(
        [
            PortalFactory.PublicUrl + "/",
            PortalFactory.PublicUrl + "/p/acme",
            PortalFactory.PublicUrl + "/p/acme/kb",
            PortalFactory.PublicUrl + "/p/acme/kb/accounts",
            PortalFactory.PublicUrl + "/p/acme/kb/general",
            PortalFactory.PublicUrl + "/p/acme/kb/general/shared-tips",
            PortalFactory.PublicUrl + "/p/acme/kb/accounts/reset-password",
            PortalFactory.PublicUrl + "/p/orbitly",
            PortalFactory.PublicUrl + "/p/orbitly/kb",
            PortalFactory.PublicUrl + "/p/orbitly/kb/general",
            PortalFactory.PublicUrl + "/p/orbitly/kb/general/shared-tips",
        ]);
        var days = xml.Descendants().Where(element => element.Name.LocalName == "url")
            .ToDictionary(url => url.Elements().First(e => e.Name.LocalName == "loc").Value, url => url.Elements().FirstOrDefault(e => e.Name.LocalName == "lastmod")?.Value);
        days[PortalFactory.PublicUrl + "/p/acme/kb/general/shared-tips"].ShouldBe("2026-10-05");
        days[PortalFactory.PublicUrl + "/p/acme/kb/accounts/reset-password"].ShouldBe("2026-09-05");
        days[PortalFactory.PublicUrl + "/p/acme"].ShouldBeNull();
    }

    [Fact]
    public async Task A_shared_article_is_listed_under_every_product_and_nothing_the_api_did_not_return_is_listed()
    {
        await using var factory = Factory();
        using var client = FormTestKit.Client(factory);

        var (_, xml) = await GetAsync(client);

        var locations = Locations(xml);
        locations.ShouldContain(PortalFactory.PublicUrl + "/p/acme/kb/general/shared-tips");
        locations.ShouldContain(PortalFactory.PublicUrl + "/p/orbitly/kb/general/shared-tips");
        locations.Count(location => location.EndsWith("/shared-tips", StringComparison.Ordinal)).ShouldBe(2);
        locations.ShouldAllBe(location => location.StartsWith(PortalFactory.PublicUrl + "/", StringComparison.Ordinal));
        locations.Count(location => new Uri(location).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length == 5).ShouldBe(3, "the three articles the API returned, and no other");
    }

    [Fact]
    public async Task An_address_with_characters_that_mean_something_in_xml_or_a_url_is_escaped_and_the_document_still_parses()
    {
        await using var factory = Factory();
        factory.Api.OnJson(HttpMethod.Get, "/api/public/kb/acme/sitemap", new[] { new KbSitemapEntryDto("acme", "a&b<c>", "x\"y'z&w", Updated) });
        using var client = FormTestKit.Client(factory);

        var (response, xml) = await GetAsync(client);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Locations(xml).ShouldContain(PortalFactory.PublicUrl + "/p/acme/kb/a%26b%3Cc%3E/x%22y%27z%26w");
    }

    [Fact]
    public async Task With_a_default_product_the_root_is_a_redirect_so_it_is_not_listed()
    {
        await using var factory = Factory(settings: new Dictionary<string, string?> { [PortalOptions.DefaultProductKey] = "acme" });
        using var client = FormTestKit.Client(factory);

        var (_, xml) = await GetAsync(client);

        Locations(xml).ShouldNotContain(PortalFactory.PublicUrl + "/");
        Locations(xml).ShouldContain(PortalFactory.PublicUrl + "/p/acme");
    }

    [Fact]
    public async Task Robots_txt_names_the_sitemap_and_the_sitemap_is_where_it_says()
    {
        await using var factory = Factory();
        using var client = FormTestKit.Client(factory);

        var robots = await client.GetStringAsync("/robots.txt", Ct);
        var line = robots.Split('\n', StringSplitOptions.TrimEntries).Single(l => l.StartsWith("Sitemap:", StringComparison.Ordinal));
        var path = new Uri(line["Sitemap:".Length..].Trim()).AbsolutePath;
        using var response = await client.GetAsync(path, Ct);

        line.ShouldBe($"Sitemap: {PortalFactory.PublicUrl}/sitemap.xml");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        robots.ShouldContain("Disallow: /t/");
    }

    [Fact]
    public async Task Twenty_concurrent_requests_make_one_build_and_a_later_request_makes_none()
    {
        await using var factory = Factory();
        factory.Api.On(HttpMethod.Get, "/api/public/products", _ =>
        {
            Thread.Sleep(150);
            return StubJson(new[] { new PublicProductSummaryDto("acme", "Acme"), new PublicProductSummaryDto("orbitly", "Orbitly") });
        });
        using var client = FormTestKit.Client(factory);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => client.GetAsync("/sitemap.xml", Ct)));
        using var later = await client.GetAsync("/sitemap.xml", Ct);

        responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.OK);
        factory.Api.Count(HttpMethod.Get, "/api/public/products").ShouldBe(1);
        factory.Api.Count(HttpMethod.Get, "/api/public/kb/acme/sitemap").ShouldBe(1);
        factory.Api.Count(HttpMethod.Get, "/api/public/kb/orbitly/sitemap").ShouldBe(1);
        later.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_crawler_that_goes_away_does_not_poison_the_cache_and_the_next_request_is_served_from_the_same_build()
    {
        await using var factory = Factory();
        factory.Api.On(HttpMethod.Get, "/api/public/products", _ =>
        {
            Thread.Sleep(400);
            return StubJson(new[] { new PublicProductSummaryDto("acme", "Acme"), new PublicProductSummaryDto("orbitly", "Orbitly") });
        });
        using var client = FormTestKit.Client(factory);
        using var impatient = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        impatient.CancelAfter(TimeSpan.FromMilliseconds(80));

        await Should.ThrowAsync<OperationCanceledException>(() => client.GetAsync("/sitemap.xml", impatient.Token));
        var (response, xml) = await GetAsync(client);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Locations(xml).ShouldContain(PortalFactory.PublicUrl + "/p/orbitly");
        factory.Api.Count(HttpMethod.Get, "/api/public/products").ShouldBe(1, "the build the first crawler started was not cancelled and not repeated");
    }

    [Fact]
    public async Task A_failed_build_is_a_500_and_is_not_repeated_for_a_minute()
    {
        await using var factory = Factory("Production");
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products", HttpStatusCode.InternalServerError, "boom", "A table name the visitor must never see.");
        using var client = FormTestKit.Client(factory);

        using var first = await client.GetAsync("/sitemap.xml", Ct);
        using var second = await client.GetAsync("/sitemap.xml", Ct);
        using var third = await client.GetAsync("/sitemap.xml", Ct);

        first.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        second.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        third.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await first.Content.ReadAsStringAsync(Ct)).ShouldNotContain("table name");
        factory.Api.Count(HttpMethod.Get, "/api/public/products").ShouldBe(1, "the API is not asked again while the failure is remembered");
        first.Headers.CacheControl?.Public.ShouldNotBe(true);
    }

    [Fact]
    public async Task When_a_rebuild_fails_the_last_good_sitemap_is_served_and_the_api_is_left_alone_for_the_failure_lifetime()
    {
        await using var factory = Factory(configure: services => services.AddSingleton(new PortalSitemapCache(
            new MemoryCache(new MemoryCacheOptions()), NullLogger<PortalSitemapCache>.Instance, TimeSpan.FromMilliseconds(400), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1))));
        using var client = FormTestKit.Client(factory);
        var (_, good) = await GetAsync(client);

        await Task.Delay(TimeSpan.FromMilliseconds(900), Ct);
        factory.Api.OnStatus(HttpMethod.Get, "/api/public/products", HttpStatusCode.InternalServerError);
        var (staleResponse, stale) = await GetAsync(client);
        var (againResponse, again) = await GetAsync(client);

        staleResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        againResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        Locations(stale).ShouldBe(Locations(good));
        Locations(again).ShouldBe(Locations(good));
        factory.Api.Count(HttpMethod.Get, "/api/public/products").ShouldBe(2, "one good build and one failed rebuild; nothing while the failure is remembered");
    }

    [Fact]
    public async Task At_most_fifty_thousand_addresses_are_served()
    {
        await using var factory = Factory();
        factory.Api.OnJson(
            HttpMethod.Get,
            "/api/public/kb/acme/sitemap",
            Enumerable.Range(0, PortalSitemapBuilder.MaxUrls + 25).Select(i => new KbSitemapEntryDto("acme", "accounts", $"article-{i}", Updated)).ToArray());
        using var client = FormTestKit.Client(factory);

        var (response, xml) = await GetAsync(client);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Locations(xml).Length.ShouldBe(PortalSitemapBuilder.MaxUrls, "the root page and the products' addresses together fill exactly one sitemap file");
        Locations(xml)[0].ShouldBe(PortalFactory.PublicUrl + "/", "the static entry is kept and the products' own addresses are cut");
    }

    private static HttpResponseMessage StubJson<T>(T body) => TechStrap.Portal.Tests.Api.StubApiHandler.JsonResponse(HttpStatusCode.OK, body);
}
