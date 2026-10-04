using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TechStrap.Application.Email;
using TechStrap.Infrastructure.Email;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class OutboxRetentionOptionsTests
{
    private static IOptions<OutboxRetentionOptions> Resolve(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddTechStrapOutboxRetention(configuration);
        return services.BuildServiceProvider().GetRequiredService<IOptions<OutboxRetentionOptions>>();
    }

    [Fact]
    public void Defaults_apply_when_nothing_is_configured()
    {
        var value = Resolve([]).Value;

        value.Enabled.ShouldBeTrue();
        value.Days.ShouldBe(90);
        value.IntervalMinutes.ShouldBe(60);
        value.BatchSize.ShouldBe(500);
    }

    [Fact]
    public void Section_values_are_bound()
    {
        var value = Resolve(new()
        {
            ["OutboxRetention:Enabled"] = "false",
            ["OutboxRetention:Days"] = "30",
            ["OutboxRetention:IntervalMinutes"] = "10",
            ["OutboxRetention:BatchSize"] = "100",
        }).Value;

        value.Enabled.ShouldBeFalse();
        value.Days.ShouldBe(30);
        value.IntervalMinutes.ShouldBe(10);
        value.BatchSize.ShouldBe(100);
    }

    [Theory]
    [InlineData("OutboxRetention:Enabled", "maybe")]
    [InlineData("OutboxRetention:Enabled", "0")]
    [InlineData("OutboxRetention:IntervalMinutes", "abc")]
    [InlineData("OutboxRetention:BatchSize", "abc")]
    [InlineData("OutboxRetention:Days", "0")]
    [InlineData("OutboxRetention:Days", "3651")]
    [InlineData("OutboxRetention:Days", "abc")]
    [InlineData("OutboxRetention:IntervalMinutes", "0")]
    [InlineData("OutboxRetention:IntervalMinutes", "1441")]
    [InlineData("OutboxRetention:BatchSize", "0")]
    [InlineData("OutboxRetention:BatchSize", "501")]
    public void Invalid_values_fail_validation(string key, string value)
    {
        var options = Resolve(new() { [key] = value });

        Should.Throw<OptionsValidationException>(() => options.Value);
    }
}
