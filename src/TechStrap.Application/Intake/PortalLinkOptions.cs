namespace TechStrap.Application.Intake;

/// <summary>The customer portal base URL (TECHSTRAP_PORTAL_PUBLIC_URL) used to build ticket links.</summary>
public sealed class PortalLinkOptions
{
    public const string PublicUrlKey = "TECHSTRAP_PORTAL_PUBLIC_URL";

    public string PublicUrl { get; set; } = string.Empty;

    public string TicketLink(string token) => $"{PublicUrl.TrimEnd('/')}/t/{token}";

    /// <summary>The portal page of a published KB article: <c>{url}/p/{product key}/kb/{category slug}/{article slug}</c> (PHASE-09 route, D-044).</summary>
    public string ArticleLink(string productKey, string categorySlug, string articleSlug) =>
        PublicUrl.TrimEnd('/') + ArticlePath(productKey, categorySlug, articleSlug);

    /// <summary>The part of <see cref="ArticleLink"/> after the base URL. The Worker compares it at send time without needing the portal URL.</summary>
    public static string ArticlePath(string productKey, string categorySlug, string articleSlug) =>
        $"/p/{Uri.EscapeDataString(productKey)}/kb/{Uri.EscapeDataString(categorySlug)}/{Uri.EscapeDataString(articleSlug)}";
}
