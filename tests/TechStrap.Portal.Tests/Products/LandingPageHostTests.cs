using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Settings;
using TechStrap.Tests.Shared;

namespace TechStrap.Portal.Tests.Products;

/// <summary>D-052: with TECHSTRAP_PORTAL_LANDING=Products the default host's root lists the listed products as cards; a product host, an unlisted product, an empty list and a failed list never show one.</summary>
public sealed class LandingPageHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static Dictionary<string, string?> Products(string? defaultProduct = null) => new()
    {
        [PortalOptions.LandingKey] = "Products",
        [PortalOptions.DefaultProductKey] = defaultProduct,
    };

    private static PublicProductSummaryDto[] List() =>
    [
        new("acme", "Acme Corp", null, "Tickets for Acme.", "https://cdn.acme.test/logo.png", "#7C3AED"),
        new("hidden", "Hidden One", null, "Never shown", null, "#000000", ListedOnLanding: false),
        new("paperplane", "Paperplane", "support.paperplane.test", null, "http://insecure.test/logo.png", "#F59E0B"),
    ];

    [Fact]
    public async Task The_root_lists_the_listed_products_as_cards_in_api_order_with_safe_logos_only()
    {
        await using var factory = new PortalFactory(settings: Products());
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products", List());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/", Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<h1>Support</h1>");
        html.ShouldContain("Choose your product");
        html.ShouldContain("class=\"ts-landing-card\" href=\"/p/acme\"");
        html.ShouldContain("Acme Corp");
        html.ShouldContain("Tickets for Acme.");
        html.ShouldContain("src=\"https://cdn.acme.test/logo.png\"");
        html.ShouldContain("class=\"ts-landing-card\" href=\"https://support.paperplane.test/\"");
        html.ShouldNotContain("insecure.test");
        html.ShouldNotContain("Hidden One");
        html.ShouldNotContain("/p/hidden");
        html.IndexOf("Acme Corp", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("Paperplane", StringComparison.Ordinal));
        html.ShouldContain("rel=\"canonical\" href=\"https://portal.test/\"");
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        factory.Api.Count(HttpMethod.Get, "/api/public/products").ShouldBe(1);
    }

    [Fact]
    public async Task A_second_request_is_served_from_the_output_cache_with_a_public_minute_and_a_query_string_is_not_cached()
    {
        await using var factory = new PortalFactory(settings: Products());
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products", List());
        using var client = factory.CreateClient();

        using var first = await client.GetAsync("/", Ct);
        using var second = await client.GetAsync("/", Ct);
        using var query = await client.GetAsync("/?utm=1", Ct);
        using var query2 = await client.GetAsync("/?utm=2", Ct);

        first.Headers.CacheControl!.ToString().ShouldBe("public, max-age=60");
        second.Headers.Age.ShouldNotBeNull("the second request is a cache hit");
        factory.Api.Count(HttpMethod.Get, "/api/public/products").ShouldBe(3, "one build for the two plain requests, one per query variant");
    }

    [Theory]
    [InlineData(200)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task An_empty_list_or_a_failed_list_renders_the_neutral_copy_with_200(int status)
    {
        await using var factory = new PortalFactory(settings: Products());
        if (status == 200)
        {
            factory.Api.OnJson(HttpMethod.Get, "/api/public/products", Array.Empty<PublicProductSummaryDto>());
        }
        else
        {
            factory.Api.OnStatus(HttpMethod.Get, "/api/public/products", (HttpStatusCode)status);
        }

        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/", Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("the link in your email");
        html.ShouldNotContain("ts-landing-card");
    }

    [Fact]
    public async Task A_product_host_root_is_its_product_home_never_the_landing()
    {
        await using var factory = new PortalFactory(settings: Products());
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products", List());
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", new PublicProductDto("paperplane", "Paperplane", null, "#F59E0B", "#000000", "#9D6507", "support.paperplane.test"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = "support.paperplane.test";

        var html = await client.GetStringAsync("/", Ct);

        html.ShouldContain("How can we help?");
        html.ShouldNotContain("ts-landing-card");
        html.ShouldNotContain("Acme Corp");
    }

    [Fact]
    public async Task Neutral_mode_is_the_page_it_always_was_and_never_calls_the_api()
    {
        await using var factory = new PortalFactory(settings: new Dictionary<string, string?> { [PortalOptions.LandingKey] = "Neutral" });
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/", Ct);

        html.ShouldContain("the link in your email");
        html.ShouldNotContain("ts-landing");
        factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Products_with_a_default_product_stops_the_host_at_start()
    {
        await using var factory = new PortalFactory(settings: Products("paperplane"));

        var failure = StartupFailure.Capture(factory, () => factory.LogSink.Events);

        failure.Message.ShouldContain(PortalOptions.LandingKey);
        failure.Message.ShouldContain(PortalOptions.DefaultProductKey);
    }

    [Fact]
    public async Task An_unknown_landing_value_stops_the_host_at_start()
    {
        await using var factory = new PortalFactory(settings: new Dictionary<string, string?> { [PortalOptions.LandingKey] = "List" });

        StartupFailure.Capture(factory, () => factory.LogSink.Events).Message.ShouldContain(PortalOptions.LandingKey);
    }
}
