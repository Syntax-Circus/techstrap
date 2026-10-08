namespace TechStrap.Application.Intake;

/// <summary>The customer portal base URL (TECHSTRAP_PORTAL_PUBLIC_URL) used to build ticket links. A product with a portal host links on that host instead (D-050).</summary>
public sealed class PortalLinkOptions
{
    public const string PublicUrlKey = "TECHSTRAP_PORTAL_PUBLIC_URL";

    public string PublicUrl { get; set; } = string.Empty;

    /// <summary>The ticket link on the default host (callers without a product): <c>{url}/t/{token}</c>.</summary>
    public string TicketLink(string token) => $"{PublicUrl.TrimEnd('/')}/t/{token}";

    /// <summary>The ticket link of a product: on its host when <paramref name="portalHost"/> is set, else on the default host.</summary>
    public string TicketLink(string? portalHost, string token) => $"{BaseFor(portalHost)}/t/{token}";

    /// <summary>The portal page of a published KB article on the default host: <c>{url}/p/{product key}/kb/{category slug}/{article slug}</c> (PHASE-09 route, D-044).</summary>
    public string ArticleLink(string productKey, string categorySlug, string articleSlug) =>
        PublicUrl.TrimEnd('/') + ArticlePath(productKey, categorySlug, articleSlug);

    /// <summary>The article link of a product: <c>https://{host}/kb/{category}/{slug}</c> on its host, else the default-host shape.</summary>
    public string ArticleLink(string? portalHost, string productKey, string categorySlug, string articleSlug) =>
        string.IsNullOrWhiteSpace(portalHost)
            ? ArticleLink(productKey, categorySlug, articleSlug)
            : ProductHostBase(portalHost) + ArticlePathOnHost(categorySlug, articleSlug);

    /// <summary>The part of <see cref="ArticleLink(string, string, string)"/> after the base URL. The Worker compares it at send time without needing the portal URL.</summary>
    public static string ArticlePath(string productKey, string categorySlug, string articleSlug) =>
        $"/p/{Uri.EscapeDataString(productKey)}/kb/{Uri.EscapeDataString(categorySlug)}/{Uri.EscapeDataString(articleSlug)}";

    /// <summary>The article path on a product host, which has no <c>/p/{key}</c> prefix.</summary>
    public static string ArticlePathOnHost(string categorySlug, string articleSlug) =>
        $"/kb/{Uri.EscapeDataString(categorySlug)}/{Uri.EscapeDataString(articleSlug)}";

    /// <summary>True when <paramref name="url"/> is this article's link in either shape (default host: ends with <see cref="ArticlePath"/>; product host: <c>https://{host}</c> plus <see cref="ArticlePathOnHost"/>), ordinal.</summary>
    public static bool IsArticleLink(string url, string productKey, string categorySlug, string articleSlug) =>
        url.EndsWith(ArticlePath(productKey, categorySlug, articleSlug), StringComparison.Ordinal)
        || IsOnProductHost(url, ArticlePathOnHost(categorySlug, articleSlug));

    // The host shape is a suffix of another product's default-host shape (".../p/other/kb/a/b"), so what precedes it must be a bare "https://host".
    private static bool IsOnProductHost(string url, string path)
    {
        const string scheme = "https://";
        return url.EndsWith(path, StringComparison.Ordinal)
            && url.StartsWith(scheme, StringComparison.Ordinal)
            && url.IndexOf('/', scheme.Length) == url.Length - path.Length;
    }

    /// <summary>The base URL of a product host.</summary>
    public static string ProductHostBase(string host) => $"https://{host}";

    private string BaseFor(string? portalHost) =>
        string.IsNullOrWhiteSpace(portalHost) ? PublicUrl.TrimEnd('/') : ProductHostBase(portalHost);
}
