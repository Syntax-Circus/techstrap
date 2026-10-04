using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TechStrap.Application.Tickets.AutoClose;
using TechStrap.Infrastructure.Tickets;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class AutoCloseOptionsTests
{
    private static IOptions<AutoCloseOptions> Resolve(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        TicketOperationsServiceCollectionExtensions.AddAutoCloseOptions(services, configuration);
        return services.BuildServiceProvider().GetRequiredService<IOptions<AutoCloseOptions>>();
    }

    [Fact]
    public void Defaults_apply_when_nothing_is_configured()
    {
        var value = Resolve([]).Value;

        value.Days.ShouldBe(7);
        value.Enabled.ShouldBeTrue();
        value.IntervalMinutes.ShouldBe(15);
        value.BatchSize.ShouldBe(50);
    }

    [Fact]
    public void The_flat_days_key_is_bound()
    {
        Resolve(new() { [AutoCloseOptions.DaysKey] = "10" }).Value.Days.ShouldBe(10);
    }

    [Fact]
    public void A_blank_days_key_keeps_the_default()
    {
        Resolve(new() { [AutoCloseOptions.DaysKey] = "" }).Value.Days.ShouldBe(7);
    }

    [Fact]
    public void The_section_values_are_bound()
    {
        var value = Resolve(new() { ["AutoClose:Enabled"] = "false", ["AutoClose:IntervalMinutes"] = "30", ["AutoClose:BatchSize"] = "100" }).Value;

        value.Enabled.ShouldBeFalse();
        value.IntervalMinutes.ShouldBe(30);
        value.BatchSize.ShouldBe(100);
    }

    [Theory]
    [InlineData(AutoCloseOptions.DaysKey, "0")]
    [InlineData(AutoCloseOptions.DaysKey, "366")]
    [InlineData(AutoCloseOptions.DaysKey, "abc")]
    [InlineData("AutoClose:IntervalMinutes", "0")]
    [InlineData("AutoClose:BatchSize", "0")]
    public void Invalid_values_fail_validation(string key, string value)
    {
        var options = Resolve(new() { [key] = value });

        Should.Throw<OptionsValidationException>(() => options.Value);
    }
}
