namespace TechStrap.Application.Tickets.Notifications;

/// <summary>The Admin app base URL (TECHSTRAP_ADMIN_PUBLIC_URL) used to link assignment emails; optional.</summary>
public sealed class AdminLinkOptions
{
    public const string PublicUrlKey = "TECHSTRAP_ADMIN_PUBLIC_URL";

    public string? PublicUrl { get; set; }

    public string? TicketLink(string ticketNumber) =>
        string.IsNullOrWhiteSpace(PublicUrl) ? null : $"{PublicUrl.TrimEnd('/')}/tickets/{Uri.EscapeDataString(ticketNumber)}";
}
