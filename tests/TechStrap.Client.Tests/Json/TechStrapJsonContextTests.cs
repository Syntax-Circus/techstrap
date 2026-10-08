using System.Text.Json;
using TechStrap.Client.Json;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client.Tests.Json;

public sealed class TechStrapJsonContextTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Context_serializes_a_request_exactly_like_web_defaults()
    {
        var request = new SubmitTicketRequest(
            "a@example.com",
            "Ann",
            "Subject",
            "Body",
            null,
            new Dictionary<string, string> { ["Key"] = "<a> & \"q\" café" });

        var generated = JsonSerializer.Serialize(request, TechStrapJsonContext.Default.SubmitTicketRequest);

        generated.ShouldBe(JsonSerializer.Serialize(request, Web));
    }

    [Theory]
    [InlineData("""{"ticketNumber":"T-1","viewUrl":null,"warnings":["external-user-ref-ignored"]}""")]
    [InlineData("""{"TicketNumber":"T-1","ViewUrl":null,"Warnings":["external-user-ref-ignored"]}""")]
    public void Context_deserializes_a_response_case_insensitively(string json)
    {
        var response = JsonSerializer.Deserialize(json, TechStrapJsonContext.Default.SubmitTicketResponse);

        response.ShouldNotBeNull();
        response.TicketNumber.ShouldBe("T-1");
        response.ViewUrl.ShouldBeNull();
        response.Warnings.ShouldBe(["external-user-ref-ignored"]);
    }

    [Fact]
    public void Context_measures_a_dictionary_exactly_like_web_defaults()
    {
        var dictionary = new Dictionary<string, string> { ["A<b"] = "x&y \"z\" café", ["Second"] = "v" };

        var generated = JsonSerializer.Serialize(dictionary, TechStrapJsonContext.Default.DictionaryStringString);

        generated.ShouldBe(JsonSerializer.Serialize(dictionary, Web));
    }
}
