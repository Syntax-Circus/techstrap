namespace TechStrap.Client.Maui;

/// <summary>
/// What the MAUI helper captures from the device and attaches to a ticket as metadata. Nothing identifies the device or the user; see <see cref="TechStrap.Contracts.Intake.TicketMetadataKeys"/>.
/// </summary>
public sealed class DeviceContextOptions
{
    /// <summary>Whether to attach the app, platform and connectivity context at all. Defaults to <see langword="true"/>. Turn it off to send only what the caller supplies.</summary>
    public bool IncludeDeviceContext { get; set; } = true;

    /// <summary>Whether to also attach display details (size, density, orientation). Defaults to <see langword="false"/>. No identifier is read.</summary>
    public bool IncludeDisplay { get; set; }

    /// <summary>Whether to also attach battery level and power state. Defaults to <see langword="false"/>. No identifier is read.</summary>
    public bool IncludeBattery { get; set; }

    /// <summary>
    /// Optional last-chance filter, called with each metadata key and its value just before they are sent. Return the value to send (possibly changed), or <see langword="null"/> to drop the entry. Defaults to <see langword="null"/> (no filtering).
    /// </summary>
    public Func<string, string, string?>? Redact { get; set; }
}