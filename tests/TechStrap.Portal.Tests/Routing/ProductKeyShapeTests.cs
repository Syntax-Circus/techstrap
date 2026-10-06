using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Tests.Routing;

/// <summary>The shape every product key has (the Domain's slug rule, at most 40 characters). A key that does not have it is never sent to the API.</summary>
public sealed class ProductKeyShapeTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("paperplane")]
    [InlineData("paper-plane")]
    [InlineData("p2-x9")]
    [InlineData("1")]
    public void A_slug_is_well_formed(string key) => ProductKeyShape.IsWellFormed(key).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Paper")]
    [InlineData("paper_plane")]
    [InlineData("paper plane")]
    [InlineData("paper/plane")]
    [InlineData("-paper")]
    [InlineData("paper-")]
    [InlineData("paper--plane")]
    [InlineData("paper\n")]
    [InlineData("..")]
    [InlineData("a?b")]
    public void Anything_else_is_not(string? key) => ProductKeyShape.IsWellFormed(key).ShouldBeFalse();

    [Fact]
    public void Forty_characters_is_the_longest()
    {
        ProductKeyShape.IsWellFormed(new string('a', ProductKeyShape.MaxLength)).ShouldBeTrue();
        ProductKeyShape.IsWellFormed(new string('a', ProductKeyShape.MaxLength + 1)).ShouldBeFalse();
        ProductKeyShape.MaxLength.ShouldBe(40);
    }

    [Fact]
    public void A_non_ascii_letter_or_digit_is_not_well_formed()
    {
        ProductKeyShape.IsWellFormed("pap" + char.ConvertFromUtf32(0xE9) + "r").ShouldBeFalse();
        ProductKeyShape.IsWellFormed("paper" + char.ConvertFromUtf32(0x0663)).ShouldBeFalse();
    }
}
