namespace TechStrap.Admin.Options;

/// <summary>
/// The customer portal's public address, under the same flat key the API reads (<c>TECHSTRAP_PORTAL_PUBLIC_URL</c>), so one env file can configure both hosts. It is optional here: it only powers the
/// "View on portal" link of a published article, and the link is left out while it is blank. The Admin never sends it anywhere and never calls it.
/// </summary>
public sealed class PortalUrlOptions
{
    public const string PublicUrlKey = "TECHSTRAP_PORTAL_PUBLIC_URL";

    public string? PublicUrl { get; set; }

    /// <summary>
    /// True when the value is absent or an absolute http(s) URL without a query, a fragment or user info (the rule the API applies to its own optional Admin address). A lone "?" or "#" counts as a query or a
    /// fragment: <see cref="Uri.Query"/> and <see cref="Uri.Fragment"/> keep it, and the tests pin that.
    /// </summary>
    public static bool IsValidBase(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && string.IsNullOrEmpty(uri.UserInfo)
            && uri.Query.Length == 0
            && uri.Fragment.Length == 0);

    /// <summary>
    /// The public address of a published article, <c>{portal}/p/{product key}/kb/{category slug}/{slug}</c> (the route PHASE-09 serves), or null when the portal address is blank or one of the three
    /// parts is. Each part is escaped, so a slug can never add a path segment or a query.
    /// </summary>
    public string? ArticleUrl(string? productKey, string? categorySlug, string? slug) => ArticleUrl(null, productKey, categorySlug, slug);

    /// <summary>
    /// As <see cref="ArticleUrl(string?, string?, string?)"/>, but a product with a portal host is linked on its own host, <c>https://{host}/kb/{category slug}/{slug}</c> (no product key in the path), which needs
    /// no portal address. A blank host means the default-host shape.
    /// </summary>
    public string? ArticleUrl(string? portalHost, string? productKey, string? categorySlug, string? slug)
    {
        if (!string.IsNullOrWhiteSpace(portalHost))
        {
            return string.IsNullOrWhiteSpace(categorySlug) || string.IsNullOrWhiteSpace(slug)
                ? null
                : $"https://{portalHost.Trim()}/kb/{Uri.EscapeDataString(categorySlug)}/{Uri.EscapeDataString(slug)}";
        }

        if (string.IsNullOrWhiteSpace(PublicUrl) || string.IsNullOrWhiteSpace(productKey) || string.IsNullOrWhiteSpace(categorySlug) || string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        return $"{PublicUrl.Trim().TrimEnd('/')}/p/{Uri.EscapeDataString(productKey)}/kb/{Uri.EscapeDataString(categorySlug)}/{Uri.EscapeDataString(slug)}";
    }
}
