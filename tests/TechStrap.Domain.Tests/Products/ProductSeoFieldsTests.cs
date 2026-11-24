using TechStrap.Domain.Products;
using TechStrap.Domain.Rules;

namespace TechStrap.Domain.Tests.Products;

/// <summary>The per-product SEO description and help center intro: optional plain text, trimmed, blank means none, bounded.</summary>
public sealed class ProductSeoFieldsTests
{
    private static ProductBranding Create(string? description, string? intro) =>
        ProductBranding.Create("Orbitly", null, null, null, null, null, description, intro).Value;

    [Theory]
    [InlineData("  Quick answers about Orbitly: how to sign in, manage your plan, fix a failed delivery and contact the support team directly.  ", "Quick answers about Orbitly: how to sign in, manage your plan, fix a failed delivery and contact the support team directly.")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void The_description_is_trimmed_and_blank_means_none(string? input, string? expected) =>
        Create(input, null).SeoDescription.ShouldBe(expected);

    [Theory]
    [InlineData("  Help with Orbitly.\nSecond line.  ", "Help with Orbitly.\nSecond line.")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void The_intro_is_trimmed_and_blank_means_none(string? input, string? expected) =>
        Create(null, input).HelpCenterIntro.ShouldBe(expected);

    [Fact]
    public void The_description_may_be_160_characters_but_161_is_too_long()
    {
        Create(new string('a', 160), null).SeoDescription.ShouldNotBeNull();
        var tooLong = ProductBranding.Create("Orbitly", null, null, null, null, null, new string('a', 161), null);
        tooLong.Error!.Code.ShouldBe("seo-description-too-long");
        tooLong.Error.Target.ShouldBe("seo-description");
        DomainLimits.SeoDescriptionMaxLength.ShouldBe(160);
    }

    [Fact]
    public void The_description_is_one_line()
    {
        var result = ProductBranding.Create("Orbitly", null, null, null, null, null, "One\ntwo", null);

        result.Error!.Code.ShouldBe("seo-description-invalid");
    }

    [Fact]
    public void The_intro_may_be_600_characters_but_601_is_too_long()
    {
        Create(null, new string('a', 600)).HelpCenterIntro.ShouldNotBeNull();
        var tooLong = ProductBranding.Create("Orbitly", null, null, null, null, null, null, new string('a', 601));
        tooLong.Error!.Code.ShouldBe("help-center-intro-too-long");
        tooLong.Error.Target.ShouldBe("help-center-intro");
        DomainLimits.HelpCenterIntroMaxLength.ShouldBe(600);
    }

    [Fact]
    public void CreateForUpdate_sets_both_and_the_uploaded_logo_survives()
    {
        var current = ProductBranding.Restore("Orbitly", null, "#1F6FEB", null, null, null, "0123456789abcdef0123456789abcdef.png", "Old", "Old intro");

        var updated = ProductBranding.CreateForUpdate(current, "Orbitly", null, "#1F6FEB", null, null, null, "Quick answers about Orbitly: how to sign in, manage your plan, fix a failed delivery and contact the support team directly.", "New intro").Value;

        updated.SeoDescription.ShouldBe("Quick answers about Orbitly: how to sign in, manage your plan, fix a failed delivery and contact the support team directly.");
        updated.HelpCenterIntro.ShouldBe("New intro");
        updated.UploadedLogo.ShouldBe("0123456789abcdef0123456789abcdef.png");
        updated.WithUploadedLogo(null).SeoDescription.ShouldBe("Quick answers about Orbitly: how to sign in, manage your plan, fix a failed delivery and contact the support team directly.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(109)]
    public void A_description_under_110_characters_is_too_short(int length)
    {
        var result = ProductBranding.Create("Orbitly", null, null, null, null, null, new string('a', length), null);

        result.Error!.Code.ShouldBe("seo-description-too-short");
        result.Error.Target.ShouldBe("seo-description");
    }

    [Fact]
    public void A_description_of_exactly_110_characters_is_accepted()
    {
        Create(new string('a', DomainLimits.SeoDescriptionMinLength), null).SeoDescription.ShouldNotBeNull();
        DomainLimits.SeoDescriptionMinLength.ShouldBe(110);
    }
}
