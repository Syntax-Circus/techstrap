using System.Text.Encodings.Web;
using System.Text.Json;
using TechStrap.Portal.Seo;

namespace TechStrap.Portal.Tests.Seo;

/// <summary>
/// PHASE-09c Review Focus 1 (XSS in SEO): the text of a structured-data value cannot end its script block, and it reads back as exactly what was written. The serializer options below are the ones
/// <c>SyntaxCircus.Blazor.Seo</c> 0.1.4's <c>JsonLd</c> component uses (camel case, nulls left out, the encoder that leaves <c>&lt;</c>, <c>&gt;</c> and <c>&amp;</c> alone), so the test sees what the page would write.
/// </summary>
public sealed class JsonLdTextTests
{
    private sealed record Holder(JsonLdText Name);

    private static readonly JsonSerializerOptions PackageOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private static string Json(string? text) => JsonSerializer.Serialize(new Holder(JsonLdText.Safe(text)), PackageOptions);

    public static TheoryData<string> Hostile() => new()
    {
        "</script><img src=x onerror=alert(1)>",
        "</SCRIPT >",
        "<!-- comment -->",
        "<script>alert(1)</script>",
        "a & b &amp; c",
        "she said \"hi\" and \\ left",
        "line one\nline two\ttabbed\r\n",
        "it's 1+1",
        "caf" + char.ConvertFromUtf32(0xE9),
        "emoji " + char.ConvertFromUtf32(0x1F600) + " end",
        "line" + char.ConvertFromUtf32(0x2028) + "separator" + char.ConvertFromUtf32(0x2029) + "end",
        "nul" + char.ConvertFromUtf32(0) + "char",
        "a lone surrogate " + (char)0xD800 + " here",
        "",
        "plain text",
    };

    [Theory]
    [MemberData(nameof(Hostile))]
    public void The_json_has_no_character_that_could_end_a_script_block_or_start_markup(string text)
    {
        var json = Json(text);

        json.ShouldNotContain("<");
        json.ShouldNotContain(">");
        json.ShouldNotContain("&");
        json.ShouldNotContain("'");
        json.ShouldNotContain("+");
        json.ShouldAllBe(character => character >= 0x20 && character < 0x7F, "every character is printable ASCII: the rest are \\u escapes");
    }

    [Theory]
    [MemberData(nameof(Hostile))]
    public void The_json_reads_back_as_exactly_the_text_that_was_written(string text)
    {
        using var document = JsonDocument.Parse(Json(text));

        // A lone surrogate is not valid text; the strict encoder writes the replacement character for it, which is what a reader gets back.
        var expected = text.Contains((char)0xD800) ? text.Replace((char)0xD800, (char)0xFFFD) : text;
        document.RootElement.GetProperty("name").GetString().ShouldBe(expected);
    }

    [Fact]
    public void A_plain_string_in_the_same_serialiser_is_not_escaped_which_is_why_the_wrapper_exists()
    {
        var plain = JsonSerializer.Serialize(new { name = "</script><b>" }, PackageOptions);

        plain.ShouldContain("</script><b>", Case.Sensitive);
        Json("</script><b>").ShouldNotContain("</script>");
        Json("</script><b>").ShouldContain("\\u003C/script\\u003E\\u003Cb\\u003E");
    }

    [Fact]
    public void A_null_is_the_empty_string_and_the_value_is_kept_as_given()
    {
        JsonLdText.Safe(null).Value.ShouldBe(string.Empty);
        JsonLdText.Safe("a < b").Value.ShouldBe("a < b");
        JsonLdText.Safe("a < b").ToString().ShouldBe("a < b");
        Json(null).ShouldBe("{\"name\":\"\"}");
    }

    [Fact]
    public void The_converter_also_reads_a_string_back()
    {
        var holder = JsonSerializer.Deserialize<Holder>("{\"name\":\"a \\u003C b\"}", PackageOptions);

        holder!.Name.Value.ShouldBe("a < b");
    }
}
