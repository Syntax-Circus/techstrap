using Microsoft.Extensions.Configuration;
using TechStrap.AppHost;

namespace TechStrap.LocalStack.Tests;

/// <summary>The local stack's own switches.</summary>
public sealed class LocalStackOptionsTests
{
    private const string SeedDevDataKey = "LocalStack:SeedDevData";

    private static IConfiguration Configuration(string? seedDevData) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [SeedDevDataKey] = seedDevData })
        .Build();

    [Fact]
    public void SeedDevData_is_on_when_the_setting_is_missing()
    {
        LocalStackOptions.FromConfiguration(new ConfigurationBuilder().Build()).SeedDevData.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    public void SeedDevData_reads_the_setting(string? value, bool expected)
    {
        LocalStackOptions.FromConfiguration(Configuration(value)).SeedDevData.ShouldBe(expected);
    }

    [Fact]
    public void SeedDevData_rejects_any_other_value_and_names_the_setting()
    {
        var exception = Should.Throw<InvalidOperationException>(() => LocalStackOptions.FromConfiguration(Configuration("yes")));

        exception.Message.ShouldContain(SeedDevDataKey);
    }
}
