using System.Collections.Frozen;

namespace TechStrap.Contracts.Intake;

/// <summary>
/// Metadata keys the MAUI helper collects (D-048). Public keys' metadata is stored but flagged untrusted (D-001).
/// </summary>
public static class TicketMetadataKeys
{
    /// <summary>The application's display name. Metadata key <c>app.name</c>.</summary>
    public const string AppName = "app.name";
    /// <summary>The application's version string. Metadata key <c>app.version</c>.</summary>
    public const string AppVersion = "app.version";
    /// <summary>The application's build number. Metadata key <c>app.build</c>.</summary>
    public const string AppBuild = "app.build";
    /// <summary>The application's package or bundle identifier. Metadata key <c>app.package</c>.</summary>
    public const string AppPackage = "app.package";
    /// <summary>The operating system platform, for example Android or iOS. Metadata key <c>os.platform</c>.</summary>
    public const string OsPlatform = "os.platform";
    /// <summary>The operating system version. Metadata key <c>os.version</c>.</summary>
    public const string OsVersion = "os.version";
    /// <summary>The device manufacturer. Metadata key <c>device.manufacturer</c>.</summary>
    public const string DeviceManufacturer = "device.manufacturer";
    /// <summary>The device model. Metadata key <c>device.model</c>.</summary>
    public const string DeviceModel = "device.model";
    /// <summary>The device form factor, for example phone, tablet or desktop. Metadata key <c>device.idiom</c>.</summary>
    public const string DeviceIdiom = "device.idiom";
    /// <summary>Whether the app runs on a physical device or a virtual one. Metadata key <c>device.type</c>.</summary>
    public const string DeviceType = "device.type";
    /// <summary>The app's current culture name (<c>CultureInfo.CurrentCulture.Name</c>, for example en-GB). Metadata key <c>locale</c>.</summary>
    public const string Locale = "locale";
    /// <summary>The local time zone id (<c>TimeZoneInfo.Local.Id</c>). Metadata key <c>timezone</c>.</summary>
    public const string TimeZone = "timezone";
    /// <summary>The device's network connectivity level at the time of the report. Metadata key <c>network.access</c>.</summary>
    public const string NetworkAccess = "network.access";
    /// <summary>The display width in pixels. Metadata key <c>display.width</c>. Not among the default keys.</summary>
    public const string DisplayWidth = "display.width";
    /// <summary>The display height in pixels. Metadata key <c>display.height</c>. Not among the default keys.</summary>
    public const string DisplayHeight = "display.height";
    /// <summary>The display density (scale factor). Metadata key <c>display.density</c>. Not among the default keys.</summary>
    public const string DisplayDensity = "display.density";
    /// <summary>The display orientation. Metadata key <c>display.orientation</c>. Not among the default keys.</summary>
    public const string DisplayOrientation = "display.orientation";
    /// <summary>The battery charging state. Metadata key <c>battery.state</c>. Not among the default keys.</summary>
    public const string BatteryState = "battery.state";
    /// <summary>The battery charge level, as a whole-number percent (0-100); omitted when the level is unknown. Metadata key <c>battery.level</c>. Not among the default keys.</summary>
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
