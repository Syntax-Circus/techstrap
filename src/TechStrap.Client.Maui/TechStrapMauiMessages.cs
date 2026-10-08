using System.Globalization;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client.Maui;

internal static class TechStrapMauiMessages
{
    public static readonly string MetadataInvalid = string.Create(
        CultureInfo.InvariantCulture,
        $"The metadata exceeds the server's limits (at most {IntakeLimits.MaxMetadataKeys} keys of {IntakeLimits.MaxMetadataKeyLength} characters, values of {IntakeLimits.MaxMetadataValueLength} characters, {IntakeLimits.MaxMetadataJsonLength} characters in all).");
}
