using Microsoft.AspNetCore.Mvc.Testing;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;

namespace TechStrap.Portal.Tests.Headers;

/// <summary>
/// Review F1 (PHASE-11e): the header rules run when a request arrives, before a product host rewrites <c>/contact</c> to <c>/p/{key}/contact</c>, so they must know the clean paths. These tests send the Host header of a
/// product and assert the final response headers.
/// </summary>
public sealed class ProductHostHeaderTests
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
        factory.Api.OnJson(HttpMethod.Get, "/api/public/kb/dragon-poop/categories", new[] { new PublicKbCategoryDto("accounts", "Accounts", null, 2) });
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/who-flung-poo", new PublicProductDto("who-flung-poo", "Who Flung Poo", null, "#2563EB", "#FFFFFF", "#2563EB"));
        factory.Api.OnJson(HttpMethod.Get, "/api/public/kb/who-flung-poo/categories", new[] { new PublicKbCategoryDto("accounts", "Accounts", null, 2) });
        return factory;
    }

    private static async Task<HttpResponseMessage> GetAsync(CancellationToken ct, PortalFactory factory, string path, string host)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = host;
        return await client.SendAsync(request, ct);
    }

    private static string[] Header(HttpResponseMessage response, string name) => response.Headers.TryGetValues(name, out var values) ? [.. values] : [];

    [Theory(Timeout = 30_000)]
    [InlineData("/contact?subject=x&name=Jane&email=j%40example.com")]
    [InlineData("/lost-link")]
    [InlineData("/suggest?q=printer")]
    public async Task A_form_page_on_a_product_host_is_noindex_and_no_store(string path)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Host();

        using var response = await GetAsync(ct, factory, path, DragonHost);

        Header(response, "Cache-Control").ShouldBe(["no-store"], path);
        Header(response, "X-Robots-Tag").ShouldBe(["noindex"], path);
    }

    [Fact(Timeout = 30_000)]
    public async Task The_search_page_on_a_product_host_is_never_stored()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Host();

        using var response = await GetAsync(ct, factory, "/kb/search?q=x", DragonHost);

        Header(response, "Cache-Control").ShouldBe(["no-store"]);
    }

    [Fact(Timeout = 30_000)]
    public async Task The_help_centre_on_a_product_host_carries_the_same_public_header_as_on_the_default_host()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Host();

        using var onDefault = await GetAsync(ct, factory, "/p/who-flung-poo/kb", "portal.test");
        using var onProduct = await GetAsync(ct, factory, "/kb", DragonHost);

        onDefault.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        onProduct.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        Header(onDefault, "Cache-Control").ShouldNotBeEmpty();
        Header(onProduct, "Cache-Control").ShouldBe(Header(onDefault, "Cache-Control"));
    }
}
