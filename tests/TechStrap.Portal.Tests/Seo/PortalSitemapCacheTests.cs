using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Portal.Seo;

namespace TechStrap.Portal.Tests.Seo;

/// <summary>
/// PHASE-09c Review Focus 4 (sitemap robustness), the cache: single-flight, a build that no crawler can cancel, a failure remembered briefly and the last good sitemap served meanwhile. The lifetimes are short real ones
/// (a <see cref="MemoryCache"/> has no <see cref="TimeProvider"/>), so every test that waits for one waits for a few hundred milliseconds.
/// </summary>
public sealed class PortalSitemapCacheTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan Long = TimeSpan.FromMinutes(5);

    private static IReadOnlyList<SitemapEntry> Entries(string name) => [new SitemapEntry($"https://portal.test/{name}")];

    private static PortalSitemapCache Cache(TimeSpan? ttl = null, TimeSpan? failureTtl = null, TimeSpan? buildTimeout = null) =>
        new(new MemoryCache(new MemoryCacheOptions()), NullLogger<PortalSitemapCache>.Instance, ttl ?? Long, failureTtl ?? Long, buildTimeout ?? Long);

    private sealed class Builds
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public Func<CancellationToken, Task<IReadOnlyList<SitemapEntry>>> Succeeding(string name, TimeSpan? delay = null) => async token =>
        {
            Interlocked.Increment(ref _count);
            if (delay is { } wait)
            {
                await Task.Delay(wait, token);
            }

            return Entries(name);
        };

        public Func<CancellationToken, Task<IReadOnlyList<SitemapEntry>>> Failing() => token =>
        {
            Interlocked.Increment(ref _count);
            return Task.FromException<IReadOnlyList<SitemapEntry>>(new SitemapBuildException("The product list failed (api-unavailable)."));
        };
    }

    [Fact]
    public async Task Twenty_concurrent_requests_make_one_build_and_all_get_its_result()
    {
        var cache = Cache();
        var builds = new Builds();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => cache.GetAsync(builds.Succeeding("one", TimeSpan.FromMilliseconds(150)), Ct)));

        builds.Count.ShouldBe(1);
        results.ShouldAllBe(result => result.Count == 1 && result[0].Url == "https://portal.test/one");
    }

    [Fact]
    public async Task A_second_request_within_the_lifetime_is_served_from_the_cache_without_a_build()
    {
        var cache = Cache();
        var builds = new Builds();

        await cache.GetAsync(builds.Succeeding("one"), Ct);
        var again = await cache.GetAsync(builds.Succeeding("two"), Ct);

        builds.Count.ShouldBe(1);
        again[0].Url.ShouldBe("https://portal.test/one");
    }

    [Fact]
    public async Task After_the_lifetime_the_next_request_builds_again()
    {
        var cache = Cache(ttl: Short);
        var builds = new Builds();
        await cache.GetAsync(builds.Succeeding("one"), Ct);

        await Task.Delay(Short + TimeSpan.FromMilliseconds(600), Ct);
        var again = await cache.GetAsync(builds.Succeeding("two"), Ct);

        builds.Count.ShouldBe(2);
        again[0].Url.ShouldBe("https://portal.test/two");
    }

    [Fact]
    public async Task A_crawler_that_goes_away_stops_waiting_but_the_build_finishes_and_the_next_request_is_served_from_it()
    {
        var cache = Cache();
        var builds = new Builds();
        using var crawler = new CancellationTokenSource();

        var aborted = cache.GetAsync(builds.Succeeding("one", TimeSpan.FromMilliseconds(300)), crawler.Token);
        await crawler.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => aborted);
        var next = await cache.GetAsync(builds.Succeeding("never-used"), Ct);

        builds.Count.ShouldBe(1, "the build the aborted crawler started is the one everyone else used");
        next[0].Url.ShouldBe("https://portal.test/one");
    }

    [Fact]
    public async Task The_token_the_build_gets_is_not_the_requests_and_the_build_does_not_run_in_the_requests_execution_context()
    {
        var cache = Cache();
        using var crawler = new CancellationTokenSource();
        var requestLocal = new AsyncLocal<string?> { Value = "the request" };
        CancellationToken? seen = null;
        string? localInBuild = "unset";

        await cache.GetAsync(
            token =>
            {
                seen = token;
                localInBuild = requestLocal.Value;
                return Task.FromResult(Entries("one"));
            },
            crawler.Token);

        seen.ShouldNotBeNull();
        seen.Value.ShouldNotBe(crawler.Token);
        await crawler.CancelAsync();
        seen.Value.IsCancellationRequested.ShouldBeFalse("cancelling the request never cancels the build");
        localInBuild.ShouldBeNull("the request's HttpContext holder lives in its execution context, which the build must not inherit");
    }

    [Fact]
    public async Task A_failed_build_with_nothing_earlier_fails_the_request_and_is_not_repeated_for_the_failure_lifetime()
    {
        var cache = Cache(failureTtl: Long);
        var builds = new Builds();

        await Should.ThrowAsync<SitemapBuildException>(() => cache.GetAsync(builds.Failing(), Ct));
        await Should.ThrowAsync<SitemapUnavailableException>(() => cache.GetAsync(builds.Failing(), Ct));
        await Should.ThrowAsync<SitemapUnavailableException>(() => cache.GetAsync(builds.Succeeding("one"), Ct));

        builds.Count.ShouldBe(1, "the API is not asked again while the failure is remembered");
    }

    [Fact]
    public async Task After_the_failure_lifetime_the_build_is_tried_again_and_a_success_is_cached()
    {
        var cache = Cache(failureTtl: Short);
        var builds = new Builds();
        await Should.ThrowAsync<SitemapBuildException>(() => cache.GetAsync(builds.Failing(), Ct));

        await Task.Delay(Short + TimeSpan.FromMilliseconds(600), Ct);
        var recovered = await cache.GetAsync(builds.Succeeding("one"), Ct);
        var cached = await cache.GetAsync(builds.Succeeding("two"), Ct);

        builds.Count.ShouldBe(2);
        recovered[0].Url.ShouldBe("https://portal.test/one");
        cached[0].Url.ShouldBe("https://portal.test/one");
    }

    [Fact]
    public async Task When_a_rebuild_fails_the_last_good_sitemap_is_served_during_the_failure_lifetime()
    {
        var cache = Cache(ttl: Short, failureTtl: Long);
        var builds = new Builds();
        await cache.GetAsync(builds.Succeeding("good"), Ct);
        await Task.Delay(Short + TimeSpan.FromMilliseconds(600), Ct);

        var failing = await cache.GetAsync(builds.Failing(), Ct);
        var meanwhile = await cache.GetAsync(builds.Succeeding("not-asked"), Ct);

        failing[0].Url.ShouldBe("https://portal.test/good", "the request whose rebuild failed still gets the last good one");
        meanwhile[0].Url.ShouldBe("https://portal.test/good");
        builds.Count.ShouldBe(2, "one good build and one failed rebuild; nothing else while the failure is remembered");
    }

    [Fact(Timeout = 20_000)]
    public async Task A_build_that_never_finishes_ends_after_the_timeout_and_counts_as_a_failure()
    {
        var cache = Cache(buildTimeout: TimeSpan.FromMilliseconds(200));
        var started = 0;

        await Should.ThrowAsync<OperationCanceledException>(() => cache.GetAsync(
            async token =>
            {
                Interlocked.Increment(ref started);
                await Task.Delay(Timeout.Infinite, token);
                return Entries("never");
            },
            TestContext.Current.CancellationToken));
        await Should.ThrowAsync<SitemapUnavailableException>(() => cache.GetAsync(token => Task.FromResult(Entries("not-asked")), TestContext.Current.CancellationToken));

        started.ShouldBe(1);
    }

    [Fact]
    public async Task A_new_build_can_start_after_a_failure_is_forgotten_even_when_the_first_flight_threw()
    {
        var cache = Cache(failureTtl: TimeSpan.FromMilliseconds(100));
        var builds = new Builds();
        for (var i = 0; i < 3; i++)
        {
            await Should.ThrowAsync<SitemapBuildException>(() => cache.GetAsync(builds.Failing(), Ct));
            await Task.Delay(TimeSpan.FromMilliseconds(500), Ct);
        }

        builds.Count.ShouldBe(3, "a failed flight is cleared, so it never blocks the next try");
    }

    [Fact]
    public void The_production_lifetimes_are_fifteen_minutes_one_minute_and_two_minutes()
    {
        PortalSitemapCache.DefaultTtl.ShouldBe(TimeSpan.FromMinutes(15));
        PortalSitemapCache.DefaultFailureTtl.ShouldBe(TimeSpan.FromMinutes(1));
        PortalSitemapCache.BuildTimeout.ShouldBe(TimeSpan.FromMinutes(2));
        PortalSitemap.ClientCacheDuration.ShouldBe(TimeSpan.FromMinutes(5));
    }
}
