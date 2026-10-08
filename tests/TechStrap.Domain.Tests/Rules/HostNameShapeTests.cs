using TechStrap.Domain.Rules;

namespace TechStrap.Domain.Tests.Rules;

public sealed class HostNameShapeTests
{
    // 63 + 1 + 63 + 1 + 63 + 1 + 61 = 253 characters.
    private static readonly string LongestValid = string.Join('.', Label('a', 63), Label('b', 63), Label('c', 63), Label('d', 61));

    private static string Label(char c, int length) => new(c, length);

    [Theory]
    [InlineData("support.dragonpoop.com", "support.dragonpoop.com")]
    [InlineData("A-B.Example.CO.UK", "a-b.example.co.uk")]
    [InlineData("  x1.y2  ", "x1.y2")]
    [InlineData("x1.y2", "x1.y2")]
    [InlineData("a.b1", "a.b1")]
    public void A_well_formed_host_is_trimmed_and_lower_cased(string input, string expected)
    {
        HostNameShape.TryNormalize(input, out var host).ShouldBeTrue();
        host.ShouldBe(expected);
        HostNameShape.IsWellFormed(expected).ShouldBeTrue();
    }

    [Fact]
    public void The_longest_allowed_host_is_accepted()
    {
        LongestValid.Length.ShouldBe(DomainLimits.HostNameMaxLength);
        HostNameShape.TryNormalize(LongestValid, out var host).ShouldBeTrue();
        host.ShouldBe(LongestValid);
    }

    [Fact]
    public void A_host_one_character_too_long_is_rejected()
    {
        var tooLong = LongestValid + "d";
        tooLong.Length.ShouldBe(DomainLimits.HostNameMaxLength + 1);
        HostNameShape.TryNormalize(tooLong, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_label_longer_than_63_characters_is_rejected() =>
        HostNameShape.TryNormalize(Label('a', 64) + ".com", out _).ShouldBeFalse();

    [Theory]
    [InlineData("localhost")]
    [InlineData("https://x.y")]
    [InlineData("x.y:443")]
    [InlineData("x.y/path")]
    [InlineData("u@x.y")]
    [InlineData("-a.b")]
    [InlineData("a-.b")]
    [InlineData("a..b")]
    [InlineData(".a.b")]
    [InlineData("a.b.")]
    [InlineData("a_b.c")]
    [InlineData("\u00e9.example")]
    [InlineData("caf\u00e9.example")]
    [InlineData("\u212A.example")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2")]
    [InlineData("10.0.0.1")]
    [InlineData("x. y")]
    [InlineData("x .y")]
    public void A_malformed_host_is_rejected(string input)
    {
        HostNameShape.TryNormalize(input, out var host).ShouldBeFalse();
        host.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_input_succeeds_with_no_host(string? input)
    {
        HostNameShape.TryNormalize(input, out var host).ShouldBeTrue();
        host.ShouldBeNull();
    }

    [Theory]
    [InlineData("support.example.com", true)]
    [InlineData("Support.Example.com", false)]
    [InlineData(" support.example.com", false)]
    [InlineData("localhost", false)]
    public void IsWellFormed_requires_the_normalised_form(string host, bool expected) =>
        HostNameShape.IsWellFormed(host).ShouldBe(expected);
}
