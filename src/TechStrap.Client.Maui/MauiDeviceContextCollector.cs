using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Extensions.Options;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client.Maui;

/// <summary>
/// Reads device and app context through the injected MAUI Essentials abstractions.
/// Collected: app name, version, build and package; OS platform and version; device manufacturer, model, idiom and type; culture name; time zone id; network access kind; and, only when enabled, display size, density and orientation, and battery state and level.
/// Never collected: any device, advertising or install identifier, the user's name or account, location, IP or Wi-Fi details, contacts, or files.
/// A value that cannot be read is skipped; nothing is logged.
/// </summary>
/// <param name="appInfo">The app details source.</param>
/// <param name="deviceInfo">The device details source.</param>
/// <param name="connectivity">The network state source.</param>
/// <param name="display">The display details source.</param>
/// <param name="battery">The battery state source.</param>
/// <param name="options">What to collect and the optional redaction filter.</param>
public sealed class MauiDeviceContextCollector(
    IAppInfo appInfo,
    IDeviceInfo deviceInfo,
    IConnectivity connectivity,
    IDeviceDisplay display,
    IBattery battery,
    IOptions<DeviceContextOptions> options) : IDeviceContextCollector
{
    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Collect()
    {
        var settings = options.Value;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!settings.IncludeDeviceContext)
        {
            return new ReadOnlyDictionary<string, string>(values);
        }

        var redact = settings.Redact;

        Put(values, redact, TicketMetadataKeys.AppName, () => appInfo.Name);
        Put(values, redact, TicketMetadataKeys.AppVersion, () => appInfo.VersionString);
        Put(values, redact, TicketMetadataKeys.AppBuild, () => appInfo.BuildString);
        Put(values, redact, TicketMetadataKeys.AppPackage, () => appInfo.PackageName);
        Put(values, redact, TicketMetadataKeys.OsPlatform, () => deviceInfo.Platform.ToString());
        Put(values, redact, TicketMetadataKeys.OsVersion, () => deviceInfo.VersionString);
        Put(values, redact, TicketMetadataKeys.DeviceManufacturer, () => deviceInfo.Manufacturer);
        Put(values, redact, TicketMetadataKeys.DeviceModel, () => deviceInfo.Model);
        Put(values, redact, TicketMetadataKeys.DeviceIdiom, () => deviceInfo.Idiom.ToString());
        Put(values, redact, TicketMetadataKeys.DeviceType, () => deviceInfo.DeviceType.ToString());
        Put(values, redact, TicketMetadataKeys.Locale, () => CultureInfo.CurrentCulture.Name);
        Put(values, redact, TicketMetadataKeys.TimeZone, () => TimeZoneInfo.Local.Id);
        Put(values, redact, TicketMetadataKeys.NetworkAccess, () => connectivity.NetworkAccess.ToString());

        if (settings.IncludeDisplay)
        {
            Put(values, redact, TicketMetadataKeys.DisplayWidth, () => display.MainDisplayInfo.Width.ToString("R", CultureInfo.InvariantCulture));
            Put(values, redact, TicketMetadataKeys.DisplayHeight, () => display.MainDisplayInfo.Height.ToString("R", CultureInfo.InvariantCulture));
            Put(values, redact, TicketMetadataKeys.DisplayDensity, () => display.MainDisplayInfo.Density.ToString("R", CultureInfo.InvariantCulture));
            Put(values, redact, TicketMetadataKeys.DisplayOrientation, () => display.MainDisplayInfo.Orientation.ToString());
        }

        if (settings.IncludeBattery)
        {
            Put(values, redact, TicketMetadataKeys.BatteryState, () => battery.State.ToString());
            Put(values, redact, TicketMetadataKeys.BatteryLevel, () => battery.ChargeLevel < 0 ? null : Math.Round(battery.ChargeLevel * 100).ToString("0", CultureInfo.InvariantCulture));
        }

        return new ReadOnlyDictionary<string, string>(values);
    }

    private static void Put(Dictionary<string, string> values, Func<string, string, string?>? redact, string key, Func<string?> read)
    {
        string? value;
        try
        {
            value = Clean(read());
            if (value is not null && redact is not null)
            {
                // The redactor's answer is untrusted too: it is cleaned again so it cannot exceed the limits.
                value = Clean(redact(key, value));
            }
        }
        catch (Exception)
        {
            return;
        }

        if (value is not null)
        {
            values[key] = value;
        }
    }

    private static string? Clean(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value.Length > IntakeLimits.MaxMetadataValueLength)
        {
            // A cut can leave trailing whitespace.
            value = value[..IntakeLimits.MaxMetadataValueLength].TrimEnd();
        }

        return value.Length == 0 ? null : value;
    }
}
