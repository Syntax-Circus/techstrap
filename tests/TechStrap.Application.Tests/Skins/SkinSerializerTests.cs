using TechStrap.Contracts.Skins;

namespace TechStrap.Application.Tests.Skins;

public sealed class SkinSerializerTests
{
    [Fact]
    public void A_full_skin_round_trips()
    {
        var skin = new ProductSkin(
            Pack: "paper", Background: "#FFFFFF", Surface: "#FFFFFF", Ink: "#000000", Muted: "#333333", Border: "#CCCCCC", Brand: "#112233",
            Chrome: "#000000", Focus: "#000000", HeadingFont: "atkinson", BodyFont: "nunito", Radius: "round", BorderWidth: 2,
            Shadow: "hard", Button: "bevel", Header: "band");

        var json = SkinSerializer.Serialize(skin);

        json.ShouldNotBeNull();
        SkinSerializer.TryDeserialize(json, out var back).ShouldBeTrue();
        back.ShouldBe(skin);
    }

    [Fact]
    public void A_single_field_serialises_compactly_in_camel_case()
    {
        var json = SkinSerializer.Serialize(new ProductSkin(Brand: "#112233"));

        json.ShouldBe("{\"brand\":\"#112233\"}");
        SkinSerializer.TryDeserialize(json, out var back).ShouldBeTrue();
        back.ShouldBe(new ProductSkin(Brand: "#112233"));
    }

    [Fact]
    public void An_empty_or_missing_skin_serialises_to_null()
    {
        SkinSerializer.Serialize(new ProductSkin()).ShouldBeNull();
        SkinSerializer.Serialize(null).ShouldBeNull();
    }

    [Fact]
    public void An_unknown_member_is_rejected()
    {
        SkinSerializer.TryDeserialize("{\"background\":\"#FFFFFF\",\"extra\":1}", out var skin).ShouldBeFalse();
        skin.ShouldBeNull();
    }

    [Fact]
    public void Bad_json_is_rejected()
    {
        SkinSerializer.TryDeserialize("not json", out _).ShouldBeFalse();
    }

    [Fact]
    public void Json_over_the_cap_is_rejected()
    {
        SkinSerializer.TryDeserialize(new string('x', SkinRules.MaxJsonLength + 1), out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Nothing_is_a_valid_empty_skin(string? json)
    {
        SkinSerializer.TryDeserialize(json, out var skin).ShouldBeTrue();
        skin.ShouldBeNull();
    }
}
