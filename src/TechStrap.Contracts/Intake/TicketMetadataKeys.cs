using System.Collections.Frozen;

namespace TechStrap.Contracts.Intake;

/// <summary>
/// Metadata keys the MAUI helper collects (D-048). Public keys' metadata is stored but flagged untrusted (D-001).
/// </summary>
public static class TicketMetadataKeys
{
    public const string AppName = "app.name";
    public const string AppVersion = "app.version";
    public const string AppBuild = "app.build";
    public const string AppPackage = "app.package";
    public const string OsPlatform = "os.platform";
    public const string OsVersion = "os.version";
    public const string DeviceManufacturer = "device.manufacturer";
    public const string DeviceModel = "device.model";
    public const string DeviceIdiom = "device.idiom";
    public const string DeviceType = "device.type";
    public const string Locale = "locale";
    public const string TimeZone = "timezone";
    public const string NetworkAccess = "network.access";
    public const string DisplayWidth = "display.width";
    public const string DisplayHeight = "display.height";
    public const string DisplayDensity = "display.density";
    public const string DisplayOrientation = "display.orientation";
    public const string BatteryState = "battery.state";
    public const string BatteryLevel = "battery.level";

    /// <summary>The thirteen keys the helper sends by default.</summary>
    public static IReadOnlySet<string> Defaults { get; } = new[]
    {
        AppName, AppVersion, AppBuild, AppPackage,
        OsPlatform, OsVersion,
        DeviceManufacturer, DeviceModel, DeviceIdiom, DeviceType,
        Locale, TimeZone, NetworkAccess,
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Every key the helper may send: the defaults plus the display and battery keys.</summary>
    public static IReadOnlySet<string> All { get; } = new[]
    {
        AppName, AppVersion, AppBuild, AppPackage,
        OsPlatform, OsVersion,
        DeviceManufacturer, DeviceModel, DeviceIdiom, DeviceType,
        Locale, TimeZone, NetworkAccess,
        DisplayWidth, DisplayHeight, DisplayDensity, DisplayOrientation,
        BatteryState, BatteryLevel,
    }.ToFrozenSet(StringComparer.Ordinal);
}
