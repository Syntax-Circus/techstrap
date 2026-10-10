using TechStrap.Contracts.Skins;

namespace TechStrap.Application.Tests.Skins;

public sealed class SkinRulesTests
{
    private static ProductSkin Full() => new(
        Pack: "paper", Background: "#FFFFFF", Surface: "#FFFFFF", Ink: "#000000", Muted: "#333333", Border: "#CCCCCC", Brand: "#112233",
        Chrome: "#000000", Focus: "#000000", HeadingFont: "atkinson", BodyFont: "nunito", Radius: "round", BorderWidth: 2,
        Shadow: "hard", Button: "bevel", Header: "band");

    [Fact]
    public void A_valid_full_skin_has_no_problems()
    {
        SkinRules.Validate(Full()).ShouldBeEmpty();
    }

    [Fact]
    public void An_empty_skin_has_no_problems()
    {
        SkinRules.Validate(new ProductSkin()).ShouldBeEmpty();
    }

    [Fact]
    public void A_short_hex_is_invalid_for_the_named_field()
    {
        SkinRules.Validate(new ProductSkin(Background: "#abc")).ShouldBe([new SkinProblem("skin-invalid", "background")]);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void The_border_width_runs_from_one_to_four(int width, bool valid)
    {
        var problems = SkinRules.Validate(new ProductSkin(BorderWidth: width));

        problems.Count.ShouldBe(valid ? 0 : 1);
        if (!valid)
        {
            problems[0].ShouldBe(new SkinProblem("skin-invalid", "borderWidth"));
        }
    }

    [Fact]
    public void An_unknown_pack_is_invalid()
    {
        SkinRules.Validate(new ProductSkin(Pack: "nope")).ShouldBe([new SkinProblem("skin-invalid", "pack")]);
    }

    [Fact]
    public void An_unknown_font_is_invalid()
    {
        SkinRules.Validate(new ProductSkin(BodyFont: "comic")).ShouldBe([new SkinProblem("skin-invalid", "bodyFont")]);
    }

    [Fact]
    public void Enum_values_are_case_sensitive()
    {
        SkinRules.Validate(new ProductSkin(Header: "BAND")).ShouldBe([new SkinProblem("skin-invalid", "header")]);
    }
}
