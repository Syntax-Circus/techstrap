using Microsoft.Extensions.Options;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Hosting;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Routing;

/// <summary>
/// The links a Portal page writes, host-aware (PHASE-11e). Each method builds the <see cref="PortalRoutes"/> path and then applies one rule: the product the request arrived on (<see cref="ProductHostContext.Key"/>)
/// gets the clean path (the <c>/p/{key}</c> prefix removed, <c>/</c> for its home); another product that has a host gets an absolute https address on that host; a product with no host keeps its <c>/p/{key}</c> path.
/// A ticket path is host-neutral and never changes. <see cref="Absolute"/> is the only place an absolute address is made, and it is built from the stored host or the configured public URL, never from a request.
/// </summary>
public sealed class PortalLinks(ProductHostContext context, ProductHostMap map, IOptions<PortalOptions> options)
{
    private const string HttpsPrefix = "https://";
    private const string HttpPrefix = "http://";

    // True for links made by ForListedProduct: the host map is not asked, because the host was just read from the product list itself.
    private bool IgnoreMap { get; init; }

    /// <summary>
    /// The links of one product as the product list that was just read describes it: with a <paramref name="host"/> they are clean paths on that host, without one they are <c>/p/{key}</c> paths on the default host. The host
    /// map is never asked (its snapshot may be a minute old), so a host that was just cleared or changed is not written into a page that is then kept for a quarter of an hour (the sitemap).
    /// </summary>
    public static PortalLinks ForListedProduct(ProductHostMap map, IOptions<PortalOptions> options, string key, string? host) =>
        new(host is null ? new ProductHostContext() : new ProductHostContext { Key = key, Host = host }, map, options) { IgnoreMap = true };

    public string ProductHome(string key) => Localize(key, PortalRoutes.ProductHome(key));

    public string Contact(string key) => Localize(key, PortalRoutes.Contact(key));

    public string ContactReceived(string key) => Localize(key, PortalRoutes.ContactReceived(key));

    public string ContactReceived(string key, string reference) => Localize(key, PortalRoutes.ContactReceived(key, reference));

    public string LostLink(string key) => Localize(key, PortalRoutes.LostLink(key));

    public string LostLinkSent(string key) => Localize(key, PortalRoutes.LostLinkSent(key));

    public string KbHome(string key) => Localize(key, PortalRoutes.KbHome(key));

    public string KbCategory(string key, string category) => Localize(key, PortalRoutes.KbCategory(key, category));

    public string KbCategory(string key, string category, int page) => Localize(key, PortalRoutes.KbCategory(key, category, page));

    public string KbArticle(string key, string category, string slug) => Localize(key, PortalRoutes.KbArticle(key, category, slug));

    public string KbSearch(string key) => Localize(key, PortalRoutes.KbSearch(key));

    public string KbSearch(string key, string text, int page) => Localize(key, PortalRoutes.KbSearch(key, text, page));

    public string Suggest(string key) => Localize(key, PortalRoutes.Suggest(key));

    /// <summary>A ticket page is served on every host, so its link is relative and the same everywhere.</summary>
    public string Ticket(string token) => PortalRoutes.Ticket(token);

    public string Ticket(TicketToken token) => PortalRoutes.Ticket(token);

    public string TicketAttachment(string token, Guid attachmentId) => PortalRoutes.TicketAttachment(token, attachmentId);

    /// <summary>The absolute address of a path: on the product host the request arrived on, its stored host; elsewhere the configured public URL. An address that is absolute already is returned as it is.</summary>
    public string Absolute(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.StartsWith(HttpsPrefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(HttpPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return context.Host is { } host ? HttpsPrefix + host + path : options.Value.PublicBaseUrl + path;
    }

    /// <summary>A link to a place on the current page (<see cref="PageLinks.ToFragment"/>), written the way the visitor sees the page: on its product host the <c>/p/{key}</c> prefix of the rewritten request is left off.</summary>
    public string ToFragment(string currentUri, string fragment, bool keepQuery)
    {
        var link = PageLinks.ToFragment(currentUri, fragment, keepQuery);
        return context.Key is { } key && IsUnder(link, PortalRoutes.ProductHome(key)) ? Strip(key, link) : link;
    }

    private string Localize(string key, string prefixedPath)
    {
        if (context.Key is { } current && string.Equals(current, key, StringComparison.OrdinalIgnoreCase))
        {
            return Strip(key, prefixedPath);
        }

        return !IgnoreMap && map.TryGetHost(key, out var host) ? HttpsPrefix + host + Strip(key, prefixedPath) : prefixedPath;
    }

    private static bool IsUnder(string path, string prefix) =>
        path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && (path.Length == prefix.Length || path[prefix.Length] is '/' or '?' or '#');

    // The prefix goes: the home becomes the root, a longer path keeps everything after the prefix, and a query stays with its path.
    private static string Strip(string key, string prefixedPath)
    {
        var rest = prefixedPath[PortalRoutes.ProductHome(key).Length..];
        return rest.Length == 0 || rest[0] is '?' or '#' ? "/" + rest : rest;
    }
}
