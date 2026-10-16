using System.Net;
using System.Net.Sockets;
using TechStrap.Contracts.Webhooks;

namespace TechStrap.Application.Webhooks;

public static class WebhookPolicy
{
    public const int MaxEndpoints = 20;
    public const int MaxUrlLength = 2048;
    public const int MaxNameLength = 100;
    public const string GlobalMode = "Replace";
    public static readonly Guid GlobalScope = new("00000000-0000-0000-0000-000000000002");
    public static readonly string[] TicketStatuses = ["New", "Open", "Pending", "Solved", "Closed"];
    public static readonly string[] FeatureStatuses = ["UnderConsideration", "Planned", "InProgress", "Shipped", "NotPlanned"];

    public static bool Matches(WebhookEndpointRequest endpoint, string eventType, string? ticketType, string status) => endpoint.Enabled && eventType switch
    {
        "ticket.created" => endpoint.TicketCreated && endpoint.TicketTypes.Contains(ticketType),
        "ticket.status_changed" => endpoint.TicketTypes.Contains(ticketType) && endpoint.TicketStatuses.Contains(status),
        "feature.created" => endpoint.FeatureCreated,
        "feature.status_changed" => endpoint.FeatureStatuses.Contains(status),
        _ => false,
    };

    public static bool ValidUrl(string? url) => url is { Length: > 0 and <= MaxUrlLength }
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 && !url.Contains('#')
        && uri.Host.Length > 0 && uri.Host.Contains('.') && !uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        && (!IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) || IsPublicAddress(ip));

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return (bytes[0] & 0xe0) == 0x20 && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8);
        return bytes[0] != 0 && bytes[0] != 10 && bytes[0] != 127 && bytes[0] < 224
            && !(bytes[0] == 169 && bytes[1] == 254) && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            && !(bytes[0] == 192 && bytes[1] == 168) && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
            && !(bytes[0] == 198 && bytes[1] is 18 or 19)
            && !(bytes[0] == 192 && bytes[1] == 0) && !(bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
            && !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
    }
}
