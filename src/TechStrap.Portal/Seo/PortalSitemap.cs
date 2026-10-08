using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Portal.Hosting;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Seo;

/// <summary>
/// The glue between <c>MapSeoSitemap</c> and the Portal's sitemap cache (D-045 addendum, PHASE-09c). The provider is called with the request's services; the build it hands the cache runs later, on its own task, so it
/// gets a scope of its own and a stand-in <c>HttpContext</c> that carries only the address of the visitor whose request started it: the typed clients read that to set <c>X-Forwarded-For</c>, so the API rate-limits the
/// crawler and not the Portal. Every other request that waits for the same build is served from it without a call of its own; that the build's calls carry the first crawler's address is accepted and recorded as a known gap.
/// </summary>
internal static class PortalSitemap
{
    /// <summary>What a crawler or a proxy may keep: a short while, so a new article shows within minutes (the server cache is 15).</summary>
    public static readonly TimeSpan ClientCacheDuration = TimeSpan.FromMinutes(5);

    public static IServiceCollection AddPortalSitemap(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton(provider => new PortalSitemapCache(
            provider.GetRequiredService<IMemoryCache>(),
            provider.GetRequiredService<ILogger<PortalSitemapCache>>(),
            PortalSitemapCache.DefaultTtl,
            PortalSitemapCache.DefaultFailureTtl,
            PortalSitemapCache.BuildTimeout));
        services.AddScoped<PortalSitemapBuilder>();
        return services;
    }

    /// <summary>The static entries: the root page, unless a default product is configured (then <c>/</c> is a redirect, which a sitemap must not list).</summary>
    public static IReadOnlyList<SitemapEntry> StaticEntries(PortalOptions options) =>
        options.DefaultProductKeyOrNull is null ? [new SitemapEntry(options.PublicBaseUrl + "/")] : [];

    /// <summary>
    /// The sitemap of the host the request arrived on (PHASE-11e): a product host lists only its own product, with clean paths on its stored host (and no root entry, which belongs to the default host); the default host
    /// lists the static entries and the products that have no host of their own. Each host has its own cache entry, keyed by the stored host (never the request's Host header).
    /// </summary>
    public static async Task<IReadOnlyList<SitemapEntry>> ProviderAsync(IServiceProvider requestServices, CancellationToken requestAborted)
    {
        var visitor = requestServices.GetRequiredService<IHttpContextAccessor>().HttpContext?.Connection.RemoteIpAddress;
        var requested = requestServices.GetRequiredService<ProductHostContext>();
        var host = new ProductHostContext { Key = requested.Key, Host = requested.Host };
        IReadOnlyList<SitemapEntry> statics = host.IsProductHost ? [] : StaticEntries(requestServices.GetRequiredService<IOptions<PortalOptions>>().Value);
        var scopes = requestServices.GetRequiredService<IServiceScopeFactory>();
        var built = await requestServices.GetRequiredService<PortalSitemapCache>().GetAsync(
            async buildToken =>
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { Connection = { RemoteIpAddress = visitor } };
                return await scope.ServiceProvider.GetRequiredService<PortalSitemapBuilder>().BuildAsync(statics.Count, host, buildToken);
            },
            requestAborted,
            host.Host ?? PortalSitemapCache.DefaultKey);
        return statics.Count == 0 ? built : [.. statics, .. built];
    }
}

