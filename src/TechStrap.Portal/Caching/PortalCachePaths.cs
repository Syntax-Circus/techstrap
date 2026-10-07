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
    // The only raw query string a kept category request may carry besides none at all: literally "?page=" and a page number as above. The raw text is compared, so a case variant (?PAGE=2), a percent-encoded
    // spelling (?pa%67e=2, ?page=%32) or any extra parameter is answered but never stored: the page's links repeat the address bar's own spelling, and a stored copy would hand one visitor's spelling to the next.
    [GeneratedRegex(@"\A\?page=(?:[2-9]|[1-9][0-9]{1,3})\z", RegexOptions.CultureInvariant)]
    private static partial Regex PageQuery();

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
    /// A KB page that is kept when its <c>page</c> query value is absent; a category page is kept only when its raw query string is empty or literally <c>?page=</c> and a page number from two up (no case variant, no
    /// percent-encoding, no other parameter). On the home and an article any <c>page</c> value is not kept (it changes nothing, so each value would be a copy of the same page), and on a category <c>page=1</c> is not
    /// kept either (it is the page with no value). On the home and an article other query values do not change the page, so they do not change the key.
    /// Only an all-lowercase path is kept. The framework's key compares the path without regard to case, so if a capitalised path could be looked up, <c>/p/ACME/kb</c> would be answered from the stored
    /// <c>/p/acme/kb</c> instead of the neutral 404. A path with an upper-case letter makes the predicate false, so the cache neither stores nor looks it up: an unknown capitalised key stays the byte-identical 404,
    /// and a capitalised fixed segment (<c>/p/acme/KB</c>) is a 200 that is never stored (D-045 as-built).
    /// </summary>
    public static bool IsCacheable(HttpRequest request)
    {
        if (!IsKbPage(request.Path) || request.Path.Value!.AsSpan().IndexOfAnyInRange('A', 'Z') >= 0)
        {
            return false;
        }

        if (IsCategoryPath(request.Path))
        {
            var raw = request.QueryString.Value;
            return string.IsNullOrEmpty(raw) || PageQuery().IsMatch(raw);
        }

        return !request.Query.ContainsKey(PortalRoutes.PageParameter);
    }

    private static string[] Segments(PathString rest) => rest.Value!.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static bool Is(string segment, string expected) => segment.Equals(expected, StringComparison.OrdinalIgnoreCase);
}
