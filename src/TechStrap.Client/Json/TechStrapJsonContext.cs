using System.Text.Json;
using System.Text.Json.Serialization;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client.Json;

/// <summary>
/// Source-generated JSON metadata for the submit call, so serialization needs no reflection and works in AOT and fully trimmed apps.
/// The options mirror <see cref="JsonSerializerDefaults.Web"/> (camelCase names, case-insensitive reading, numbers readable from strings), which is what the server uses.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(SubmitTicketRequest))]
[JsonSerializable(typeof(SubmitTicketResponse))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public sealed partial class TechStrapJsonContext : JsonSerializerContext;
