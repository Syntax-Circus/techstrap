using System.Text.Json;
using TechStrap.Contracts.Webhooks;

namespace TechStrap.Infrastructure.Webhooks;

internal static class WebhookJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    internal static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    internal static List<WebhookEndpointRequest> Endpoints(string json) => JsonSerializer.Deserialize<List<WebhookEndpointRequest>>(json, Options) ?? [];
    internal static WebhookEndpointDto Redact(WebhookEndpointRequest endpoint) => new(endpoint with { Url = null }, new Uri(endpoint.Url!).Host + "/…");
}
