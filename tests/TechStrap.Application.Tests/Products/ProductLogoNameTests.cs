using TechStrap.Contracts.Branding;
using TechStrap.Contracts.Products;

namespace TechStrap.Application.Tests.Products;

public sealed class ProductLogoNameTests
{
    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef.png", true)]
    [InlineData("0123456789abcdef0123456789abcdef.jpg", true)]
    [InlineData("0123456789abcdef0123456789abcdef.webp", true)]
    [InlineData("0123456789abcdef0123456789abcdef.gif", false)]
    [InlineData("0123456789abcdef0123456789abcdef.svg", false)]
    [InlineData("0123456789ABCDEF0123456789abcdef.png", false)]
    [InlineData("../0123456789abcdef0123456789abcdef.png", false)]
    [InlineData("0123456789abcdef0123456789abcdef", false)]
    [InlineData("0123456789abcdef0123456789abcdef.png\n", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_a_name_the_store_writes_is_valid(string? name, bool expected) => ProductLogoName.IsValid(name).ShouldBe(expected);

    [Fact]
    public void The_storage_key_and_content_type_follow_the_name()
    {
        ProductLogoName.StorageKey("0123456789abcdef0123456789abcdef.webp").ShouldBe("product-logos/0123456789abcdef0123456789abcdef.webp");
        ProductLogoName.ContentTypeOf("0123456789abcdef0123456789abcdef.png").ShouldBe("image/png");
        ProductLogoName.ContentTypeOf("0123456789abcdef0123456789abcdef.jpg").ShouldBe("image/jpeg");
        ProductLogoName.ContentTypeOf("0123456789abcdef0123456789abcdef.webp").ShouldBe("image/webp");
        ProductLogoName.MaxLength.ShouldBe(37);
        ProductLogoLimits.MaxBytes.ShouldBe(1_048_576);
        ProductLogoLimits.AllowedExtensions.ShouldBe([".png", ".jpg", ".jpeg", ".webp"]);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("Tickets for the Orbitly desktop app.", true)]
    [InlineData("Line one\nline two", false)]
    [InlineData("Tab\there", false)]
    public void A_tagline_is_one_line_of_plain_text(string? value, bool expected) => BrandingRules.IsAcceptableTagline(value).ShouldBe(expected);

    [Fact]
    public void A_tagline_may_be_160_characters_but_not_161()
    {
        BrandingRules.IsAcceptableTagline(new string('a', 160)).ShouldBeTrue();
        BrandingRules.IsAcceptableTagline(new string('a', 161)).ShouldBeFalse();
        BrandingRules.TaglineMaxLength.ShouldBe(160);
    }
}
