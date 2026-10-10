using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The rail shows only the semver of the running build: the core and the prerelease label, never the build metadata, and nothing when the value is not SemVer.</summary>
public sealed class BuildVersionTests
{
    [Theory]
    [InlineData("0.4.1", "0.4.1")]
    [InlineData("v0.4.1", "0.4.1")]
    [InlineData("0.4.1+abc", "0.4.1")]
    [InlineData("0.4.1+Branch.main.Sha.abc123", "0.4.1")]
    [InlineData("0.4.1-rc.1+abc", "0.4.1-rc.1")]
    [InlineData("0.5.0-alpha.0.3+Branch.main", "0.5.0-alpha.0.3")]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData(" 0.4.1 ", "0.4.1")]
    public void A_semver_keeps_its_core_and_prerelease_and_loses_the_build_metadata(string value, string expected)
    {
        BuildVersion.Parse(value).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1.0.0.0")]
    [InlineData("1.0")]
    [InlineData("01.2.3")]
    [InlineData("0.4.1-")]
    [InlineData("0.4.1-01")]
    [InlineData("+abc")]
    [InlineData("0.4.1 beta")]
    public void Anything_else_shows_nothing(string? value)
    {
        BuildVersion.Parse(value).ShouldBeNull();
    }

    [Fact]
    public void The_value_is_the_parsed_semver()
    {
        new BuildVersion("0.4.1-rc.1+abc").Semver.ShouldBe("0.4.1-rc.1");
        new BuildVersion(null).Semver.ShouldBeNull();
    }
}
