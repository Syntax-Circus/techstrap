using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Caching;

/// <summary>
/// Which requests the Portal may keep (D-045 addendum, PHASE-09c). Only the three kinds of help-centre page that show the same thing to every visitor: <c>/p/{key}/kb</c>, <c>/p/{key}/kb/{category}</c> and
/// <c>/p/{key}/kb/{category}/{slug}</c>. The search page (any text can be asked), the form pages, the suggest adapter, <c>/t/*</c> and every other path are never kept; a 404, a 429 and a 503 never are either
/// (the output cache stores a 200 only). The predicates are written like <c>PortalHeaderRules.IsFormPagePath</c>: without regard to case or a trailing slash, because routing matches that way.
/// </summary>
internal static partial class PortalCachePaths
{
    /// <summary>How long the server keeps a page: the API's own <c>max-age</c> for the same data, so a cached page is never staler than the API's answer could have been.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    /// <summary>What a browser is told about a delivered KB page (the same minute).</summary>
    public const string BrowserCacheControl = "public, max-age=60";

    // A page number of one to four digits, no sign and no leading zero: the only value a cached request may carry. Anything else (text, a huge number, two values) is still answered, but never stored,
    // because each distinct value would be a new key and a visitor could fill the store with them.
    [GeneratedRegex(@"\A[1-9][0-9]{0,3}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PageNumber();

    /// <summary>A help-centre page of the three kinds above (the search page is not one).</summary>
    public static bool IsKbPage(PathString path) =>
        path.StartsWithSegments(PortalRoutes.ProductPrefix, out var rest)
        && Segments(rest) is [_, var kb, .. var tail]
        && Is(kb, PortalRoutes.KbSegment)
        && tail.Length <= 2
        && (tail.Length == 0 || !Is(tail[0], PortalRoutes.KbSearchSegment));

    /// <summary>Exactly <c>/p/{key}/kb/search</c>.</summary>
    public static bool IsKbSearchPath(PathString path) =>
        path.StartsWithSegments(PortalRoutes.ProductPrefix, out var rest)
        && Segments(rest) is [_, var kb, var search]
        && Is(kb, PortalRoutes.KbSegment)
        && Is(search, PortalRoutes.KbSearchSegment);

    /// <summary>A KB page whose <c>page</c> query value is absent or a plain page number (other query values do not change the page, so they do not change the key).</summary>
    public static bool IsCacheable(HttpRequest request) =>
        IsKbPage(request.Path)
        && (!request.Query.TryGetValue(PortalRoutes.PageParameter, out var pages) || (pages.Count == 1 && pages[0] is { } page && PageNumber().IsMatch(page)));

    private static string[] Segments(PathString rest) => rest.Value!.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static bool Is(string segment, string expected) => segment.Equals(expected, StringComparison.OrdinalIgnoreCase);
}
