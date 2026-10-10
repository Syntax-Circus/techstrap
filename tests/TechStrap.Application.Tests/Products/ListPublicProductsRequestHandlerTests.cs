using NSubstitute;
using TechStrap.Application.Persistence;
using TechStrap.Application.Products;
using TechStrap.Contracts.Products;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Tests.Products;

/// <summary>PHASE-09c Review Focus 5 (product enumeration): the list holds the key and the display name of ACTIVE products and nothing else.</summary>
public sealed class ListPublicProductsRequestHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IProductLogoUrls _logoUrls = Substitute.For<IProductLogoUrls>();

    private static Product Stored(string key, string displayName, bool isActive)
    {
        var branding = ProductBranding.Restore(displayName, "/brand/logo.png", "#7C3AED", null, null);
        return Product.Restore(Guid.CreateVersion7(), key, "Internal " + displayName, "PREFIX", branding, isActive, version: 1);
    }

    [Fact]
    public async Task The_list_asks_for_active_products_only_and_returns_key_and_display_name_ordered_by_key()
    {
        _products.ListAsync(true, Arg.Any<CancellationToken>()).Returns([Stored("paperplane", "Paperplane", true), Stored("acme", "Acme Corp", true), Stored("orbitly", "Orbitly", true)]);

        var result = await new ListPublicProductsRequestHandler(_products, _logoUrls).HandleAsync(Ct);

        result.Value.Select(product => (product.Key, product.DisplayName)).ShouldBe([("acme", "Acme Corp"), ("orbitly", "Orbitly"), ("paperplane", "Paperplane")]);
        await _products.Received(1).ListAsync(true, Ct);
        await _products.DidNotReceive().ListAsync(false, Ct);
    }

    [Fact]
    public async Task An_inactive_product_never_appears_even_if_the_repository_returned_it()
    {
        _products.ListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns([Stored("dormant", "Dormant", false), Stored("orbitly", "Orbitly", true)]);

        var result = await new ListPublicProductsRequestHandler(_products, _logoUrls).HandleAsync(Ct);

        result.Value.Select(product => product.Key).ShouldBe(["orbitly"]);
    }

    [Fact]
    public async Task The_list_is_capped_keeping_the_first_keys_in_order()
    {
        var many = Enumerable.Range(0, PublicProductLimits.MaxListed + 5).Select(i => Stored($"p{i:D5}", $"Product {i}", true)).Reverse().ToList();
        _products.ListAsync(true, Arg.Any<CancellationToken>()).Returns(many);

        var result = await new ListPublicProductsRequestHandler(_products, _logoUrls).HandleAsync(Ct);

        result.Value.Count.ShouldBe(PublicProductLimits.MaxListed);
        result.Value[0].Key.ShouldBe("p00000");
        result.Value[^1].Key.ShouldBe($"p{PublicProductLimits.MaxListed - 1:D5}");
    }

    [Fact]
    public async Task No_active_product_is_an_empty_list_not_an_error()
    {
        _products.ListAsync(true, Arg.Any<CancellationToken>()).Returns([]);

        var result = await new ListPublicProductsRequestHandler(_products, _logoUrls).HandleAsync(Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_summary_carries_the_portal_host_or_null()
    {
        var withHost = Product.Restore(Guid.CreateVersion7(), "orbitly", "Orbitly", "ORB", ProductBranding.Restore("Orbitly", null, "#7C3AED", null, null), true, 1, "support.orbitly.example");
        _products.ListAsync(true, Arg.Any<CancellationToken>()).Returns([withHost, Stored("acme", "Acme Corp", true)]);

        var result = await new ListPublicProductsRequestHandler(_products, _logoUrls).HandleAsync(Ct);

        result.Value.Select(product => product.PortalHost).ShouldBe([null, "support.orbitly.example"]);
        result.Value[0].PortalHost.ShouldBeNull();
    }

    [Fact]
    public void The_summary_dto_carries_only_what_a_landing_card_shows()
    {
        typeof(PublicProductSummaryDto).GetProperties().Select(property => property.Name).Order().ShouldBe(["AccentColour", "DisplayName", "Key", "ListedOnLanding", "LogoUrl", "PortalHost", "Skin", "Tagline"]);
        PublicProductLimits.MaxListed.ShouldBe(1_000);
    }

    [Fact]
    public async Task The_list_keeps_unlisted_active_products_and_carries_the_landing_fields()
    {
        var unlisted = Product.Restore(Guid.CreateVersion7(), "acme", "Acme Corp", "ACM", ProductBranding.Restore("Acme Corp", "https://cdn.acme.test/l.png", "#7C3AED", null, null, "Acme tagline"), true, 1, null, listedOnLanding: false);
        var uploaded = Product.Restore(Guid.CreateVersion7(), "orbitly", "Orbitly", "ORB", ProductBranding.Restore("Orbitly", null, "#1F6FEB", null, null, null, "0123456789abcdef0123456789abcdef.webp"), true, 1, "support.orbitly.example");
        _products.ListAsync(true, Arg.Any<CancellationToken>()).Returns([uploaded, unlisted]);
        _logoUrls.UrlFor("0123456789abcdef0123456789abcdef.webp").Returns("https://api.test/product-logos/0123456789abcdef0123456789abcdef.webp");

        var result = await new ListPublicProductsRequestHandler(_products, _logoUrls).HandleAsync(Ct);

        result.Value.ShouldBe([
            new PublicProductSummaryDto("acme", "Acme Corp", null, "Acme tagline", "https://cdn.acme.test/l.png", "#7C3AED", ListedOnLanding: false),
            new PublicProductSummaryDto("orbitly", "Orbitly", "support.orbitly.example", null, "https://api.test/product-logos/0123456789abcdef0123456789abcdef.webp", "#1F6FEB", ListedOnLanding: true)]);
    }
}
