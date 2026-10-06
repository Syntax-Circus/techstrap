using Microsoft.Extensions.Caching.Memory;
using SyntaxCircus.AspNetCore.Common;

namespace TechStrap.Portal.Seo;

/// <summary>No sitemap could be built and there is no earlier one to serve.</summary>
internal sealed class SitemapUnavailableException() : Exception("The sitemap could not be built and there is no earlier one to serve.");

/// <summary>
/// The Portal's sitemap, kept for 15 minutes in one <see cref="IMemoryCache"/> entry (D-045 addendum, PHASE-09c). <c>MapSeoSitemap</c> asks its provider on every request and keeps nothing, and a build costs one API call
/// for the products and one per product, so a crawler must never start a build of its own:
/// <list type="bullet">
/// <item><b>Single-flight.</b> While one build runs, every other request waits for it; twenty concurrent requests make one build. Once there is an earlier sitemap, a rebuild is stale-while-revalidate: the request that
/// starts it waits for it, and every request during it gets the earlier sitemap at once.</item>
/// <item><b>Its own cancellation token.</b> The build runs on its own task, with no link to any request (and without the request's execution context, so it never touches that request's <c>HttpContext</c>). A crawler that goes
/// away stops waiting (its own token) but cannot cancel the build, so it cannot leave the cache empty or poisoned for the others. A build that never finishes ends after <see cref="BuildTimeout"/>.</item>
/// <item><b>A failure is remembered for a minute.</b> The API is not asked again for that minute. The last good sitemap, however old, is served meanwhile; with none (the very first build failed) the request fails.</item>
/// </list>
/// The lifetimes are constructor values because <see cref="MemoryCacheOptions"/> has no <see cref="TimeProvider"/> (only an obsolete clock): production passes the defaults, a test passes short real ones.
/// </summary>
internal sealed class PortalSitemapCache(IMemoryCache cache, ILogger<PortalSitemapCache> logger, TimeSpan ttl, TimeSpan failureTtl, TimeSpan buildTimeout)
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan DefaultFailureTtl = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(2);

    private const string FreshKey = "portal-sitemap";
    private const string FailedKey = "portal-sitemap-failed";

    private readonly Lock _gate = new();
    private Task<IReadOnlyList<SitemapEntry>>? _flight;
    private IReadOnlyList<SitemapEntry>? _lastGood;

    /// <summary>The sitemap entries: the cached ones, or the result of <paramref name="build"/> when none are cached and no build is running (only the first caller's <paramref name="build"/> runs).</summary>
    public async Task<IReadOnlyList<SitemapEntry>> GetAsync(Func<CancellationToken, Task<IReadOnlyList<SitemapEntry>>> build, CancellationToken requestAborted)
    {
        Task<IReadOnlyList<SitemapEntry>> flight;
        lock (_gate)
        {
            if (cache.TryGetValue(FreshKey, out IReadOnlyList<SitemapEntry>? fresh) && fresh is not null)
            {
                return fresh;
            }

            if (cache.TryGetValue(FailedKey, out _))
            {
                return _lastGood ?? throw new SitemapUnavailableException();
            }

            if (_flight is not null && _lastGood is { } stale)
            {
                // Stale-while-revalidate: a rebuild is already running and there is an earlier sitemap, so nobody waits for the build (the one that started it does, to give it its result).
                return stale;
            }

            flight = _flight ??= StartBuild(build);
        }

        return await flight.WaitAsync(requestAborted);
    }

    private Task<IReadOnlyList<SitemapEntry>> StartBuild(Func<CancellationToken, Task<IReadOnlyList<SitemapEntry>>> build)
    {
        // The request's HttpContext lives in an AsyncLocal that the new task would inherit and the build must not touch (see PortalSitemap), so the flow is suppressed for the start only.
        using (ExecutionContext.SuppressFlow())
        {
            var flight = Task.Run(() => RunAsync(build));

            // Whoever waits on the flight may all have gone away (a crawler that aborts), so the flight's own exception is read here: it is already logged by RunAsync, and an unread one would reach
            // TaskScheduler.UnobservedTaskException when the task is collected.
            _ = flight.ContinueWith(static task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return flight;
        }
    }

    private async Task<IReadOnlyList<SitemapEntry>> RunAsync(Func<CancellationToken, Task<IReadOnlyList<SitemapEntry>>> build)
    {
        using var timeout = new CancellationTokenSource(buildTimeout);
        try
        {
            var entries = await build(timeout.Token);
            lock (_gate)
            {
                _lastGood = entries;
                cache.Set(FreshKey, entries, ttl);
                _flight = null;
            }

            return entries;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The sitemap could not be built; the last good one is served for {Seconds} seconds.", failureTtl.TotalSeconds);
            lock (_gate)
            {
                cache.Set(FailedKey, true, failureTtl);
                _flight = null;
                if (_lastGood is { } stale)
                {
                    return stale;
                }
            }

            throw;
        }
    }
}
