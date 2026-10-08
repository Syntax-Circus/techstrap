using Microsoft.Extensions.Time.Testing;
using TechStrap.Domain.Products;

namespace TechStrap.Domain.Tests.Products;

public sealed class ProductPortalHostTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero));

    private Product NewProduct() => Product.Create("acme", "Acme", "ACME", null, _clock).Value;

    [Fact]
    public void A_new_product_has_no_portal_host() => NewProduct().PortalHost.ShouldBeNull();

    [Fact]
    public void Setting_a_host_stores_it_lower_cased_and_trimmed()
    {
        var product = NewProduct();

        product.SetPortalHost("  Support.Acme.COM ").IsSuccess.ShouldBeTrue();

        product.PortalHost.ShouldBe("support.acme.com");
    }

    [Fact]
    public void An_invalid_host_is_refused_and_the_current_host_is_kept()
    {
        var product = NewProduct();
        product.SetPortalHost("support.acme.com");

        var result = product.SetPortalHost("https://support.acme.com");

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("product-host-invalid");
        result.Error.Message.ShouldBe("Use a hostname such as support.example.com: letters, digits and hyphens, no scheme, port or path.");
        product.PortalHost.ShouldBe("support.acme.com");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void A_blank_host_clears_it(string? blank)
    {
        var product = NewProduct();
        product.SetPortalHost("support.acme.com");

        product.SetPortalHost(blank).IsSuccess.ShouldBeTrue();

        product.PortalHost.ShouldBeNull();
    }

    [Fact]
    public void Restore_round_trips_the_host_and_defaults_to_none()
    {
        var branding = ProductBranding.Create("Acme", null, null, null, null).Value;

        Product.Restore(Guid.NewGuid(), "acme", "Acme", "ACME", branding, true, 1, "support.acme.com").PortalHost.ShouldBe("support.acme.com");
        Product.Restore(Guid.NewGuid(), "acme", "Acme", "ACME", branding, true, 1).PortalHost.ShouldBeNull();
    }
}
