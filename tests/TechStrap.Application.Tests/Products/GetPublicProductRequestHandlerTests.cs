using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Application.Products;
using TechStrap.Contracts.Branding;
using TechStrap.Contracts.Products;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Tests.Products;

public sealed class GetPublicProductRequestHandlerTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IProductLogoUrls _logoUrls = Substitute.For<IProductLogoUrls>();

    private Product Stored(string key, string displayName, string accentColour, bool isActive)
    {
        var branding = ProductBranding.Restore(displayName, null, accentColour, null, null);
        var product = Product.Restore(Guid.CreateVersion7(), key, displayName, "PREFIX", branding, isActive, version: 1);
        _products.GetByKeyAsync(key, Arg.Any<CancellationToken>()).Returns(product);
        return product;
    }

    [Fact]
    public async Task An_active_product_returns_its_public_branding_with_derived_accent_colours()
    {
        // Arrange
        var product = Stored("orbitly", "Orbitly", "#7C3AED", isActive: true);
        var handler = new GetPublicProductRequestHandler(_products, _logoUrls);

        // Act
        var result = await handler.HandleAsync("orbitly", TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var dto = result.Value;
        dto.Key.ShouldBe("orbitly");
        dto.DisplayName.ShouldBe("Orbitly");

        ProductAccent.TryDerive("#7C3AED", out var colors).ShouldBeTrue();
        dto.AccentColour.ShouldBe(colors.Accent);
        dto.OnAccentColour.ShouldBe(colors.OnAccent);
        dto.AccentInkColour.ShouldBe(colors.AccentInk);
    }

    [Theory]
    [InlineData("support.orbitly.example")]
    [InlineData(null)]
    public async Task The_public_product_carries_its_portal_host_or_null(string? host)
    {
        var branding = ProductBranding.Restore("Orbitly", null, "#7C3AED", null, null);
        _products.GetByKeyAsync("orbitly", Arg.Any<CancellationToken>()).Returns(Product.Restore(Guid.CreateVersion7(), "orbitly", "Orbitly", "ORB", branding, true, 1, host));

        var result = await new GetPublicProductRequestHandler(_products, _logoUrls).HandleAsync("orbitly", TestContext.Current.CancellationToken);

        result.Value.PortalHost.ShouldBe(host);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("missing")]
    public async Task A_blank_or_unknown_key_is_not_found(string? key)
    {
        // Arrange
        _products.GetByKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Product?)null);
        var handler = new GetPublicProductRequestHandler(_products, _logoUrls);

        // Act
        var result = await handler.HandleAsync(key, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("product-not-found");
    }

    [Fact]
    public async Task A_deactivated_product_is_not_found()
    {
        // Arrange
        Stored("dormant", "Dormant", "#7C3AED", isActive: false);
        var handler = new GetPublicProductRequestHandler(_products, _logoUrls);

        // Act
        var result = await handler.HandleAsync("dormant", TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("product-not-found");
    }

    [Fact]
    public async Task The_dto_never_exposes_from_or_reply_to_addresses()
    {
        // Arrange - just verify the type definition
        var properties = typeof(PublicProductDto).GetProperties();
        var propertyNames = properties.Select(p => p.Name.ToLower()).ToList();

        // Assert
        propertyNames.ShouldNotContain(pn => pn.Contains("from"));
        propertyNames.ShouldNotContain(pn => pn.Contains("reply"));
    }

    [Fact]
    public async Task The_public_product_carries_the_tagline_and_the_uploaded_logo_wins_over_the_linked_one()
    {
        var branding = ProductBranding.Restore("Orbitly", "https://cdn.orbitly.test/l.png", "#1F6FEB", null, null, "One line.", "0123456789abcdef0123456789abcdef.png");
        var product = Product.Restore(Guid.CreateVersion7(), "orbitly", "Orbitly", "ORB", branding, true, 1);
        _products.GetByKeyAsync("orbitly", Arg.Any<CancellationToken>()).Returns(product);
        _logoUrls.UrlFor("0123456789abcdef0123456789abcdef.png").Returns("https://api.test/product-logos/0123456789abcdef0123456789abcdef.png");

        var result = await new GetPublicProductRequestHandler(_products, _logoUrls).HandleAsync("orbitly", TestContext.Current.CancellationToken);

        result.Value.Tagline.ShouldBe("One line.");
        result.Value.LogoPath.ShouldBe("https://api.test/product-logos/0123456789abcdef0123456789abcdef.png");
    }
}
