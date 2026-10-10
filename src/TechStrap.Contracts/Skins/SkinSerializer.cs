using System.Text.Json;
using System.Text.Json.Serialization;

namespace TechStrap.Contracts.Skins;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    WriteIndented = false)]
[JsonSerializable(typeof(ProductSkin))]
internal sealed partial class SkinJsonContext : JsonSerializerContext;

/// <summary>The one JSON form of a skin: compact camelCase, nulls omitted, unknown members rejected.</summary>
public static class SkinSerializer
{
    /// <summary>The JSON for a skin, or null for a null or empty skin.</summary>
    public static string? Serialize(ProductSkin? skin) =>
        skin is null || skin.IsEmpty ? null : JsonSerializer.Serialize(skin, SkinJsonContext.Default.ProductSkin);

    /// <summary>
    /// Strict parse. Null or blank JSON succeeds with a null skin; an unknown member, malformed JSON or text longer than
    /// <see cref="SkinRules.MaxJsonLength"/> fails.
    /// </summary>
    public static bool TryDeserialize(string? json, out ProductSkin? skin)
    {
        skin = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        if (json.Length > SkinRules.MaxJsonLength)
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize(json, SkinJsonContext.Default.ProductSkin);
            skin = parsed is null || parsed.IsEmpty ? null : parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
