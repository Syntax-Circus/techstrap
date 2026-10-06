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

    // A page number of two to four digits, no sign and no leading zero, or a single digit from two to nine: the only value a cached category request may carry. Page one is not one of them: it is the page with no value,
    // so ?page=1 would be a second stored copy of the same page. Anything else (text, a huge number, two values) is still answered, but never stored, because each distinct value would be a new key and a visitor
    // could fill the store with them.
    [GeneratedRegex(@"\A(?:[2-9]|[1-9][0-9]{1,3})\z", RegexOptions.CultureInvariant)]
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

    /// <summary><c>/p/{key}/kb/{category}</c>: the only kept page that pages (the home and an article ignore <c>page</c>).</summary>
    private static bool IsCategoryPath(PathString path) =>
        IsKbPage(path)
        && path.StartsWithSegments(PortalRoutes.ProductPrefix, out var rest)
        && Segments(rest).Length == 3;

    /// <summary>
    /// A KB page that is kept when its <c>page</c> query value is absent; a category page is also kept with a page number from two up. On the home and an article any <c>page</c> value is not kept (it changes nothing, so
    /// each value would be a copy of the same page), and on a category <c>page=1</c> is not kept either (it is the page with no value). Other query values do not change the page, so they do not change the key.
    /// The key's path is compared without regard to case (the framework's default), so once <c>/p/acme/kb</c> is stored <c>/p/ACME/kb</c> is answered from it, a known exception to "an unknown key is the neutral 404";
    /// it is the same public page with a canonical address fixed from <c>Seo:BaseUrl</c>, and a case-sensitive key would let a visitor store one copy per capitalisation (D-045 as-built).
    /// </summary>
    public static bool IsCacheable(HttpRequest request)
    {
        if (!IsKbPage(request.Path))
        {
            return false;
        }

        if (!request.Query.TryGetValue(PortalRoutes.PageParameter, out var pages))
        {
            return true;
        }

        return IsCategoryPath(request.Path) && pages.Count == 1 && pages[0] is { } page && PageNumber().IsMatch(page);
    }

    private static string[] Segments(PathString rest) => rest.Value!.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static bool Is(string segment, string expected) => segment.Equals(expected, StringComparison.OrdinalIgnoreCase);
}
