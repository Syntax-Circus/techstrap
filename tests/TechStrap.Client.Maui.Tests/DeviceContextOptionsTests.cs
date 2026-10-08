namespace TechStrap.Client.Maui.Tests;

public sealed class DeviceContextOptionsTests
{
    [Fact]
    public void Defaults_capture_the_device_context_only()
    {
        var options = new DeviceContextOptions();

        options.IncludeDeviceContext.ShouldBeTrue();
        options.IncludeDisplay.ShouldBeFalse();
        options.IncludeBattery.ShouldBeFalse();
        options.Redact.ShouldBeNull();
    }

    [Fact]
    public void A_redact_delegate_round_trips()
    {
        Func<string, string, string?> redact = (key, value) => key == "secret" ? null : value;

        var options = new DeviceContextOptions { Redact = redact };

        options.Redact.ShouldBeSameAs(redact);
        options.Redact!("secret", "x").ShouldBeNull();
        options.Redact("app.version", "1.0").ShouldBe("1.0");
    }
}