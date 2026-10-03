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
        var handler = new GetPublicProductRequestHandler(_products);

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
    [InlineData(null)]
    [InlineData("")]
    [InlineData("missing")]
    public async Task A_blank_or_unknown_key_is_not_found(string? key)
    {
        // Arrange
        _products.GetByKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Product?)null);
        var handler = new GetPublicProductRequestHandler(_products);

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
        var handler = new GetPublicProductRequestHandler(_products);

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
}
