using System.Net;
using System.Text.RegularExpressions;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Settings;
using TechStrap.Contracts.Skins;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Portal.Products;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Tests.Skins;

/// <summary>D-053 at the host: a resolved skin reaches the page only as validated custom properties and preset attributes on the one style carrier, and a page with no skin is byte for byte what it was.</summary>
public sealed class SkinRenderingHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static PublicProductDto Paperplane(ProductSkin? skin = null) =>
        new("paperplane", "Paperplane", "https://cdn.example.com/paperplane.png", "#F59E0B", "#000000", "#9D6507", Skin: skin);

    private static PublicProductDto Orbitly() => new("orbitly", "Orbitly", null, "#7C3AED", "#FFFFFF", "#7C3AED");

    // The asset fingerprints (css/app.<hash>.css, blazor.web.<hash>.js, ...) change with every build: the golden holds them without the hash.
    private static string Normalise(string html) =>
        Regex.Replace(html.Replace("\r\n", "\n", StringComparison.Ordinal), @"\.[a-z0-9]{10}\.(css|js)\b", ".$1");

    // The provider's first read has a real two-second bound; a test that needs the pack on its very first request reads it before the request, so a loaded machine cannot turn it into Classic.
    private static async Task WarmAsync(PortalFactory factory) =>
        await factory.Services.GetRequiredService<DefaultPackProvider>().GetAsync(Ct);

    private static Dictionary<string, string?> Products() => new() { [PortalOptions.LandingKey] = "Products" };

    [Fact]
    public async Task An_unskinned_product_page_is_byte_identical()
    {
        await using var factory = new PortalFactory();
        factory.Api.OnJson(HttpMethod.Get, "/api/public/site", new PublicSiteDto("classic"));
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", Paperplane());
        using var client = factory.CreateClient();

        var html = Normalise(await client.GetStringAsync("/p/paperplane", Ct));

        html.ShouldBe(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "unskinned-product-home.html")).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_default_pack_themes_the_product_page_and_the_root()
    {
        await using var factory = new PortalFactory();
        factory.Api.OnJson(HttpMethod.Get, "/api/public/site", new PublicSiteDto("midnight"));
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", Paperplane());
        await WarmAsync(factory);
        using var client = factory.CreateClient();

        var product = await client.GetStringAsync("/p/paperplane", Ct);
        var root = await client.GetStringAsync("/", Ct);

        foreach (var html in new[] { product, root })
        {
            html.ShouldContain("--p-bg:#0F1420");
            html.ShouldContain("--ts-focus:#FFD166");
            html.ShouldContain("data-ts-shadow=\"soft\"");
            html.ShouldContain("data-ts-header=\"solid\"");
        }

        product.ShouldContain("--ts-accent:#F59E0B");
        root.ShouldContain("--ts-accent:#6EA8FF", Case.Sensitive, "a neutral page carries the pack's own brand");
    }

    [Fact]
    public async Task A_product_override_changes_only_that_product()
    {
        await using var factory = new PortalFactory();
        factory.Api.OnJson(HttpMethod.Get, "/api/public/site", new PublicSiteDto("classic"));
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", Paperplane(new ProductSkin(Pack: "paper", Radius: "square", BorderWidth: 4)));
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/orbitly", Orbitly());
        using var client = factory.CreateClient();

        var paperplane = await client.GetStringAsync("/p/paperplane", Ct);
        var orbitly = await client.GetStringAsync("/p/orbitly", Ct);

        paperplane.ShouldContain("--p-bg:#FBF7EF");
        paperplane.ShouldContain("--ts-radius:0");
        paperplane.ShouldContain("--ts-border-w:4px");
        orbitly.ShouldNotContain("--p-bg:");
        orbitly.ShouldNotContain("--ts-radius");
    }

    [Fact]
    public async Task Hostile_skin_values_never_reach_the_page()
    {
        await using var factory = new PortalFactory();
        factory.Api.OnJson(HttpMethod.Get, "/api/public/site", new PublicSiteDto("classic"));
        factory.Api.OnJson(
            HttpMethod.Get,
            "/api/public/products/paperplane",
            Paperplane(new ProductSkin(Background: "red;}body{display:none", Ink: "url(https://evil.test/x)", HeadingFont: "x';}", Radius: "50%", Shadow: "0 0 9px red", Button: "\"><script>", Header: "javascript:1")));
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/p/paperplane", Ct);

        html.ShouldNotContain("evil.test");
        html.ShouldNotContain("display:none");
        html.ShouldNotContain("<script>alert");
        var styles = Regex.Matches(html, "style=\"([^\"]*)\"").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();
        styles.ShouldAllBe(s => Regex.IsMatch(s, @"^(--[a-z0-9-]+:(#[0-9A-F]{6}|[0-9.]+(rem|px)|0|'[^';]+'(, [^;']+|, '[^';]+')*);?)+$"));
    }

    [Fact]
    public async Task Neutral_pages_use_only_the_default_pack_and_are_identical_across_hosts()
    {
        await using var factory = new PortalFactory();
        factory.Api.OnJson(HttpMethod.Get, "/api/public/site", new PublicSiteDto("slate"));
        await WarmAsync(factory);
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Host = "portal.test";
        var a = await client.GetStringAsync("/not-found", Ct);
        client.DefaultRequestHeaders.Host = "stranger.example.net";
        var b = await client.GetStringAsync("/not-found", Ct);

        a.ShouldBe(b);
        a.ShouldContain("--p-bg:#F8FAFC");
    }

    [Fact]
    public async Task A_failed_site_setting_falls_back_to_classic_without_an_error_page()
    {
        await using var factory = new PortalFactory();
        factory.Api.OnStatus(HttpMethod.Get, "/api/public/site", HttpStatusCode.ServiceUnavailable);
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", Paperplane());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/p/paperplane", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("--p-bg:");
    }

    [Fact]
    public async Task A_neutral_root_asks_for_the_site_setting_once_and_for_no_product_at_all()
    {
        await using var factory = new PortalFactory();
        factory.Api.OnJson(HttpMethod.Get, "/api/public/site", new PublicSiteDto("slate"));
        using var client = factory.CreateClient();

        await client.GetStringAsync("/", Ct);
        await client.GetStringAsync("/", Ct);

        factory.Api.SiteSettingRequests.ShouldHaveSingleItem("the second page is served from the one-minute snapshot");
        factory.Api.Requests.ShouldBeEmpty("no product, ticket or help-centre call");
    }

    [Fact]
    public async Task Each_landing_card_is_scoped_to_its_own_brand()
    {
        await using var factory = new PortalFactory(settings: Products());
        factory.Api.OnJson(HttpMethod.Get, "/api/public/site", new PublicSiteDto("classic"));
        factory.Api.OnJson(
            HttpMethod.Get,
            "/api/public/products",
            new[]
            {
                new PublicProductSummaryDto("acme", "Acme Corp", AccentColour: "#7C3AED"),
                new PublicProductSummaryDto("paperplane", "Paperplane", AccentColour: "#F59E0B", Skin: new ProductSkin(Shadow: "hard")),
            });
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/", Ct);

        Regex.IsMatch(html, "<div class=\"ts-accent-scope\" style=\"--ts-accent:#7C3AED[^\"]*\">\\s*<a class=\"ts-landing-card\" href=\"/p/acme\"").ShouldBeTrue(html);
        Regex.IsMatch(html, "<div class=\"ts-accent-scope\" style=\"--ts-accent:#F59E0B[^\"]*\" data-ts-shadow=\"hard\">\\s*<a class=\"ts-landing-card\" href=\"/p/paperplane\"").ShouldBeTrue(html);
    }
}
