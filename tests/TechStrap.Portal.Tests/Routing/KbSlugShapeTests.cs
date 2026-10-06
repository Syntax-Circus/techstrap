using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Tests.Routing;

/// <summary>The shape every category and article slug has (the Domain's slug rule, at most 80 characters). A slug that does not have it is never sent to the API.</summary>
public sealed class KbSlugShapeTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("accounts")]
    [InlineData("reset-password")]
    [InlineData("how-to-2fa-1")]
    [InlineData("1")]
    [InlineData("search")]
    public void A_slug_is_well_formed(string slug) => KbSlugShape.IsWellFormed(slug).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Accounts")]
    [InlineData("reset_password")]
    [InlineData("reset password")]
    [InlineData("reset/password")]
    [InlineData("-reset")]
    [InlineData("reset-")]
    [InlineData("reset--password")]
    [InlineData("reset\n")]
    [InlineData("..")]
    [InlineData("a?b")]
    [InlineData("a#b")]
    [InlineData("a%2Fb")]
    public void Anything_else_is_not(string? slug) => KbSlugShape.IsWellFormed(slug).ShouldBeFalse();

    [Fact]
    public void Eighty_characters_is_the_longest_and_it_is_the_domains_limit_for_a_kb_slug()
    {
        KbSlugShape.IsWellFormed(new string('a', KbSlugShape.MaxLength)).ShouldBeTrue();
        KbSlugShape.IsWellFormed(new string('a', KbSlugShape.MaxLength + 1)).ShouldBeFalse();
        KbSlugShape.MaxLength.ShouldBe(80);
    }

    [Fact]
    public void A_non_ascii_letter_or_digit_is_not_well_formed()
    {
        KbSlugShape.IsWellFormed("caf" + char.ConvertFromUtf32(0xE9)).ShouldBeFalse();
        KbSlugShape.IsWellFormed("guide" + char.ConvertFromUtf32(0x0663)).ShouldBeFalse();
    }
}
