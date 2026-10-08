using System.Text.Json;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client.Tests.Integration;

/// <summary>The served OpenAPI document and the SDK's DTOs must describe the same wire shape. No database is needed to serve the document.</summary>
public sealed class OpenApiContractTests
{
    private static async Task<JsonDocument> GetDocumentAsync(CancellationToken ct)
    {
        await using var factory = new ClientApiFactory();
        using var client = factory.CreateClient();
        return JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json", ct));
    }

    private static string[] CamelCaseConstructorParameters<T>() =>
        [.. typeof(T).GetConstructors().Single().GetParameters().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name!)).Order(StringComparer.Ordinal)];

    private static JsonElement Resolve(JsonElement root, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var reference))
        {
            return schema;
        }

        var current = root;
        foreach (var segment in reference.GetString()!.TrimStart('#', '/').Split('/'))
        {
            current = current.GetProperty(segment);
        }

        return current;
    }

    private static string[] PropertyNames(JsonElement root, JsonElement schema) =>
        [.. Resolve(root, schema).GetProperty("properties").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)];

    private static JsonElement PostOperation(JsonElement root) => root.GetProperty("paths").GetProperty("/" + IntakeRoutes.Tickets).GetProperty("post");

    [Fact(Timeout = 60_000)]
    public async Task The_intake_post_is_secured_by_the_api_key_header()
    {
        using var document = await GetDocumentAsync(Xunit.TestContext.Current.CancellationToken);
        var root = document.RootElement;

        var scheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey");

        scheme.GetProperty("type").GetString().ShouldBe("apiKey");
        scheme.GetProperty("in").GetString().ShouldBe("header");
        scheme.GetProperty("name").GetString().ShouldBe(HeaderNames.ApiKey);
        PostOperation(root).GetProperty("security").EnumerateArray().SelectMany(r => r.EnumerateObject().Select(s => s.Name)).ShouldBe(["ApiKey"]);
    }

    [Fact(Timeout = 60_000)]
    public async Task The_request_body_is_json_with_exactly_the_properties_of_the_sdk_request()
    {
        using var document = await GetDocumentAsync(Xunit.TestContext.Current.CancellationToken);
        var root = document.RootElement;

        var content = PostOperation(root).GetProperty("requestBody").GetProperty("content");

        content.EnumerateObject().Select(c => c.Name).ShouldNotContain(name => name.Contains("multipart", StringComparison.OrdinalIgnoreCase));
        PropertyNames(root, content.GetProperty("application/json").GetProperty("schema")).ShouldBe(CamelCaseConstructorParameters<SubmitTicketRequest>());
    }

    [Fact(Timeout = 60_000)]
    public async Task The_idempotency_key_is_a_header_parameter_of_the_intake_post()
    {
        using var document = await GetDocumentAsync(Xunit.TestContext.Current.CancellationToken);

        var parameters = PostOperation(document.RootElement).GetProperty("parameters").EnumerateArray()
            .Select(p => (Name: p.GetProperty("name").GetString(), In: p.GetProperty("in").GetString()));

        parameters.ShouldContain((HeaderNames.IdempotencyKey, "header"));
    }
}
