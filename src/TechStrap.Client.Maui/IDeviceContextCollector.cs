namespace TechStrap.Client.Maui;

/// <summary>Collects the device and app context the MAUI helper attaches to a ticket as metadata.</summary>
public interface IDeviceContextCollector
{
    /// <summary>
    /// Reads the current device context. Never throws: a value that cannot be read is left out. Every key is one of <see cref="TechStrap.Contracts.Intake.TicketMetadataKeys"/>.
    /// </summary>
    /// <returns>The metadata to send, empty when device context is turned off.</returns>
    IReadOnlyDictionary<string, string> Collect();
}
