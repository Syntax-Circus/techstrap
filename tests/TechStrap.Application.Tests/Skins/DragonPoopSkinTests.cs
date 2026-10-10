using TechStrap.Contracts.Skins;

namespace TechStrap.Application.Tests.Skins;

/// <summary>The dragon-poop sample in docs/skins must keep parsing strictly and resolving cleanly (the JSON below is its exact content).</summary>
public sealed class DragonPoopSkinTests
{
    private const string Focus = "\"focus\": \"#26140C\"";

    private const string Json = """
        {
          "background": "#F6E8C2",
          "surface": "#FFF8E4",
          "ink": "#1D120B",
          "muted": "#4A4640",
          "border": "#1D120B",
          "brand": "#63371F",
          "chrome": "#26140C",
          "focus": "#26140C",
          "headingFont": "pixelify",
          "bodyFont": "nunito",
          "radius": "square",
          "borderWidth": 4,
          "shadow": "hard",
          "button": "bevel",
          "header": "solid"
        }
        """;

    [Fact]
    public void The_sample_parses_strictly_and_resolves_without_problems()
    {
        SkinSerializer.TryDeserialize(Json, out var skin).ShouldBeTrue();
        skin.ShouldNotBeNull();
        SkinRules.Validate(skin).ShouldBeEmpty();

        var result = SkinResolver.Resolve("classic", skin, null);

        result.Problems.ShouldBeEmpty();
        result.Skin.Tokens.Background.ShouldBe("#F6E8C2");
        result.Skin.Tokens.Brand.ShouldBe("#63371F");
        result.Skin.BrandIsExplicit.ShouldBeTrue();
        result.Skin.OnBrand.ShouldBe("#FFFFFF");
        result.Skin.OnChrome.ShouldBe("#FFFFFF");
        SkinCss.Attributes(result.Skin).Select(a => a.Key + "=" + a.Value).ShouldBe(["data-ts-shadow=hard", "data-ts-button=bevel", "data-ts-header=solid"]);
    }

    [Fact]
    public void Gold_focus_on_the_parchment_page_fails_the_contrast_rule_which_is_why_the_sample_does_not_use_it()
    {
        Json.ShouldContain(Focus);
        var goldJson = Json.Replace(Focus, "\"focus\": \"#FFCF4A\"");
        goldJson.ShouldNotBe(Json);

        SkinSerializer.TryDeserialize(goldJson, out var gold).ShouldBeTrue();
        var result = SkinResolver.Resolve("classic", gold, null);

        result.Problems.ShouldContain(new SkinProblem("skin-contrast-invalid", "focus/background"));
    }
}
