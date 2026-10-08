using NSubstitute;
using System.Globalization;
using TechStrap.Client.Maui.Tests.Infrastructure;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client.Maui.Tests.Collector;

public sealed class MauiDeviceContextCollectorTests
{
    [Fact]
    public void Default_collection_emits_exactly_the_thirteen_default_keys()
    {
        var result = EssentialsFakes.Default().Collector().Collect();

        result.Keys.ToHashSet().SetEquals(TicketMetadataKeys.Defaults).ShouldBeTrue();
        result[TicketMetadataKeys.AppName].ShouldBe("Puppies Plus");
        result[TicketMetadataKeys.OsPlatform].ShouldBe("Android");
        result[TicketMetadataKeys.DeviceType].ShouldBe("Physical");
        result[TicketMetadataKeys.NetworkAccess].ShouldBe("Internet");
        result[TicketMetadataKeys.Locale].ShouldNotBeNullOrWhiteSpace();
        result[TicketMetadataKeys.TimeZone].ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Display_and_battery_are_collected_only_when_enabled()
    {
        var fakes = EssentialsFakes.Default();

        var displayOnly = fakes.Collector(new DeviceContextOptions { IncludeDisplay = true }).Collect();
        displayOnly[TicketMetadataKeys.DisplayWidth].ShouldBe("1080");
        displayOnly[TicketMetadataKeys.DisplayHeight].ShouldBe("2400");
        displayOnly[TicketMetadataKeys.DisplayDensity].ShouldBe("2.625");
        displayOnly[TicketMetadataKeys.DisplayOrientation].ShouldBe("Portrait");
        displayOnly.ContainsKey(TicketMetadataKeys.BatteryState).ShouldBeFalse();
        displayOnly.ContainsKey(TicketMetadataKeys.BatteryLevel).ShouldBeFalse();

        var batteryOnly = fakes.Collector(new DeviceContextOptions { IncludeBattery = true }).Collect();
        batteryOnly[TicketMetadataKeys.BatteryState].ShouldBe("Charging");
        batteryOnly[TicketMetadataKeys.BatteryLevel].ShouldBe("80");
        batteryOnly.ContainsKey(TicketMetadataKeys.DisplayWidth).ShouldBeFalse();

        var neither = fakes.Collector().Collect();
        neither.ContainsKey(TicketMetadataKeys.DisplayWidth).ShouldBeFalse();
        neither.ContainsKey(TicketMetadataKeys.BatteryLevel).ShouldBeFalse();
    }

    [Fact]
    public void Disabled_device_context_collects_nothing()
    {
        var result = EssentialsFakes.Default()
            .Collector(new DeviceContextOptions { IncludeDeviceContext = false, IncludeDisplay = true, IncludeBattery = true })
            .Collect();

        result.ShouldBeEmpty();
    }

    [Fact]
    public void Long_values_are_truncated_to_the_metadata_value_limit()
    {
        var fakes = EssentialsFakes.Default();
        fakes.DeviceInfo.Model.Returns(new string('m', 1_500));

        var result = fakes.Collector().Collect();

        result[TicketMetadataKeys.DeviceModel].Length.ShouldBe(IntakeLimits.MaxMetadataValueLength);
    }

    [Fact]
    public void Blank_values_are_dropped()
    {
        var fakes = EssentialsFakes.Default();
        fakes.DeviceInfo.Manufacturer.Returns("");
        fakes.DeviceInfo.Model.Returns("   ");

        var result = fakes.Collector().Collect();

        result.ContainsKey(TicketMetadataKeys.DeviceManufacturer).ShouldBeFalse();
        result.ContainsKey(TicketMetadataKeys.DeviceModel).ShouldBeFalse();
        result.Count.ShouldBe(TicketMetadataKeys.Defaults.Count - 2);
    }

    [Fact]
    public void Values_are_trimmed_before_truncation()
    {
        var fakes = EssentialsFakes.Default();
        fakes.DeviceInfo.Model.Returns("  Pixel 8  ");

        fakes.Collector().Collect()[TicketMetadataKeys.DeviceModel].ShouldBe("Pixel 8");
    }

    [Fact]
    public void Redaction_is_applied_after_truncation_and_null_drops_the_field()
    {
        var fakes = EssentialsFakes.Default();
        fakes.DeviceInfo.Model.Returns(new string('m', 1_500));
        string? received = null;

        var result = fakes.Collector(new DeviceContextOptions
        {
            Redact = (key, value) =>
            {
                if (key == TicketMetadataKeys.DeviceModel)
                {
                    received = value;
                    return "redacted";
                }

                return key == TicketMetadataKeys.AppPackage ? null : value;
            },
        }).Collect();

        received.ShouldNotBeNull();
        received.Length.ShouldBe(IntakeLimits.MaxMetadataValueLength);
        result[TicketMetadataKeys.DeviceModel].ShouldBe("redacted");
        result.ContainsKey(TicketMetadataKeys.AppPackage).ShouldBeFalse();
        result[TicketMetadataKeys.AppName].ShouldBe("Puppies Plus");
        result.Count.ShouldBe(TicketMetadataKeys.Defaults.Count - 1);
    }

    [Fact]
    public void A_throwing_accessor_skips_its_field_and_the_rest_is_collected()
    {
        var fakes = EssentialsFakes.Default();
        fakes.DeviceInfo.Model.Returns<string>(_ => throw new NotSupportedException("reference assembly"));
        fakes.AppInfo.BuildString.Returns<string>(_ => throw new InvalidOperationException("boom"));

        var result = fakes.Collector().Collect();

        result.ContainsKey(TicketMetadataKeys.DeviceModel).ShouldBeFalse();
        result.ContainsKey(TicketMetadataKeys.AppBuild).ShouldBeFalse();
        result.Count.ShouldBe(TicketMetadataKeys.Defaults.Count - 2);
        result[TicketMetadataKeys.AppName].ShouldBe("Puppies Plus");
    }

    [Fact]
    public void No_key_outside_TicketMetadataKeys_is_ever_emitted()
    {
        var result = EssentialsFakes.Default()
            .Collector(new DeviceContextOptions { IncludeDisplay = true, IncludeBattery = true })
            .Collect();

        result.Keys.ToHashSet().IsSubsetOf(TicketMetadataKeys.All).ShouldBeTrue();
        result.Count.ShouldBe(TicketMetadataKeys.All.Count);
    }

    [Fact]
    public void Values_use_the_invariant_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var result = EssentialsFakes.Default()
                .Collector(new DeviceContextOptions { IncludeDisplay = true })
                .Collect();

            result[TicketMetadataKeys.DisplayDensity].ShouldBe("2.625");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
