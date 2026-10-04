using TechStrap.Admin.Features.Account;

namespace TechStrap.Admin.Tests.Components;

public sealed class PublicNamePreviewTests
{
    [Theory]
    [InlineData(null, "Sam Ortiz", "Orbitly", "Sam from Orbitly Support")]
    [InlineData("", "Sam Ortiz", "Orbitly", "Sam from Orbitly Support")]
    [InlineData("   ", "Sam Ortiz", "Orbitly", "Sam from Orbitly Support")]
    [InlineData("Samantha", "Sam Ortiz", "Orbitly", "Samantha from Orbitly Support")]
    [InlineData("  Samantha  ", "Sam Ortiz", " Orbitly ", "Samantha from Orbitly Support")]
    [InlineData(null, "  Sam   Ortiz ", "Orbitly", "Sam from Orbitly Support")]
    public void A_typed_name_or_the_first_word_of_the_profile_name_goes_before_the_product(string? typed, string? profile, string product, string expected)
    {
        PublicNamePreview.Build(typed, profile, product).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, "sam@example.com")]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("  ", "   ")]
    public void With_no_usable_name_it_is_the_products_plain_support_name_and_an_email_is_never_shown(string? typed, string? profile)
    {
        var line = PublicNamePreview.Build(typed, profile, "Orbitly");

        line.ShouldBe("Orbitly Support");
        line.ShouldNotContain("@");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Without_an_active_product_the_product_is_a_placeholder(string? product)
    {
        PublicNamePreview.Build(null, "Sam Ortiz", product).ShouldBe("Sam from [product] Support");
        PublicNamePreview.Build(null, null, product).ShouldBe("[product] Support");
    }

    [Fact]
    public void FirstWord_skips_a_profile_name_that_is_an_email_address()
    {
        PublicNamePreview.FirstWord("sam@example.com Ortiz").ShouldBeNull();
        PublicNamePreview.FirstWord("Sam Ortiz").ShouldBe("Sam");
        PublicNamePreview.FirstWord(null).ShouldBeNull();
    }
}
