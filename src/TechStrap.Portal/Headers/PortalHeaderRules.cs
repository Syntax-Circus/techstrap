using Microsoft.AspNetCore.Http;
using TechStrap.Hosting.Wiring;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Headers;

/// <summary>
/// The Portal's per-path response headers (D-045), applied by <c>UseTechStrapWebHost</c> after the shared security headers so nothing a page sets can undo them.
/// Everything under <c>/t</c> (the ticket page, its attachments, and the 404 that replaces an unknown ticket path) is a private page: the access token is in its address, so the browser must send no
/// referrer from it, keep no copy of it and not index it. Only the attachment route is sandboxed, because the ticket page itself is an ordinary page that needs its normal policy.
/// </summary>
internal static class PortalHeaderRules
{
    public const string ReferrerPolicy = "no-referrer";
    public const string CacheControl = "no-store";
    public const string RobotsTag = "noindex";

    /// <summary>The rules for <c>UseTechStrapWebHost</c>: the ticket headers, the attachment sandbox, then the form pages' headers.</summary>
    public static IReadOnlyList<PathHeaderRule> Rules { get; } =
    [
        PathHeaderRule.Set(IsTicketPath, ("Referrer-Policy", ReferrerPolicy), ("Cache-Control", CacheControl), ("X-Robots-Tag", RobotsTag)),
        PathHeaderRule.Sandbox(IsTicketAttachmentPath),

        // The form pages keep the shared referrer policy (a visitor's own address is no secret from the same site) but are never indexed and never stored: their address can carry a name, an address and a subject.
        PathHeaderRule.Set(IsFormPagePath, ("Cache-Control", CacheControl), ("X-Robots-Tag", RobotsTag)),
    ];

    /// <summary><c>/t</c> and everything under it, without regard to case or a trailing slash.</summary>
    public static bool IsTicketPath(PathString path) => path.StartsWithSegments(PortalRoutes.TicketPrefix);

    /// <summary>
    /// The four form pages of a product (D-045 addendum): <c>/p/{key}/contact</c>, <c>/p/{key}/contact/received</c>, <c>/p/{key}/lost-link</c> and <c>/p/{key}/suggest</c>, without regard to case or a trailing slash (routing
    /// matches without regard to case, so the rule must too). Nothing deeper and nothing else, so the product home and the help centre stay cacheable.
    /// </summary>
    public static bool IsFormPagePath(PathString path) =>
        path.StartsWithSegments(PortalRoutes.ProductPrefix, out var rest)
        && rest.Value!.Split('/', StringSplitOptions.RemoveEmptyEntries) is [_, var page, .. var tail]
        && (Is(page, PortalRoutes.ContactSegment) ? tail.Length == 0 || (tail.Length == 1 && Is(tail[0], PortalRoutes.ReceivedSegment))
            : (Is(page, PortalRoutes.LostLinkSegment) || Is(page, PortalRoutes.SuggestSegment)) && tail.Length == 0);

    private static bool Is(string segment, string expected) => segment.Equals(expected, StringComparison.OrdinalIgnoreCase);

    /// <summary>Exactly <c>/t/{token}/attachments/{id}</c>: the three segments after <c>/t</c>, the middle one <c>attachments</c>.</summary>
    public static bool IsTicketAttachmentPath(PathString path) =>
        path.StartsWithSegments(PortalRoutes.TicketPrefix, out var rest)
        && rest.Value!.Split('/', StringSplitOptions.RemoveEmptyEntries) is [_, var middle, _]
        && middle.Equals("attachments", StringComparison.OrdinalIgnoreCase);
}
