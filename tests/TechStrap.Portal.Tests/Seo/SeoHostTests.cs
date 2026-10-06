using System.Net;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SyntaxCircus.Blazor.Seo;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Settings;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Seo;

/// <summary>
/// P09-T04 (09a part): <c>SyntaxCircus.Blazor.Seo</c> with its real API. <c>Seo:BaseUrl</c> is derived from <c>TECHSTRAP_PORTAL_PUBLIC_URL</c> (one setting for one value), robots.txt disallows the ticket
/// pages, and the canonical-host redirect is an allow-list that does nothing while it is not configured. PHASE-09c maps the sitemap (its content is pinned in <c>SitemapHostTests</c>).
/// </summary>
public sealed class SeoHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Robots_txt_disallows_the_ticket_pages_and_names_the_sitemap_under_the_public_address()
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/robots.txt", Ct);
        var lines = (await response.Content.ReadAsStringAsync(Ct)).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
        lines.ShouldBe(["User-agent: *", "Allow: /", "Disallow: /t/", $"Sitemap: {PortalFactory.PublicUrl}/sitemap.xml"]);
    }

    [Fact]
    public async Task Robots_txt_is_served_in_Production_too_and_in_Development_without_a_public_url()
    {
        await using var production = new PortalFactory("Production");
        await using var development = new PortalFactory("Development", new Dictionary<string, string?> { [PortalOptions.PublicUrlKey] = "" });
        using var productionClient = production.CreateClient();
        using var developmentClient = development.CreateClient();

        var prod = await productionClient.GetStringAsync("/robots.txt", Ct);
        var dev = await developmentClient.GetStringAsync("/robots.txt", Ct);

        prod.ShouldContain("Disallow: /t/");
        dev.ShouldContain("Disallow: /t/");
    }

    [Fact]
    public async Task The_sitemap_is_served_as_xml_and_with_no_product_it_lists_the_root_page_only()
    {
        await using var factory = new PortalFactory();
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products", Array.Empty<PublicProductSummaryDto>());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/sitemap.xml", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, "robots.txt names the sitemap, so it must be there (09a left it unmapped; PHASE-09c maps it)");
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/xml");
        var locations = XDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).Descendants().Where(element => element.Name.LocalName == "loc").Select(element => element.Value).ToList();
        locations.ShouldBe([PortalFactory.PublicUrl + "/"]);
    }

    [Fact]
    public async Task The_sitemap_build_calls_the_api_with_the_address_of_the_visitor_whose_request_started_it_and_lists_what_it_found()
    {
        await using var factory = TechStrap.Portal.Tests.Forms.FormTestKit.Factory(product: false);
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products", new[] { new PublicProductSummaryDto("paperplane", "Paperplane") });
        factory.Api.OnJson(HttpMethod.Get, "/api/public/kb/paperplane/sitemap", new[] { new TechStrap.Contracts.Kb.KbSitemapEntryDto(null, "general", "shared-tips", new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero)) });
        using var client = TechStrap.Portal.Tests.Forms.FormTestKit.Client(factory);

        using var response = await client.GetAsync("/sitemap.xml", Ct);
        var locations = XDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).Descendants().Where(element => element.Name.LocalName == "loc").Select(element => element.Value).ToList();

        locations.ShouldBe(
        [
            PortalFactory.PublicUrl + "/",
            PortalFactory.PublicUrl + "/p/paperplane",
            PortalFactory.PublicUrl + "/p/paperplane/kb",
            PortalFactory.PublicUrl + "/p/paperplane/kb/general",
            PortalFactory.PublicUrl + "/p/paperplane/kb/general/shared-tips",
        ]);
        factory.Api.Requests.Select(request => request.Path).ShouldBe(["/api/public/products", "/api/public/kb/paperplane/sitemap"]);
        factory.Api.AssertEveryCallBore(TechStrap.Portal.Tests.Forms.FormTestKit.Visitor);
    }

    [Theory]
    [InlineData("https://support.example.com", "https://support.example.com")]
    [InlineData("https://support.example.com/", "https://support.example.com")]
    [InlineData("http://localhost:8082", "http://localhost:8082")]
    public async Task The_seo_base_url_is_the_public_url_without_a_trailing_slash(string publicUrl, string expected)
    {
        await using var factory = new PortalFactory(settings: new Dictionary<string, string?> { [PortalOptions.PublicUrlKey] = publicUrl });
        using var client = factory.CreateClient();

        factory.Services.GetRequiredService<IOptions<SeoOptions>>().Value.BaseUrl.ShouldBe(expected);
    }

    [Fact]
    public async Task A_seo_base_url_set_by_hand_never_wins_over_the_public_url()
    {
        await using var factory = new PortalFactory(settings: new Dictionary<string, string?> { ["Seo:BaseUrl"] = "https://elsewhere.example.com" });
        using var client = factory.CreateClient();

        factory.Services.GetRequiredService<IOptions<SeoOptions>>().Value.BaseUrl.ShouldBe(PortalFactory.PublicUrl);
        (await client.GetStringAsync("/robots.txt", Ct)).ShouldContain($"Sitemap: {PortalFactory.PublicUrl}/sitemap.xml");
    }

    private static PortalFactory Canonical(bool forceHttps = false, string? canonical = "portal.example.com", string environment = "Development") => new(
        environment,
        new Dictionary<string, string?>
        {
            ["CanonicalHost:CanonicalHost"] = canonical,
            ["CanonicalHost:LegacyHosts:0"] = "old.example.com",
            ["CanonicalHost:LegacyHosts:1"] = "www.old.example.com",
            ["CanonicalHost:ForceHttps"] = forceHttps ? "true" : "false",
        });

    private static async Task<HttpResponseMessage> GetAsync(PortalFactory factory, string host, string path)
    {
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = host;
        return await client.SendAsync(request, Ct);
    }

    [Theory]
    [InlineData("old.example.com")]
    [InlineData("WWW.OLD.EXAMPLE.COM")]
    public async Task A_legacy_host_is_redirected_permanently_to_the_canonical_host_with_its_path_and_query(string host)
    {
        await using var factory = Canonical();

        using var response = await GetAsync(factory, host, "/p/paperplane/contact?subject=Hi&name=Jo");

        response.StatusCode.ShouldBe(HttpStatusCode.MovedPermanently);
        response.Headers.Location!.ToString().ShouldBe("http://portal.example.com/p/paperplane/contact?subject=Hi&name=Jo");
    }

    [Fact]
    public async Task Force_https_redirects_to_https()
    {
        await using var factory = Canonical(forceHttps: true);

        using var response = await GetAsync(factory, "old.example.com", "/p/paperplane");

        response.Headers.Location!.ToString().ShouldBe("https://portal.example.com/p/paperplane");
    }

    [Theory]
    [InlineData("portal.example.com")]
    [InlineData("localhost")]
    [InlineData("anything.else.example")]
    public async Task A_host_that_is_not_a_legacy_host_is_never_redirected_because_the_list_is_an_allow_list(string host)
    {
        await using var factory = Canonical();

        using var response = await GetAsync(factory, host, "/robots.txt");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Location.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task With_no_canonical_host_configured_nothing_is_redirected_even_when_legacy_hosts_are_listed(string? canonical)
    {
        await using var factory = Canonical(canonical: canonical);

        using var response = await GetAsync(factory, "old.example.com", "/robots.txt");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Location.ShouldBeNull();
    }

    [Fact]
    public async Task Nothing_is_configured_by_default_so_no_host_is_redirected_in_Development()
    {
        await using var factory = new PortalFactory();

        using var response = await GetAsync(factory, "old.example.com", "/robots.txt");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_redirect_target_is_the_configured_host_never_the_requested_one()
    {
        await using var factory = Canonical();

        using var response = await GetAsync(factory, "old.example.com", "//evil.example/path");

        response.Headers.Location!.Host.ShouldBe("portal.example.com");
    }
}
