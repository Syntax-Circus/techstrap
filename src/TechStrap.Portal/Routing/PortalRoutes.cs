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

    /// <summary>The query parameter that makes the lost-link page show its confirmation.</summary>
    public const string SentParameter = "sent";

    /// <summary>The query parameter that carries the protected ticket reference to the "received" page.</summary>
    public const string ReceivedReferenceParameter = "ref";

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

    public static string ProductHome(string key) => $"{ProductPrefix}/{Escape(key)}";

    public static string Contact(string key) => $"{ProductHome(key)}/contact";

    public static string ContactReceived(string key) => $"{Contact(key)}/received";

    /// <summary>The "received" page with its protected reference.</summary>
    public static string ContactReceived(string key, string reference) => $"{ContactReceived(key)}?{ReceivedReferenceParameter}={Escape(reference)}";

    public static string LostLink(string key) => $"{ProductHome(key)}/lost-link";

    /// <summary>The lost-link page as it is shown after a request: the same address for every request, whatever the address was.</summary>
    public static string LostLinkSent(string key) => $"{LostLink(key)}?{SentParameter}=1";

    public static string KbHome(string key) => $"{ProductHome(key)}/kb";

    public static string KbCategory(string key, string category) => $"{KbHome(key)}/{Escape(category)}";

    public static string KbArticle(string key, string category, string slug) => $"{KbCategory(key, category)}/{Escape(slug)}";

    public static string KbSearch(string key) => $"{KbHome(key)}/search";

    public static string Suggest(string key) => $"{ProductHome(key)}/suggest";

    public static string Ticket(string token) => $"{TicketPrefix}/{Escape(token)}";

    public static string TicketAttachment(string token, Guid attachmentId) => $"{Ticket(token)}/attachments/{attachmentId}";

    /// <summary>The link to a ticket from a parsed token (the token's own <c>ToString</c> is a fixed marker, so the real value is read here, where a link is built).</summary>
    public static string Ticket(TicketToken token) => Ticket(token.Value);

    public static string TicketAttachment(TicketToken token, Guid attachmentId) => TicketAttachment(token.Value, attachmentId);

    private static string Escape(string value) => Uri.EscapeDataString(value);
}
