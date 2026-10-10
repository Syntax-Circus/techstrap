using Microsoft.Extensions.Options;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Hosting;
using TechStrap.Portal.Routing;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Seo;

/// <summary>A sitemap build that could not finish: the API said no or could not be reached. The message carries the failing call and its error code, never a visitor's text.</summary>
internal sealed class SitemapBuildException(string message) : Exception(message);

/// <summary>
/// Builds the Portal's sitemap entries from the API (D-045, PHASE-09c): one call for the active products, then one call per product for its published articles. For each product the entries are its home, its help
/// center home (only when it has an article), each category (found from the articles, last changed when its newest article was) and each article; a shared article (no product key) is listed under every product, because each
/// product's help center is its own site. Every address is absolute, built by <see cref="PortalLinks"/> (which escapes each segment) from the host the product has in the fresh product list (never the host map's snapshot), or from <c>TECHSTRAP_PORTAL_PUBLIC_URL</c> on the default host; with no public address (Development only)
/// they are root-relative. At most <see cref="MaxUrls"/> addresses are listed in all, the limit of one sitemap file, the static entries (the root page) included: the caller says how many of those there are, and the build keeps
/// the rest of the room for the products; a cut is logged. The build is scoped: it uses the typed clients.
/// </summary>
internal sealed class PortalSitemapBuilder(IPublicProductClient products, IPublicKbClient kb, ProductHostMap map, IOptions<PortalOptions> options, ILogger<PortalSitemapBuilder> logger)
{
    /// <summary>The most addresses one sitemap file may hold (sitemaps.org).</summary>
    public const int MaxUrls = 50_000;

    /// <param name="reservedForStatic">How many addresses the sitemap holds besides these (the static entries), so the whole file stays within <see cref="MaxUrls"/>.</param>
    /// <param name="host">The host the sitemap is for: on a product host only that product is listed, with clean paths on its host; on the default host only the products that have no host of their own.</param>
    public async Task<IReadOnlyList<SitemapEntry>> BuildAsync(int reservedForStatic, ProductHostContext host, CancellationToken cancellationToken)
    {
        var listed = await products.ListAsync(cancellationToken);
        if (listed.IsFailure)
        {
            throw new SitemapBuildException($"The product list failed ({listed.Errors[0].Code}).");
        }

        // The hosts come from this fresh list, never from the host map (whose snapshot may be a minute old): the result is kept for a quarter of an hour, so a stale host would stay that long.
        var entries = new List<SitemapEntry>();
        foreach (var product in listed.Value)
        {
            var productHost = string.IsNullOrWhiteSpace(product.PortalHost) ? null : product.PortalHost.Trim().ToLowerInvariant();
            var listedHere = host.IsProductHost
                ? string.Equals(product.Key, host.Key, StringComparison.OrdinalIgnoreCase) && string.Equals(productHost, host.Host, StringComparison.Ordinal)
                : productHost is null;
            if (!listedHere)
            {
                continue;
            }

            var articles = await kb.GetSitemapAsync(product.Key, cancellationToken);
            if (articles.IsFailure)
            {
                throw new SitemapBuildException($"The sitemap of a product failed ({articles.Errors[0].Code}).");
            }

            entries.AddRange(EntriesOf(PortalLinks.ForListedProduct(map, options, product.Key, productHost), product.Key, articles.Value));
        }

        var distinct = entries.DistinctBy(entry => entry.Url).ToList();
        var room = Math.Max(0, MaxUrls - reservedForStatic);
        if (distinct.Count > room)
        {
            logger.LogWarning("The sitemap would hold {Count} addresses; only the first {Max} are listed.", distinct.Count, room);
            distinct = distinct.Take(room).ToList();
        }

        return distinct;
    }

    /// <summary>The entries of one product: its home, its help center home, its categories and its articles (see the class summary).</summary>
    internal static IEnumerable<SitemapEntry> EntriesOf(PortalLinks links, string productKey, IReadOnlyList<KbSitemapEntryDto> articles)
    {
        yield return new SitemapEntry(links.Absolute(links.ProductHome(productKey)));
        if (articles.Count == 0)
        {
            yield break;
        }

        yield return new SitemapEntry(links.Absolute(links.KbHome(productKey)), Day(articles.Max(article => article.UpdatedAt)));
        foreach (var category in articles.GroupBy(article => article.CategorySlug, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            yield return new SitemapEntry(links.Absolute(links.KbCategory(productKey, category.Key)), Day(category.Max(article => article.UpdatedAt)));
        }

        foreach (var article in articles)
        {
            yield return new SitemapEntry(links.Absolute(links.KbArticle(productKey, article.CategorySlug, article.Slug)), Day(article.UpdatedAt));
        }
    }

    private static DateOnly Day(DateTimeOffset moment) => DateOnly.FromDateTime(moment.UtcDateTime);
}
