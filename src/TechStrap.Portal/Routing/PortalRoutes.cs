using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Routing;

/// <summary>
/// Every route the Portal serves, once. A page declares its route with <c>@attribute [Route(PortalRoutes.XTemplate)]</c> and every link, redirect and form action is built by the matching
/// builder, so a route changes in one place (RouteLiteralTests fails on a second spelling). The product key and the ticket token are always escaped by the builders, so a value can never add
/// a path segment, a query or a fragment. The routes of PHASE-09b and 09c are here already, so the three pull requests share one list.
/// </summary>
public static class PortalRoutes
{
    public const string ProductPrefix = "/p";

    /// <summary>The ticket pages. The Portal's header rules and its robots.txt exclusion apply to everything under it.</summary>
    public const string TicketPrefix = "/t";

    // The last segments of the product's form pages: the header rule that keeps them out of the index and out of every cache matches on these (PortalHeaderRules.IsFormPagePath).
    public const string ContactSegment = "contact";
    public const string ReceivedSegment = "received";
    public const string LostLinkSegment = "lost-link";
    public const string SuggestSegment = "suggest";

    // The help centre's own segments and query parameters. The output cache and the header rules match on them (PortalCachePaths), so they are named once.
    public const string KbSegment = "kb";
    public const string KbSearchSegment = "search";

    /// <summary>The page number of a paged KB list (a category page or the search results).</summary>
    public const string PageParameter = "page";

    /// <summary>The search text of the KB search page.</summary>
    public const string QueryParameter = "q";

    /// <summary>The query parameter that makes the lost-link page show its confirmation.</summary>
    public const string SentParameter = "sent";

    /// <summary>The query parameter that carries the protected ticket reference to the "received" page.</summary>
    public const string ReceivedReferenceParameter = "ref";

    /// <summary>The query parameters of the contact page that prefill its inputs (<c>?subject=&amp;name=&amp;email=</c>).</summary>
    public static readonly IReadOnlyList<string> PrefillParameters = ["subject", "name", "email"];

    public const string HomeTemplate = "/";
    public const string NotFoundTemplate = "/not-found";
    public const string ErrorTemplate = "/error";
    public const string StyleGuideTemplate = "/_styleguide";

    public const string ProductHomeTemplate = "/p/{key}";
    public const string ContactTemplate = "/p/{key}/contact";
    public const string ContactReceivedTemplate = "/p/{key}/contact/received";
    public const string LostLinkTemplate = "/p/{key}/lost-link";
    public const string KbHomeTemplate = "/p/{key}/kb";

    // A literal segment wins over a parameter in endpoint routing: /p/{key}/kb/search is not a category. The API reserves the category slug "search" (KbLimits.ReservedCategorySlug).
    public const string KbCategoryTemplate = "/p/{key}/kb/{category}";
    public const string KbArticleTemplate = "/p/{key}/kb/{category}/{slug}";
    public const string KbSearchTemplate = "/p/{key}/kb/search";

    // The KB suggestion adapter sits beside the KB, not under it, so no category slug can ever shadow it (D-045 addendum, 2026-10-06).
    public const string SuggestTemplate = "/p/{key}/suggest";

    public const string TicketTemplate = "/t/{token}";
    public const string TicketAttachmentTemplate = "/t/{token}/attachments/{id}";

    // Paths served on every host (a product host does not rewrite them): the framework's and the static assets, the SEO files, the health checks and the host's own error pages.
    public const string FrameworkPrefix = "/_framework";
    public const string BlazorPrefix = "/_blazor";
    public const string ContentPrefix = "/_content";
    public const string CssPrefix = "/css";
    public const string JsPrefix = "/js";
    public const string ImgPrefix = "/img";
    public const string FaviconPrefix = "/favicon";
    public const string SitemapPath = "/sitemap.xml";
    public const string RobotsPath = "/robots.txt";
    public const string HealthPrefix = "/health";

    // The paths a product host answers with the clean form of a /p/{key} page: the product's own pages without their /p/{key} prefix.
    public static readonly string ContactPath = $"/{ContactSegment}";
    public static readonly string LostLinkPath = $"/{LostLinkSegment}";
    public static readonly string KbPath = $"/{KbSegment}";
    public static readonly string SuggestPath = $"/{SuggestSegment}";

    /// <summary>
    /// Splits <c>/p/{key}</c> and <c>/p/{key}/more</c> into the key and the rest (<paramref name="rest"/> is empty or starts with a slash). Any other path, and a path with an empty key, is not a product path.
    /// </summary>
    public static bool TryStripProductPrefix(PathString path, out string key, out PathString rest)
    {
        key = string.Empty;
        rest = PathString.Empty;
        var value = path.Value;
        if (value is null || !value.StartsWith($"{ProductPrefix}/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var afterPrefix = value[(ProductPrefix.Length + 1)..];
        var slash = afterPrefix.IndexOf('/');
        var candidate = slash < 0 ? afterPrefix : afterPrefix[..slash];
        if (candidate.Length == 0)
        {
            return false;
        }

        key = candidate;
        rest = slash < 0 ? PathString.Empty : new PathString(afterPrefix[slash..]);
        return true;
    }

    public static string ProductHome(string key) => $"{ProductPrefix}/{Escape(key)}";

    public static string Contact(string key) => $"{ProductHome(key)}/contact";

    public static string ContactReceived(string key) => $"{Contact(key)}/received";

    /// <summary>The "received" page with its protected reference.</summary>
    public static string ContactReceived(string key, string reference) => $"{ContactReceived(key)}?{ReceivedReferenceParameter}={Escape(reference)}";

    public static string LostLink(string key) => $"{ProductHome(key)}/lost-link";

    /// <summary>The lost-link page as it is shown after a request: the same address for every request, whatever the address was.</summary>
    public static string LostLinkSent(string key) => $"{LostLink(key)}?{SentParameter}=1";

    public static string KbHome(string key) => $"{ProductHome(key)}/{KbSegment}";

    public static string KbCategory(string key, string category) => $"{KbHome(key)}/{Escape(category)}";

    /// <summary>A page of a category: page one is the category's own address, so there is one address for it; a later page adds <c>?page=n</c>.</summary>
    public static string KbCategory(string key, string category, int page) => page <= 1 ? KbCategory(key, category) : $"{KbCategory(key, category)}?{PageParameter}={page}";

    public static string KbArticle(string key, string category, string slug) => $"{KbCategory(key, category)}/{Escape(slug)}";

    public static string KbSearch(string key) => $"{KbHome(key)}/{KbSearchSegment}";

    /// <summary>A page of search results: the text is escaped, so it can never add a parameter, and a paging link keeps it. Page one has no <c>page</c>; a blank text is the search page itself.</summary>
    public static string KbSearch(string key, string text, int page) =>
        string.IsNullOrWhiteSpace(text)
            ? KbSearch(key)
            : $"{KbSearch(key)}?{QueryParameter}={Escape(text)}{(page <= 1 ? string.Empty : $"&{PageParameter}={page}")}";

    public static string Suggest(string key) => $"{ProductHome(key)}/suggest";

    public static string Ticket(string token) => $"{TicketPrefix}/{Escape(token)}";

    public static string TicketAttachment(string token, Guid attachmentId) => $"{Ticket(token)}/attachments/{attachmentId}";

    /// <summary>The link to a ticket from a parsed token (the token's own <c>ToString</c> is a fixed marker, so the real value is read here, where a link is built).</summary>
    public static string Ticket(TicketToken token) => Ticket(token.Value);

    public static string TicketAttachment(TicketToken token, Guid attachmentId) => TicketAttachment(token.Value, attachmentId);

    private static string Escape(string value) => Uri.EscapeDataString(value);
}
