using System.Text.Json;
using SyntaxCircus.Common;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client.Maui;

internal static class MauiMetadataMerger
{
    private static readonly JsonSerializerOptions SizeOptions = new(JsonSerializerDefaults.Web);

    public static Result<IReadOnlyDictionary<string, string>> Merge(IReadOnlyDictionary<string, string> collected, IReadOnlyDictionary<string, string>? app)
    {
        var merged = new Dictionary<string, string>(collected, StringComparer.Ordinal);
        if (app is not null)
        {
            foreach (var (key, value) in app)
            {
                // The device context is the helper's to set: an app value never overrides or adds a reserved key.
                if (key is not null && TicketMetadataKeys.All.Contains(key))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(key) || key.Length > IntakeLimits.MaxMetadataKeyLength)
                {
                    return Invalid();
                }

                var text = value?.Trim();
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                if (text.Length > IntakeLimits.MaxMetadataValueLength)
                {
                    text = text[..IntakeLimits.MaxMetadataValueLength].TrimEnd();
                }

                merged[key] = text;
            }
        }

        if (merged.Count > IntakeLimits.MaxMetadataKeys || JsonSerializer.Serialize(merged, SizeOptions).Length > IntakeLimits.MaxMetadataJsonLength)
        {
            return Invalid();
        }

        return Result<IReadOnlyDictionary<string, string>>.Success(merged);
    }

    private static Result<IReadOnlyDictionary<string, string>> Invalid() =>
        Result<IReadOnlyDictionary<string, string>>.Failure(new ResultError(TechStrapMauiErrorCodes.MetadataInvalid, TechStrapMauiMessages.MetadataInvalid, ResultErrorKind.Validation, "metadata"));
}
