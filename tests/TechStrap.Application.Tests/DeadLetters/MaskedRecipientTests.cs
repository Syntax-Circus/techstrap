using TechStrap.Application.DeadLetters;

namespace TechStrap.Application.Tests.DeadLetters;

public sealed class MaskedRecipientTests
{
    [Theory]
    [InlineData("ann@example.com", "a***@example.com")]
    [InlineData("Ann.Lovelace@Example.com", "A***@Example.com")]
    [InlineData("a@example.com", "***@example.com")]
    [InlineData("", "***")]
    [InlineData("   ", "***")]
    [InlineData(null, "***")]
    [InlineData("no-at-sign", "***")]
    [InlineData("@example.com", "***")]
    [InlineData("ann@", "***")]
    [InlineData("a@b@c.com", "***")]
    public void Recipients_are_masked(string? input, string expected) => MaskedRecipient.Mask(input).ShouldBe(expected);

    [Fact]
    public void The_mask_never_contains_more_than_the_first_character_of_the_local_part()
    {
        var masked = MaskedRecipient.Mask("secretlocal@example.com");

        masked.ShouldNotContain("ecret");
        masked.ShouldNotContain("local");
        masked.ShouldBe("s***@example.com");
    }
}
