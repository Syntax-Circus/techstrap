using System.Text.Json;
using SyntaxCircus.Common;
using TechStrap.Client.Json;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client.Maui;

internal static class MauiMetadataMerger
{
    public static Result<IReadOnlyDictionary<string, string>> Merge(IReadOnlyDictionary<string, string> collected, IReadOnlyDictionary<string, string>? app)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);

        // A custom IDeviceContextCollector gets the same per-entry checks as the app: nothing over the limits is ever sent.
        foreach (var (key, value) in collected)
        {
            if (!TryPut(merged, key, value))
            {
                return Invalid();
            }
        }

        if (app is not null)
        {
            foreach (var (key, value) in app)
            {
                // The device context is the helper's to set: an app value never overrides or adds a reserved key.
                if (key is not null && TicketMetadataKeys.All.Contains(key))
                {
                    continue;
                }

                if (!TryPut(merged, key, value))
                {
                    return Invalid();
                }
            }
        }

        if (merged.Count > IntakeLimits.MaxMetadataKeys || JsonSerializer.Serialize(merged, TechStrapJsonContext.Default.DictionaryStringString).Length > IntakeLimits.MaxMetadataJsonLength)
        {
            return Invalid();
        }

        return Result<IReadOnlyDictionary<string, string>>.Success(merged);
    }

    private static bool TryPut(Dictionary<string, string> merged, string? key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > IntakeLimits.MaxMetadataKeyLength)
        {
            return false;
        }

        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        if (text.Length > IntakeLimits.MaxMetadataValueLength)
        {
            text = text[..IntakeLimits.MaxMetadataValueLength].TrimEnd();
        }

        merged[key] = text;
        return true;
    }

    private static Result<IReadOnlyDictionary<string, string>> Invalid() =>
        Result<IReadOnlyDictionary<string, string>>.Failure(new ResultError(TechStrapMauiErrorCodes.MetadataInvalid, TechStrapMauiMessages.MetadataInvalid, ResultErrorKind.Validation, "metadata"));
}
