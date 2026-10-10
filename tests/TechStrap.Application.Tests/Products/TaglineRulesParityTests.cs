using TechStrap.Contracts.Branding;
using TechStrap.Domain.Products;
using TechStrap.Domain.Rules;

namespace TechStrap.Application.Tests.Products;

/// <summary>Domain cannot reference Contracts, so the Admin's tagline pre-check and the Domain rule are kept in step here.</summary>
public sealed class TaglineRulesParityTests
{
    [Fact]
    public void The_limits_match() => BrandingRules.TaglineMaxLength.ShouldBe(DomainLimits.TaglineMaxLength);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("One line.")]
    [InlineData("Two\nlines")]
    [InlineData("Tab\tbed")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Contracts_and_Domain_agree_on_every_sample(string? value) =>
        BrandingRules.IsAcceptableTagline(value).ShouldBe(ProductBranding.Create("Orbitly", null, null, null, null, value).IsSuccess);
}
