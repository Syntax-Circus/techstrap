using System.Net;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Tests.Forms;

namespace TechStrap.Portal.Tests.Kb;

/// <summary>
/// P09-T15 (output caching) at the host, on the real pages and the stub API: a repeated request makes one API call, a hit still carries the request's own headers, a 404 or a 429 is never stored, nothing that is not a
/// help-centre page is ever kept, and a cache key is never shared between products. <c>OutputCachePipelineTests</c> proves the mechanism on a small host; this proves it on the pages. Review Focus 3.
/// </summary>
public sealed class KbPageCacheHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static PortalFactory Factory()
    {
        var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoriesPath, new[] { new PublicKbCategoryDto("accounts", "Accounts", null, 3) });
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoryArticlesPath, KbTestKit.Page(1, 10, 1, KbTestKit.Article()));
        return factory;
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? correlationId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-Id", correlationId);
        }

        return await client.SendAsync(request, Ct);
    }

    [Theory]
    [InlineData("/p/paperplane/kb", KbTestKit.CategoriesPath)]
    [InlineData("/p/paperplane/kb/accounts", KbTestKit.CategoryArticlesPath)]
    public async Task A_help_centre_page_asked_twice_calls_the_api_once_for_the_product_and_once_for_its_data(string path, string dataPath)
    {
        await using var factory = Factory();
        using var client = FormTestKit.Client(factory);

        using var first = await GetAsync(client, path);
        using var second = await GetAsync(client, path);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Api.Count(HttpMethod.Get, KbTestKit.ProductPath).ShouldBe(1);
        factory.Api.Count(HttpMethod.Get, dataPath).ShouldBe(1);
        KbTestKit.Header(second, "Age").ShouldNotBeEmpty();
        (await second.Content.ReadAsStringAsync(Ct)).ShouldBe(await first.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_cached_page_carries_the_security_headers_the_browser_cache_header_and_the_requests_own_correlation_id()
    {
        await using var factory = Factory();
        using var client = FormTestKit.Client(factory);

        using var first = await GetAsync(client, "/p/paperplane/kb", "cid-one");
        using var second = await GetAsync(client, "/p/paperplane/kb", "cid-two");

        factory.Api.Count(HttpMethod.Get, KbTestKit.CategoriesPath).ShouldBe(1);
        KbTestKit.Header(first, "X-Correlation-Id").ShouldBe(["cid-one"]);
        KbTestKit.Header(second, "X-Correlation-Id").ShouldBe(["cid-two"]);
        foreach (var response in new[] { first, second })
        {
            KbTestKit.Header(response, "Content-Security-Policy").ShouldHaveSingleItem().ShouldContain("script-src 'self'");
            KbTestKit.Header(response, "X-Content-Type-Options").ShouldBe(["nosniff"]);
            response.Headers.CacheControl!.ToString().ShouldBe("public, max-age=60");
            KbTestKit.Header(response, "Set-Cookie").ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Only_the_page_value_changes_the_key_and_a_page_asked_with_other_query_values_is_the_same_page()
    {
        await using var factory = Factory();
        using var client = FormTestKit.Client(factory);

        using var a = await GetAsync(client, "/p/paperplane/kb/accounts");
        using var b = await GetAsync(client, "/p/paperplane/kb/accounts?utm_source=mail");
        using var c = await GetAsync(client, "/p/paperplane/kb/accounts?utm_source=other&x=1");

        factory.Api.Count(HttpMethod.Get, KbTestKit.CategoryArticlesPath).ShouldBe(1);

        factory.Api.OnJson(HttpMethod.Get, KbTestKit.CategoryArticlesPath, KbTestKit.Page(2, 10, 15, KbTestKit.Article("change-email", "Change your email")));
        using var page2 = await GetAsync(client, "/p/paperplane/kb/accounts?page=2");
        using var page2Again = await GetAsync(client, "/p/paperplane/kb/accounts?page=2&utm=1");

        factory.Api.Count(HttpMethod.Get, KbTestKit.CategoryArticlesPath).ShouldBe(2);
        (await page2.Content.ReadAsStringAsync(Ct)).ShouldContain("Change your email");
        (await page2Again.Content.ReadAsStringAsync(Ct)).ShouldContain("Change your email");
    }

    [Fact]
    public async Task A_page_value_that_is_not_a_plain_page_number_is_never_stored()
    {
        await using var factory = Factory();
        using var client = FormTestKit.Client(factory);

        using var first = await GetAsync(client, "/p/paperplane/kb/accounts?page=abc");
        using var second = await GetAsync(client, "/p/paperplane/kb/accounts?page=abc");

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Api.Count(HttpMethod.Get, KbTestKit.CategoryArticlesPath).ShouldBe(2);
    }

    [Fact]
    public async Task A_404_is_never_stored_and_carries_no_public_cache_header()
    {
        await using var factory = Factory();
        KbTestKit.Problem(factory, "/api/public/kb/paperplane/categories/gone/articles", HttpStatusCode.NotFound);
        using var client = FormTestKit.Client(factory);

        using var first = await GetAsync(client, "/p/paperplane/kb/gone");
        using var second = await GetAsync(client, "/p/paperplane/kb/gone");

        foreach (var response in new[] { first, second })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            response.Headers.CacheControl?.Public.ShouldNotBe(true);
            KbTestKit.Header(response, "Age").ShouldBeEmpty();
        }

        factory.Api.Count(HttpMethod.Get, "/api/public/kb/paperplane/categories/gone/articles").ShouldBe(2);
    }

    [Fact]
    public async Task A_429_is_never_stored()
    {
        await using var factory = Factory();
        factory.Api.OnStatus(HttpMethod.Get, KbTestKit.CategoriesPath, HttpStatusCode.TooManyRequests);
        using var client = FormTestKit.Client(factory);

        using var first = await GetAsync(client, "/p/paperplane/kb");
        using var second = await GetAsync(client, "/p/paperplane/kb");

        first.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        second.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        factory.Api.Count(HttpMethod.Get, KbTestKit.CategoriesPath).ShouldBe(2);
        first.Headers.CacheControl?.Public.ShouldNotBe(true);
    }

    [Fact]
    public async Task The_search_page_the_product_home_and_the_form_pages_are_never_stored()
    {
        await using var factory = Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.SearchPath, KbTestKit.Hits(1, 10, 1, KbTestKit.Hit()));
        using var client = FormTestKit.Client(factory);

        foreach (var path in new[] { "/p/paperplane/kb/search?q=router", "/p/paperplane", "/p/paperplane/contact", "/p/paperplane/lost-link" })
        {
            using var first = await GetAsync(client, path);
            using var second = await GetAsync(client, path);
            first.StatusCode.ShouldBe(HttpStatusCode.OK, path);
            KbTestKit.Header(second, "Age").ShouldBeEmpty(path);
            first.Headers.CacheControl?.Public.ShouldNotBe(true, path);
        }

        factory.Api.Count(HttpMethod.Get, KbTestKit.SearchPath).ShouldBe(2);
        factory.Api.Count(HttpMethod.Get, KbTestKit.ProductPath).ShouldBe(8, "each of the four paths asked the API for the product twice");
    }

    [Fact]
    public async Task A_cache_key_is_never_shared_between_products_a_themed_page_is_always_its_own_products()
    {
        await using var factory = KbTestKit.Factory();
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/acme", new PublicProductDto("acme", "Acme Anvils", null, "#F59E0B", "#000000", "#9D6507"));
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/orbitly", new PublicProductDto("orbitly", "Orbitly Orbits", null, "#7C3AED", "#FFFFFF", "#7C3AED"));
        factory.Api.OnJson(HttpMethod.Get, "/api/public/kb/acme/categories", new[] { new PublicKbCategoryDto("general", "General", null, 1) });
        factory.Api.OnJson(HttpMethod.Get, "/api/public/kb/orbitly/categories", new[] { new PublicKbCategoryDto("general", "General", null, 9) });
        using var client = FormTestKit.Client(factory);

        var acme = await (await GetAsync(client, "/p/acme/kb")).Content.ReadAsStringAsync(Ct);
        var orbitly = await (await GetAsync(client, "/p/orbitly/kb")).Content.ReadAsStringAsync(Ct);
        var acmeAgain = await (await GetAsync(client, "/p/acme/kb")).Content.ReadAsStringAsync(Ct);

        acme.ShouldContain("Acme Anvils");
        acme.ShouldNotContain("Orbitly");
        orbitly.ShouldContain("Orbitly Orbits");
        orbitly.ShouldContain("9 articles");
        orbitly.ShouldNotContain("Acme");
        acmeAgain.ShouldBe(acme);
        factory.Api.Count(HttpMethod.Get, "/api/public/kb/acme/categories").ShouldBe(1);
        factory.Api.Count(HttpMethod.Get, "/api/public/kb/orbitly/categories").ShouldBe(1);
    }

    [Fact]
    public async Task A_page_that_is_kept_never_sets_a_cookie_on_any_help_centre_page()
    {
        await using var factory = Factory();
        factory.Api.OnJson(HttpMethod.Get, KbTestKit.SearchPath, KbTestKit.Hits(1, 10, 1, KbTestKit.Hit()));
        using var client = FormTestKit.Client(factory);

        foreach (var path in new[] { "/p/paperplane/kb", "/p/paperplane/kb/accounts", "/p/paperplane/kb/search", "/p/paperplane/kb/search?q=x" })
        {
            using var response = await GetAsync(client, path);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
            KbTestKit.Header(response, "Set-Cookie").ShouldBeEmpty(path);
        }
    }
}
