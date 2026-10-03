using Microsoft.Extensions.Time.Testing;
using TechStrap.Domain.Products;

namespace TechStrap.Domain.Tests.Products;

public sealed class ProductTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_new_product_is_active_and_defaults_its_branding_from_its_name()
    {
        var product = Product.Create("orbitly", "Orbitly", "ORB", null, _clock).Value;

        product.IsActive.ShouldBeTrue();
        product.Key.ShouldBe("orbitly");
        product.NumberPrefix.ShouldBe("ORB");
        product.Branding.DisplayName.ShouldBe("Orbitly");
        product.Branding.AccentColour.ShouldBe(ProductBranding.DefaultAccentColour);
        product.Id.ShouldNotBe(Guid.Empty);
    }

    [Theory]
    [InlineData("Orbitly")]
    [InlineData("orb itly")]
    [InlineData("-orbitly")]
    [InlineData("orbitly-")]
    [InlineData("orb--itly")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_key_that_is_not_a_slug_is_rejected(string key)
    {
        var result = Product.Create(key, "Orbitly", "ORB", null, _clock);

        result.Error!.Kind.ShouldBe(DomainErrorKind.Validation);
        result.Error.Code.ShouldBe("key-invalid");
    }

    [Fact]
    public void A_key_longer_than_the_limit_is_rejected()
    {
        Product.Create(new string('a', 41), "Orbitly", "ORB", null, _clock).Error!.Code.ShouldBe("key-invalid");
    }

    [Theory]
    [InlineData("orb")]
    [InlineData("O")]
    [InlineData("1ORB")]
    [InlineData("")]
    public void An_invalid_number_prefix_is_rejected(string prefix)
    {
        Product.Create("orbitly", "Orbitly", prefix, null, _clock).Error!.Code.ShouldBe("number-prefix-invalid");
    }

    [Theory]
    [InlineData("#1f6feb", "#1F6FEB")]
    [InlineData(" #AbCdEf ", "#ABCDEF")]
    public void An_accent_colour_is_normalised_to_upper_case_hex(string accent, string expected)
    {
        ProductBranding.Create("Orbitly", null, accent, null, null).Value.AccentColour.ShouldBe(expected);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("1F6FEB")]
    [InlineData("#GGGGGG")]
    public void A_malformed_accent_colour_is_rejected(string accent)
    {
        ProductBranding.Create("Orbitly", null, accent, null, null).Error!.Code.ShouldBe("accent-colour-invalid");
    }

    [Fact]
    public void Branding_addresses_are_validated_and_lower_cased()
    {
        var branding = ProductBranding.Create("Orbitly", null, null, "Support@Orbitly.example", "  ").Value;

        branding.FromAddress.ShouldBe("support@orbitly.example");
        branding.ReplyTo.ShouldBeNull();
        ProductBranding.Create("Orbitly", null, null, "not-an-address", null).Error!.Code.ShouldBe("from-address-invalid");
    }

    [Fact]
    public void Updating_details_changes_name_and_branding_but_never_the_key_or_prefix()
    {
        var product = Product.Create("orbitly", "Orbitly", "ORB", null, _clock).Value;
        var branding = ProductBranding.Create("Orbitly Cloud", "/logo.svg", "#112233", null, null).Value;

        product.UpdateDetails("Orbitly Cloud", branding).IsSuccess.ShouldBeTrue();

        product.Name.ShouldBe("Orbitly Cloud");
        product.Branding.ShouldBe(branding);
        product.Key.ShouldBe("orbitly");
        product.NumberPrefix.ShouldBe("ORB");
    }

    [Fact]
    public void A_product_can_be_deactivated_and_reactivated()
    {
        var product = Product.Create("orbitly", "Orbitly", "ORB", null, _clock).Value;

        product.SetActive(false);
        product.IsActive.ShouldBeFalse();
        product.SetActive(true);
        product.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Branding_can_only_be_built_through_Create_so_new_and_with_cannot_bypass_validation()
    {
        typeof(ProductBranding).GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).ShouldBeEmpty();
        typeof(ProductBranding).GetProperties().ShouldAllBe(property => property.SetMethod == null);

        var branding = ProductBranding.Create("Orbitly", null, "#112233", null, null).Value;
        branding.ShouldBe(ProductBranding.Create("Orbitly", null, "#112233", null, null).Value);
        branding.ShouldNotBe(ProductBranding.Create("Orbitly", null, "#112234", null, null).Value);
    }

    [Fact]
    public void Restore_rebuilds_stored_branding_unchanged()
    {
        var branding = ProductBranding.Restore("Orbitly", "/l.svg", "#112233", "a@example.com", null);

        branding.DisplayName.ShouldBe("Orbitly");
        branding.AccentColour.ShouldBe("#112233");
        branding.FromAddress.ShouldBe("a@example.com");
    }
}
