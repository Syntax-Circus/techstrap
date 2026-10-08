using System.Reflection;
using System.Text.RegularExpressions;
using TechStrap.Contracts.Intake;

namespace TechStrap.Application.Tests.Intake;

public sealed class TicketMetadataKeysTests
{
    private static readonly Regex KeyShape = new(@"^[a-z]+(\.[a-z]+)*$", RegexOptions.CultureInvariant);

    [Fact]
    public void All_contains_every_constant()
    {
        var constants = typeof(TicketMetadataKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet();

        constants.Count.ShouldBe(19);
        TicketMetadataKeys.All.SetEquals(constants).ShouldBeTrue();
    }

    [Fact]
    public void Defaults_are_the_thirteen_spec_keys()
    {
        TicketMetadataKeys.Defaults.SetEquals(new[]
        {
            "app.name", "app.version", "app.build", "app.package",
            "os.platform", "os.version",
            "device.manufacturer", "device.model", "device.idiom", "device.type",
            "locale", "timezone", "network.access",
        }).ShouldBeTrue();
    }

    [Fact]
    public void Every_key_fits_the_server_limits()
    {
        foreach (var key in TicketMetadataKeys.All)
        {
            key.ShouldNotBeNullOrWhiteSpace();
            (key.Length <= IntakeLimits.MaxMetadataKeyLength).ShouldBeTrue($"'{key}' is longer than {IntakeLimits.MaxMetadataKeyLength}");
            KeyShape.IsMatch(key).ShouldBeTrue($"'{key}' does not match the key shape");
        }
    }

    [Fact]
    public void Extras_are_the_display_and_battery_keys()
    {
        TicketMetadataKeys.All.Except(TicketMetadataKeys.Defaults).ToHashSet().SetEquals(new[]
        {
            "display.width", "display.height", "display.density", "display.orientation",
            "battery.state", "battery.level",
        }).ShouldBeTrue();
    }
}
