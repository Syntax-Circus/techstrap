namespace TechStrap.Portal.Routing;

/// <summary>
/// Links to a place on the CURRENT page (the error summary's field links, the skip link and the "jump to your reply" link). The document's <c>base</c> is <c>/</c> (it has to be: the stylesheet, the favicon and
/// <c>blazor.web.js</c> are relative to it), and under that base a bare <c>href="#email"</c> resolves to <c>/#email</c>, the home page, not to the field (D-045 09d addendum; the Admin hit the same bug). So the link
/// is written out in full as a root-relative path plus the fragment, which the browser treats as a jump within the page, and the link opts out of Blazor's enhanced navigation (<c>data-enhance-nav="false"</c>),
/// which would otherwise take the click, scroll, and leave the focus on the link instead of moving it to the field.
/// </summary>
public static class PageLinks
{
    private static readonly string[] None = [];

    /// <summary>
    /// The current page's root-relative address plus <paramref name="fragment"/>, built from the absolute <paramref name="currentUri"/> (<c>NavigationManager.Uri</c>). With <paramref name="keepQuery"/> the query
    /// parameters THIS PAGE READS stay (the prefill of the contact form, the reference of the received page, the search text), so a jump to <c>#main</c> is a jump and not a reload that loses what the page shows.
    /// Every other parameter is dropped: it changes nothing on the page and is never echoed into it, and a copy the output cache keeps for one visitor must not carry another's parameters.
    /// </summary>
    public static string ToFragment(string currentUri, string fragment, bool keepQuery)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fragment);
        var uri = new Uri(currentUri, UriKind.Absolute);
        var path = uri.AbsolutePath;
        var query = string.Empty;
        if (keepQuery)
        {
            var known = KnownParameters(path);
            // The raw pairs, in the address bar's own spelling (a form's GET submit writes "paper+jam"; re-encoding it as "paper%20jam" would make the href differ from the address and the jump a reload).
            // Only the KEY is decoded, to decide whether the page reads it; a case-variant key (?PAGE=2) is kept as written, because the canonical name would again differ from the address bar.
            var kept = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(pair => known.Contains(DecodeKey(pair), StringComparer.OrdinalIgnoreCase))
                .ToList();
            query = kept.Count == 0 ? string.Empty : "?" + string.Join('&', kept);
        }

        return $"{path}{query}#{fragment}";
    }

    /// <summary>The query parameters a page of this address reads (see <see cref="PortalRoutes"/>); none for any other page.</summary>
    internal static IReadOnlyList<string> KnownParameters(string path)
    {
        var segments = Uri.UnescapeDataString(path).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3 || !segments[0].Equals(PortalRoutes.ProductPrefix.Trim('/'), StringComparison.OrdinalIgnoreCase))
        {
            return None;
        }

        var page = segments[2..];
        return page switch
        {
            [var contact] when Is(contact, PortalRoutes.ContactSegment) => PortalRoutes.PrefillParameters,
            [var contact, var received] when Is(contact, PortalRoutes.ContactSegment) && Is(received, PortalRoutes.ReceivedSegment) => [PortalRoutes.ReceivedReferenceParameter],
            [var lostLink] when Is(lostLink, PortalRoutes.LostLinkSegment) => [PortalRoutes.SentParameter],
            [var kb, var search] when Is(kb, PortalRoutes.KbSegment) && Is(search, PortalRoutes.KbSearchSegment) => [PortalRoutes.QueryParameter, PortalRoutes.PageParameter],
            [var kb, _] when Is(kb, PortalRoutes.KbSegment) => [PortalRoutes.PageParameter],
            _ => None,
        };
    }

    private static string DecodeKey(string pair)
    {
        var eq = pair.IndexOf('=', StringComparison.Ordinal);
        var key = eq < 0 ? pair : pair[..eq];
        return Uri.UnescapeDataString(key.Replace('+', ' '));
    }

    private static bool Is(string segment, string expected) => segment.Equals(expected, StringComparison.OrdinalIgnoreCase);
}
