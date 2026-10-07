using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace TechStrap.Portal.Tests.Components;

/// <summary>What the accessibility host tests share: a parsed page and the way a browser resolves a link in it.</summary>
internal static class PageKit
{
    public const string Origin = "http://localhost";

    public static IDocument Parse(string html) => new HtmlParser().ParseDocument(html);

    /// <summary>
    /// Where a browser goes for <paramref name="href"/> on the page at <paramref name="pageUrl"/>: the link is resolved against the document's base URL, which is the page's <c>base</c> element resolved against the
    /// page's own address. (A bare <c>#email</c> under <c>base href="/"</c> comes out as <c>/#email</c>: the home page. That is the bug the Portal's links are written to avoid.)
    /// </summary>
    public static Uri Resolve(IDocument dom, string pageUrl, string href)
    {
        var page = new Uri(pageUrl, UriKind.Absolute);
        var baseHref = dom.QuerySelector("base")?.GetAttribute("href") ?? string.Empty;
        return new Uri(new Uri(page, baseHref), href);
    }

    /// <summary>True when following <paramref name="href"/> stays inside the document at <paramref name="pageUrl"/> (the same address, only the fragment differs), so the browser jumps and does not load a page.</summary>
    public static bool IsJumpWithin(IDocument dom, string pageUrl, string href)
    {
        var target = Resolve(dom, pageUrl, href);
        var page = new Uri(pageUrl, UriKind.Absolute);
        return target.GetLeftPart(UriPartial.Query) == page.GetLeftPart(UriPartial.Query) && target.Fragment.Length > 1;
    }

    /// <summary>The first element, in document order, a keyboard reaches with Tab: a link with an address, a button, a field that is not hidden or disabled and not taken out of the order.</summary>
    public static IElement? FirstFocusable(IDocument dom) =>
        dom.QuerySelectorAll("a[href], button:not([disabled]), input:not([type=hidden]):not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex^='-'])").FirstOrDefault();
}
