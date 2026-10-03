namespace TechStrap.Application.Intake;

/// <summary>The customer portal base URL (TECHSTRAP_PORTAL_PUBLIC_URL) used to build ticket links.</summary>
public sealed class PortalLinkOptions
{
    public const string PublicUrlKey = "TECHSTRAP_PORTAL_PUBLIC_URL";

    public string PublicUrl { get; set; } = string.Empty;

    public string TicketLink(string token) => $"{PublicUrl.TrimEnd('/')}/t/{token}";
}
