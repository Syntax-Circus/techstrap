using TechStrap.Domain.Products;
using TechStrap.Domain.Rules;

namespace TechStrap.Domain.Tests.Products;

/// <summary>D-052: the tagline is one line of plain text, the uploaded logo survives a branding update, and a product is listed on the landing page unless told otherwise.</summary>
public sealed class ProductLandingAndLogoTests
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    [Theory]
    [InlineData("  Tickets for the desktop app.  ", "Tickets for the desktop app.")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void A_tagline_is_trimmed_and_blank_means_none(string? input, string? expected) =>
        ProductBranding.Create("Orbitly", null, null, null, null, input).Value.Tagline.ShouldBe(expected);

    [Theory]
    [InlineData("Line one\nline two", "tagline-invalid")]
    [InlineData("Tab\there", "tagline-invalid")]
    public void A_tagline_with_a_line_break_or_control_character_is_refused(string input, string code)
    {
        var result = ProductBranding.Create("Orbitly", null, null, null, null, input);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe(code);
        result.Error.Target.ShouldBe("tagline");
    }

    [Fact]
    public void A_tagline_may_be_160_characters_but_161_is_too_long()
    {
        ProductBranding.Create("Orbitly", null, null, null, null, new string('a', 160)).IsSuccess.ShouldBeTrue();
        var tooLong = ProductBranding.Create("Orbitly", null, null, null, null, new string('a', 161));
        tooLong.Error!.Code.ShouldBe("tagline-too-long");
        DomainLimits.TaglineMaxLength.ShouldBe(160);
    }

    [Fact]
    public void Two_brandings_that_differ_only_in_the_tagline_are_not_equal()
    {
        var a = ProductBranding.Restore("Orbitly", null, "#1F6FEB", null, null, "One");
        var b = ProductBranding.Restore("Orbitly", null, "#1F6FEB", null, null, "Two");

        a.ShouldNotBe(b);
    }

    [Fact]
    public void CreateForUpdate_keeps_the_uploaded_logo_which_the_request_cannot_carry()
    {
        var current = ProductBranding.Restore("Orbitly", "https://cdn.orbitly.test/l.png", "#1F6FEB", null, null, "Old", "0123456789abcdef0123456789abcdef.png");

        var updated = ProductBranding.CreateForUpdate(current, "Orbitly Cloud", "https://cdn.orbitly.test/l.png", "#7C3AED", null, null, "New").Value;

        updated.UploadedLogo.ShouldBe("0123456789abcdef0123456789abcdef.png");
        updated.Tagline.ShouldBe("New");
        updated.DisplayName.ShouldBe("Orbitly Cloud");
    }

    [Fact]
    public void WithUploadedLogo_replaces_only_the_uploaded_logo()
    {
        var current = ProductBranding.Restore("Orbitly", "https://cdn.orbitly.test/l.png", "#1F6FEB", "a@b.test", null, "Tag");

        var with = current.WithUploadedLogo("0123456789abcdef0123456789abcdef.webp");
        var without = with.WithUploadedLogo(null);

        with.UploadedLogo.ShouldBe("0123456789abcdef0123456789abcdef.webp");
        with.LogoPath.ShouldBe("https://cdn.orbitly.test/l.png");
        with.Tagline.ShouldBe("Tag");
        without.UploadedLogo.ShouldBeNull();
        without.ShouldBe(current);
    }

    [Fact]
    public void A_restored_product_without_the_new_arguments_is_listed_with_no_tagline_or_uploaded_logo()
    {
        var product = Product.Restore(Guid.CreateVersion7(), "orbitly", "Orbitly", "ORB", ProductBranding.Restore("Orbitly", null, "#1F6FEB", null, null), true, 1);

        product.ListedOnLanding.ShouldBeTrue();
        product.Branding.Tagline.ShouldBeNull();
        product.Branding.UploadedLogo.ShouldBeNull();
    }

    [Fact]
    public void A_created_product_is_listed_and_can_be_unlisted_and_given_an_uploaded_logo()
    {
        var product = Product.Create("orbitly", "Orbitly", "ORB", null, Clock).Value;
        product.ListedOnLanding.ShouldBeTrue();

        product.SetListedOnLanding(false);
        product.SetUploadedLogo("0123456789abcdef0123456789abcdef.png");

        product.ListedOnLanding.ShouldBeFalse();
        product.Branding.UploadedLogo.ShouldBe("0123456789abcdef0123456789abcdef.png");
        product.Branding.DisplayName.ShouldBe("Orbitly");
    }
}
