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

    /// <summary>The rules for <c>UseTechStrapWebHost</c>: the ticket headers, then the attachment sandbox.</summary>
    public static IReadOnlyList<PathHeaderRule> Rules { get; } =
    [
        PathHeaderRule.Set(IsTicketPath, ("Referrer-Policy", ReferrerPolicy), ("Cache-Control", CacheControl), ("X-Robots-Tag", RobotsTag)),
        PathHeaderRule.Sandbox(IsTicketAttachmentPath),
    ];

    /// <summary><c>/t</c> and everything under it, without regard to case or a trailing slash.</summary>
    public static bool IsTicketPath(PathString path) => path.StartsWithSegments(PortalRoutes.TicketPrefix);

    /// <summary>Exactly <c>/t/{token}/attachments/{id}</c>: the three segments after <c>/t</c>, the middle one <c>attachments</c>.</summary>
    public static bool IsTicketAttachmentPath(PathString path) =>
        path.StartsWithSegments(PortalRoutes.TicketPrefix, out var rest)
        && rest.Value!.Split('/', StringSplitOptions.RemoveEmptyEntries) is [_, var middle, _]
        && middle.Equals("attachments", StringComparison.OrdinalIgnoreCase);
}
