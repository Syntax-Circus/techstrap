using TechStrap.Contracts.Branding;

namespace TechStrap.Application.Tests.Products;

public sealed class ProductAccentReadableOnTests
{
    [Theory]
    [InlineData("#F59E0B")]
    [InlineData("#1D4FA8")]
    [InlineData("#FFFF00")]
    [InlineData("#7C3AED")]
    [InlineData("#000000")]
    public void On_white_it_is_exactly_the_existing_accent_ink(string accent)
    {
        ProductAccent.TryDerive(accent, out var colors).ShouldBeTrue();

        ProductAccent.ReadableOn(accent, "#FFFFFF").ShouldBe(colors.AccentInk);
    }

    [Fact]
    public void On_a_dark_background_it_lightens_until_readable()
    {
        var ink = ProductAccent.ReadableOn("#1D4FA8", "#0F1420");

        ProductAccent.ContrastRatio(ink, "#0F1420").ShouldBeGreaterThanOrEqualTo(4.5);
        ink.ShouldNotBe("#1D4FA8");
    }

    [Fact]
    public void A_colour_that_already_reads_is_returned_as_entered_in_upper_case()
    {
        ProductAccent.ReadableOn("#6EA8FF", "#0F1420").ShouldBe("#6EA8FF");
    }
}
