using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using TechStrap.Contracts.Intake;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Tests.Forms;
using TechStrap.Portal.Tests.Tickets;

namespace TechStrap.Portal.Tests.Hosting;

/// <summary>
/// P11e-T05 through the real pipeline: the product list that names the hosts is behind the stub API, and every request names its host in the Host header. A product host serves the clean paths of its product, the
/// canonical 301s apply to reads only, a post is served and never redirected, and a host the map does not know is the default host.
/// </summary>
public sealed class ProductHostHostTests
{
    private const string DragonHost = "support.dragonpoop.com";

    private static PortalFactory Host()
    {
        var factory = new PortalFactory();
        factory.Api.OnJson(
            HttpMethod.Get,
            "/api/public/products",
            new[] { new PublicProductSummaryDto("dragon-poop", "Dragon Poop", DragonHost), new PublicProductSummaryDto("who-flung-poo", "Who Flung Poo") });
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/dragon-poop", new PublicProductDto("dragon-poop", "Dragon Poop", null, "#F59E0B", "#000000", "#9D6507", DragonHost));
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/who-flung-poo", new PublicProductDto("who-flung-poo", "Who Flung Poo", null, "#2563EB", "#FFFFFF", "#2563EB"));
        factory.Api.OnJson(HttpMethod.Get, "/api/public/kb/dragon-poop/sitemap", Array.Empty<KbSitemapEntryDto>());
        factory.Api.OnJson(HttpMethod.Get, "/api/public/kb/who-flung-poo/sitemap", Array.Empty<KbSitemapEntryDto>());
        return factory;
    }

    private static HttpClient Client(PortalFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static HttpRequestMessage Request(HttpMethod method, string path, string? host)
    {
        var request = new HttpRequestMessage(method, path);
        if (host is not null)
        {
            request.Headers.Host = host;
        }

        return request;
    }

    private static async Task<(HttpResponseMessage Response, string Body)> SendAsync(CancellationToken ct, HttpClient client, string path, string? host, HttpMethod? method = null)
    {
        using var request = Request(method ?? HttpMethod.Get, path, host);
        var response = await client.SendAsync(request, ct);
        return (response, await response.Content.ReadAsStringAsync(ct));
    }

    [Fact(Timeout = 30_000)]
    public async Task The_root_of_a_product_host_renders_its_product_home()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Host();
        using var client = Client(factory);

        var (response, body) = await SendAsync(ct, client, "/", DragonHost);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("<title>Dragon Poop Support</title>");
        factory.Api.Count(HttpMethod.Get, "/api/public/products/dragon-poop").ShouldBe(1);
    }

    [Fact(Timeout = 30_000)]
    public async Task The_contact_page_of_a_product_host_is_the_products_contact_page()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Host();
        using var client = Client(factory);

        var (response, body) = await SendAsync(ct, client, "/contact", DragonHost);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("name=\"Form.Email\"");
        body.ShouldContain("Dragon Poop");
        factory.Api.Count(HttpMethod.Get, "/api/public/products/dragon-poop").ShouldBe(1);
    }

    [Fact(Timeout = 30_000)]
    public async Task The_help_centre_of_a_product_host_is_the_products_help_centre()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Host();
        factory.Api.OnJson(HttpMethod.Get, "/api/public/kb/dragon-poop/categories", new[] { new PublicKbCategoryDto("accounts", "Accounts", null, 2) });
        using var client = Client(factory);

        var (response, body) = await SendAsync(ct, client, "/kb", DragonHost);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("Help centre");
        body.ShouldContain("Accounts");
    }

    [Fact(Timeout = 30_000)]
    public async Task A_ticket_page_is_served_on_any_host()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Host();
        factory.Api.OnJson(HttpMethod.Get, TicketTestKit.TicketApi, TicketTestKit.Ticket(productKey: "dragon-poop"));
        using var client = Client(factory);

        var (onProduct, productBody) = await SendAsync(ct, client, TicketTestKit.Path, DragonHost);
        var (onDefault, defaultBody) = await SendAsync(ct, client, TicketTestKit.Path, "portal.test");
        var (onUnknown, _) = await SendAsync(ct, client, TicketTestKit.Path, "evil.example");

        onProduct.StatusCode.ShouldBe(HttpStatusCode.OK);
        onDefault.StatusCode.ShouldBe(HttpStatusCode.OK);
        onUnknown.StatusCode.ShouldBe(HttpStatusCode.OK);
        productBody.ShouldContain("<span class=\"ts-ticket-number\">PAP-42</span>");
        defaultBody.ShouldContain("<span class=\"ts-ticket-number\">PAP-42</span>");
    }

    [Theory(Timeout = 30_000)]
    [InlineData("/robots.txt")]
    [InlineData("/health/live")]
    [InlineData("/sitemap.xml")]
    [InlineData("/css/app.css")]
    [InlineData("/favicon.ico")]
    public async Task The_seo_files_the_health_checks_and_the_static_assets_answer_on_a_product_host(string path)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Host();
        using var client = Client(factory);

        var (response, _) = await SendAsync(ct, client, path, DragonHost);
        var (reference, _) = await SendAsync(ct, client, path, "portal.test");

        reference.StatusCode.ShouldBe(HttpStatusCode.OK, "the same path answers on the default host");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact(Timeout = 30_000)]
    public async Task The_blazor_hub_connects_on_a_product_host()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Host();
        using var client = Client(factory);

        var (scriptOnProduct, _) = await SendAsync(ct, client, "/_framework/blazor.web.js", DragonHost);
        var (onProduct, _) = await SendAsync(ct, client, "/_blazor/negotiate?negotiateVersion=1", DragonHost, HttpMethod.Post);
        var (onDefault, _) = await SendAsync(ct, client, "/_blazor/negotiate?negotiateVersion=1", "portal.test", HttpMethod.Post);

        // The Portal renders statically (no interactive circuit), so the hub path answers the same on every host; what matters is that the product host neither rewrites nor redirects it, and serves the framework script.
        scriptOnProduct.StatusCode.ShouldBe(HttpStatusCode.OK);
        onProduct.StatusCode.ShouldBe(onDefault.StatusCode, "the hub path is not rewritten or redirected");
        onProduct.StatusCode.ShouldNotBe(HttpStatusCode.MovedPermanently);
    }
    [Fact(Timeout = 30_000)]
    public async Task A_post_on_a_product_host_is_rewritten_not_redirected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Host();
        factory.Api.OnJson(HttpMethod.Post, "/api/public/products/dragon-poop/tickets", new SubmitTicketResponse("DRP-1", null, []), HttpStatusCode.Created);
        using var client = Client(factory);
        var (_, page) = await SendAsync(ct, client, "/contact", DragonHost);
        var token = FormTestKit.TokenFrom(page);

        using var request = Request(HttpMethod.Post, "/contact", DragonHost);
        request.Content = FormTestKit.ContactForm(token);
        using var response = await client.SendAsync(request, ct);

        response.StatusCode.ShouldNotBe(HttpStatusCode.MovedPermanently);
        response.StatusCode.ShouldBe(HttpStatusCode.Found, "the form's own post, redirect, get");
        factory.Api.Count(HttpMethod.Post, "/api/public/products/dragon-poop/tickets").ShouldBe(1);

        // A post to the product's own prefix on its host is served too: a 301 would drop the body.
        using var prefixed = Request(HttpMethod.Post, "/p/dragon-poop/contact", DragonHost);
        prefixed.Content = FormTestKit.ContactForm(token);
        using var prefixedResponse = await client.SendAsync(prefixed, ct);

        prefixedResponse.StatusCode.ShouldBe(HttpStatusCode.Found);
        factory.Api.Count(HttpMethod.Post, "/api/public/products/dragon-poop/tickets").ShouldBe(2);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_products_own_prefix_on_its_host_is_a_301_to_the_clean_path_and_a_post_to_it_is_not()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Host();
        using var client = Client(factory);

        var (get, _) = await SendAsync(ct, client, "/p/dragon-poop/contact?x=1", DragonHost);
        var (viaDefault, _) = await SendAsync(ct, client, "/p/dragon-poop/kb", "portal.test");

        get.StatusCode.ShouldBe(HttpStatusCode.MovedPermanently);
        get.Headers.Location!.ToString().ShouldBe("https://support.dragonpoop.com/contact?x=1");
        viaDefault.StatusCode.ShouldBe(HttpStatusCode.MovedPermanently);
        viaDefault.Headers.Location!.ToString().ShouldBe("https://support.dragonpoop.com/kb");
    }

    [Fact(Timeout = 30_000)]
    public async Task A_product_without_a_host_is_served_under_its_prefix_on_the_default_host()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Host();
        using var client = Client(factory);

        var (response, body) = await SendAsync(ct, client, "/p/who-flung-poo", "portal.test");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("Who Flung Poo");
    }

    [Theory(Timeout = 30_000)]
    [InlineData("evil.example")]
    [InlineData("support.dragonpoop.com.evil")]
    [InlineData("[::1]:8082")]
    public async Task A_hostile_host_leaks_nothing(string host)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Host();
        using var client = Client(factory);
        var (reference, referenceBody) = await SendAsync(ct, client, "/", "portal.test");

        var (response, body) = await SendAsync(ct, client, "/", host);

        response.StatusCode.ShouldBe(reference.StatusCode);
        body.ShouldBe(referenceBody, "an unknown host renders the default host's home");
        var markers = new[] { "evil", "::1", "8082" };
        var headers = string.Join('\n', response.Headers.Concat(response.Content.Headers).Select(header => $"{header.Key}: {string.Join(',', header.Value)}"));
        foreach (var marker in markers)
        {
            body.ShouldNotContain(marker);
            headers.ShouldNotContain(marker);
        }

        response.Headers.Location.ShouldBeNull();
    }

    [Fact(Timeout = 30_000)]
    public async Task A_hostile_host_on_a_product_path_redirects_only_to_the_stored_host()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Host();
        using var client = Client(factory);

        var (response, body) = await SendAsync(ct, client, "/p/dragon-poop/contact", "evil.example");

        response.StatusCode.ShouldBe(HttpStatusCode.MovedPermanently);
        response.Headers.Location!.ToString().ShouldBe("https://support.dragonpoop.com/contact");
        body.ShouldNotContain("evil");
    }
}
