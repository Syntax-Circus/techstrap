namespace TechStrap.Application.Tickets.Notifications;

/// <summary>The Admin app base URL (TECHSTRAP_ADMIN_PUBLIC_URL) used to link assignment emails; optional.</summary>
public sealed class AdminLinkOptions
{
    public const string PublicUrlKey = "TECHSTRAP_ADMIN_PUBLIC_URL";

    public string? PublicUrl { get; set; }

    /// <summary>True when the value is absent or an absolute http(s) URL without a query or fragment.</summary>
    public static bool IsValidBase(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && uri.Query.Length == 0
            && uri.Fragment.Length == 0);

    public string? TicketLink(string ticketNumber) =>
        string.IsNullOrWhiteSpace(PublicUrl) ? null : $"{PublicUrl.TrimEnd('/')}/tickets/{Uri.EscapeDataString(ticketNumber)}";
}
