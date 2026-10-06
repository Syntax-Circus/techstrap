using System.Net;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Products;

/// <summary>P09-T03 and T05 at the host, with a fake API behind the Portal: the themed product home, the logo rule and the accent that only the derivation rule can produce.</summary>
public sealed class ProductHomeHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static PublicProductDto Product(string name = "Paperplane", string? logo = "https://cdn.example.com/paperplane.png", string accent = "#F59E0B", string onAccent = "#000000", string ink = "#9D6507") =>
        new("paperplane", name, logo, accent, onAccent, ink);

    private static async Task<(HttpResponseMessage Response, string Html)> GetAsync(PortalFactory factory, string path)
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync(path, Ct);
        return (response, await response.Content.ReadAsStringAsync(Ct));
    }

    private static PortalFactory WithProduct(PublicProductDto product, string environment = "Development", Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? configure = null)
    {
        var factory = new PortalFactory(environment, configureServices: configure);
        factory.Api.OnJson(HttpMethod.Get, $"/api/public/products/{product.Key}", product);
        return factory;
    }

    [Fact]
    public async Task A_known_product_gets_a_themed_home_with_its_name_logo_search_and_contact_link()
    {
        await using var factory = WithProduct(Product());

        var (response, html) = await GetAsync(factory, "/p/paperplane");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<title>Paperplane Support</title>");
        html.ShouldContain("--ts-accent:#F59E0B;--ts-on-accent:#000000;--ts-accent-ink:#9D6507");
        html.ShouldContain("class=\"ts-product-name\"");
        html.ShouldContain(">Paperplane</a>");
        html.ShouldContain("<img class=\"ts-product-logo\" src=\"https://cdn.example.com/paperplane.png\"");
        html.ShouldContain("<h1>How can we help?</h1>");
        html.ShouldContain("<form method=\"get\" action=\"/p/paperplane/kb/search\"");
        html.ShouldContain("name=\"q\"");
        html.ShouldContain("maxlength=\"200\"");
        html.ShouldContain("<a class=\"btn btn-primary\" href=\"/p/paperplane/contact\">Contact support</a>");
        html.ShouldContain("href=\"/p/paperplane/lost-link\"");
        html.ShouldNotContain("TechStrap Portal");
        factory.Api.Count(HttpMethod.Get, "/api/public/products/paperplane").ShouldBe(1);
    }

    [Fact]
    public async Task The_search_box_is_labelled_and_posts_nothing_it_is_a_get_form_with_no_script()
    {
        await using var factory = WithProduct(Product());

        var (_, html) = await GetAsync(factory, "/p/paperplane");

        html.ShouldContain("<label for=\"kb-search\"");
        html.ShouldContain("id=\"kb-search\"");
        html.ShouldContain("type=\"search\"");
        html.ShouldContain("role=\"search\"");
        html.ShouldNotContain("<form method=\"post\"");
        html.ShouldNotContain("<script>");
    }

    [Fact]
    public async Task The_product_name_is_encoded_wherever_it_appears()
    {
        await using var factory = WithProduct(Product(name: "<img src=x onerror=alert(1)>Paper & \"plane\""));

        var (_, html) = await GetAsync(factory, "/p/paperplane");

        html.ShouldNotContain("<img src=x");
        html.ShouldNotContain("onerror=alert(1)>Paper");
        html.ShouldContain("&lt;img src=x onerror=alert(1)&gt;Paper &amp; &quot;plane&quot;");
    }

    [Theory]
    [InlineData("http://cdn.example.com/logo.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=")]
    [InlineData("//cdn.example.com/logo.png")]
    [InlineData("https://user:pw@cdn.example.com/logo.png")]
    [InlineData("http://localhost:5000/logo.png")]
    public async Task A_logo_that_is_not_https_is_not_rendered_in_Production(string logo)
    {
        await using var factory = WithProduct(Product(logo: logo), "Production");

        var (response, html) = await GetAsync(factory, "/p/paperplane");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldNotContain("ts-product-logo");
        html.ShouldNotContain(logo.Replace("&", "&amp;"));
        html.ShouldContain(">Paperplane</a>", Case.Sensitive, "the name still shows");
    }

    [Fact]
    public async Task A_loopback_http_logo_is_rendered_in_Development_only()
    {
        await using var development = WithProduct(Product(logo: "http://localhost:5000/logo.png"), "Development");
        await using var production = WithProduct(Product(logo: "http://localhost:5000/logo.png"), "Production");

        var (_, devHtml) = await GetAsync(development, "/p/paperplane");
        var (_, prodHtml) = await GetAsync(production, "/p/paperplane");

        devHtml.ShouldContain("src=\"http://localhost:5000/logo.png\"");
        prodHtml.ShouldNotContain("localhost:5000");
    }

    [Fact]
    public async Task A_product_without_a_logo_still_shows_its_name()
    {
        await using var factory = WithProduct(Product(logo: null));

        var (_, html) = await GetAsync(factory, "/p/paperplane");

        html.ShouldNotContain("ts-product-logo");
        html.ShouldContain(">Paperplane</a>");
    }

    [Theory]
    [InlineData("#F59E0B;background:url(//evil.example/x)")]
    [InlineData("red")]
    [InlineData("")]
    public async Task An_accent_that_is_not_a_hex_colour_sets_no_style_and_nothing_reaches_the_page(string accent)
    {
        await using var factory = WithProduct(Product(accent: accent));

        var (response, html) = await GetAsync(factory, "/p/paperplane");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldNotContain("--ts-accent");
        html.ShouldNotContain("evil.example");
        html.ShouldNotContain("style=");
    }

    [Fact]
    public async Task The_colours_come_only_from_the_derivation_rule_not_from_the_dtos_own_derived_fields()
    {
        await using var factory = WithProduct(Product(accent: "#4B7D87", onAccent: "red;x:y", ink: "url(//evil.example/x)"));

        var (_, html) = await GetAsync(factory, "/p/paperplane");

        html.ShouldContain("--ts-accent:#4B7D87;--ts-on-accent:#FFFFFF;--ts-accent-ink:#4B7D87");
        html.ShouldNotContain("evil.example");
        html.ShouldNotContain("red;x:y");
    }

    [Fact]
    public async Task The_visitors_address_behind_a_trusted_proxy_reaches_the_api_from_a_real_page()
    {
        await using var factory = WithProduct(Product(), configure: ProxyHopStartupFilter.Add("192.0.2.10"));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/p/paperplane");
        request.Headers.Add("X-Forwarded-For", "203.0.113.9");

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Api.AssertEveryCallBore("203.0.113.9");
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError, HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests)]
    public async Task When_the_api_fails_the_page_says_so_calmly_with_a_retry_link_and_no_brand_and_no_internals(HttpStatusCode apiStatus, HttpStatusCode pageStatus)
    {
        await using var factory = new PortalFactory("Production");
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/paperplane", apiStatus, "internal-error", "System.InvalidOperationException at Npgsql host=10.0.0.5 stack trace");

        var (response, html) = await GetAsync(factory, "/p/paperplane");

        response.StatusCode.ShouldBe(pageStatus);
        html.ShouldContain("This page could not be loaded.");
        html.ShouldContain("href=\"/p/paperplane\">Try again</a>");
        html.ShouldNotContain("Npgsql");
        html.ShouldNotContain("10.0.0.5");
        html.ShouldNotContain("InvalidOperationException");
        html.ShouldNotContain("--ts-accent");
        html.ShouldNotContain("ts-product-header");
        html.ShouldContain("ts-powered");
    }

    [Fact]
    public async Task A_400_from_the_api_shows_the_fixed_copy_and_never_the_apis_own_text()
    {
        await using var factory = new PortalFactory("Production");
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/paperplane", HttpStatusCode.BadRequest, "validation-failed", "Column secret_notes of table products is invalid");

        var (response, html) = await GetAsync(factory, "/p/paperplane");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        html.ShouldContain(ProblemCopy.ApiUnavailable);
        html.ShouldNotContain("secret_notes");
    }

    [Fact]
    public async Task A_transport_failure_is_the_same_calm_page()
    {
        await using var factory = new PortalFactory("Production");
        factory.Api.On(HttpMethod.Get, "/api/public/products/paperplane", _ => throw new HttpRequestException("No connection could be made (10.1.2.3:5432)"));

        var (response, html) = await GetAsync(factory, "/p/paperplane");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        html.ShouldContain("This page could not be loaded.");
        html.ShouldNotContain("10.1.2.3");
        factory.Api.Count(HttpMethod.Get, "/api/public/products/paperplane").ShouldBe(1 + 2, "a read is retried twice on a transport failure");
    }

    [Fact]
    public async Task A_page_for_a_product_is_not_cached_by_the_browser_forever_and_carries_the_shared_headers()
    {
        await using var factory = WithProduct(Product());

        var (response, _) = await GetAsync(factory, "/p/paperplane");

        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("strict-origin-when-cross-origin");
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("img-src 'self' https: data:");
    }
}
