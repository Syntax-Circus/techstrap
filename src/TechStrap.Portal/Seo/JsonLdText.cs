using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TechStrap.Portal.Seo;

/// <summary>
/// A string of structured data that cannot end the script block it is written into. <c>SyntaxCircus.Blazor.Seo</c> 0.1.4 writes its JSON-LD through a markup string with an encoder that leaves <c>&lt;</c>, <c>&gt;</c> and
/// <c>&amp;</c> alone, so an article title that contains <c>&lt;/script&gt;</c> ends the block and injects markup into the head (the PHASE-09c spike reproduced it). Escaping the text before it reaches that serialiser would
/// be escaped a second time (the JSON would read back as backslash text), so a value is wrapped in this type, whose converter writes the string itself with the strict encoder: <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, the
/// apostrophe, <c>+</c> and every non-ASCII character become <c>\uXXXX</c> escapes, which a JSON reader turns back into the original text. <see cref="Safe"/> is the only way to make one; every string of every
/// structured-data record the Portal writes is one.
/// </summary>
[JsonConverter(typeof(JsonLdTextConverter))]
public readonly record struct JsonLdText
{
    private JsonLdText(string value) => Value = value;

    /// <summary>The text as it was given (never escaped: the escaping happens when it is written).</summary>
    public string Value { get; }

    /// <summary>Wraps a string (a null is the empty string).</summary>
    public static JsonLdText Safe(string? text) => new(text ?? string.Empty);

    public override string ToString() => Value;
}

/// <summary>Writes a <see cref="JsonLdText"/> as a JSON string literal with the strict (HTML-safe, ASCII-only) encoder.</summary>
public sealed class JsonLdTextConverter : JsonConverter<JsonLdText>
{
    private static readonly JsonSerializerOptions Strict = new() { Encoder = JavaScriptEncoder.Default };

    public override JsonLdText Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => JsonLdText.Safe(reader.GetString());

    public override void Write(Utf8JsonWriter writer, JsonLdText value, JsonSerializerOptions options) => writer.WriteRawValue(JsonSerializer.Serialize(value.Value, Strict));
}
