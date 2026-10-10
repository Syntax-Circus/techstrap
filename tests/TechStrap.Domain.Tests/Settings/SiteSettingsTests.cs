using TechStrap.Domain.Settings;

namespace TechStrap.Domain.Tests.Settings;

public sealed class SiteSettingsTests
{
    [Fact]
    public void The_default_pack_is_stored_trimmed_and_lower_case()
    {
        var settings = SiteSettings.Restore("classic", 1);

        settings.SetDefaultPack("  Slate ").IsSuccess.ShouldBeTrue();

        settings.DefaultPackKey.ShouldBe("slate");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_pack_is_refused(string? key)
    {
        var result = SiteSettings.Restore("classic", 1).SetDefaultPack(key);

        result.Error!.Code.ShouldBe("skin-pack-invalid");
        result.Error.Target.ShouldBe("default-pack");
    }

    [Fact]
    public void A_key_over_32_characters_is_refused() =>
        SiteSettings.Restore("classic", 1).SetDefaultPack(new string('a', 33)).IsFailure.ShouldBeTrue();

    [Fact]
    public void The_seed_value_is_classic() => SiteSettings.DefaultPack.ShouldBe("classic");
}
