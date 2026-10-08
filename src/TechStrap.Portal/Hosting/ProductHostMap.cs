using SyntaxCircus.Common;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Hosting;

/// <summary>
/// The product hosts, read from the API's public product list and kept for <see cref="ProductHostMapOptions.Ttl"/> (PHASE-11e). One singleton; the product client is scoped, so every read opens a scope of its own.
/// <list type="bullet">
/// <item><b>Stale-while-revalidate.</b> Once there is a map, every lookup answers from it at once; an expired map starts one background read and nobody waits for it. Only the very first lookup (no map yet) waits.</item>
/// <item><b>One read at a time, on its own token.</b> The read runs as a task of its own, without the request's execution context and without any request's token: a caller that gives up stops waiting but cannot cancel it.</item>
/// <item><b>A host the map does not know</b> may start a read, but never twice inside <see cref="ProductHostMapOptions.MissRefreshInterval"/>; that window is checked before the lock is taken, so a miss inside it costs nothing. A failed read counts as a read.</item>
/// <item>A failed read keeps the previous map (logged as a warning, nothing thrown); with no previous map every host is unknown, which is the default host's behaviour.</item>
/// </list>
/// The age is measured by the <see cref="TimeProvider"/>, so a test moves the clock instead of waiting (<c>MemoryCacheOptions</c> has no <see cref="TimeProvider"/>, so no <c>IMemoryCache</c> entry is used).
/// </summary>
public sealed class ProductHostMap(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ProductHostMap> logger, IHttpContextAccessor accessor)
{
    private sealed record Snapshot(IReadOnlyDictionary<string, string> KeyByHost, IReadOnlyDictionary<string, string> HostByKey, DateTimeOffset LoadedAt);

    private readonly Lock _lock = new();
    private volatile Snapshot? _snapshot;
    private volatile Task _flight = Task.CompletedTask;
    private long _lastAttemptTicks;

    /// <summary>The read that is running, or a completed task. For tests, which wait for it instead of sleeping.</summary>
    internal Task PendingRefresh => _flight;

    /// <summary>
    /// False for a host that can never be a product host: one label (<c>localhost</c>) or an IP address, which the host rule refuses. Such a request is never looked up and never redirected, so a probe, a health check
    /// and local development on <c>localhost</c> cost the API nothing and keep every page under <c>/p/{key}</c>. The host is the Host header without its port.
    /// </summary>
    public static bool CanBeProductHost(string host) => host.Contains('.', StringComparison.Ordinal) && !System.Net.IPAddress.TryParse(host, out _);

    /// <summary>The key of the product whose host this is (any case), from the map as it is now.</summary>
    public bool TryResolve(string host, out string key)
    {
        key = string.Empty;
        return _snapshot is { } snapshot && snapshot.KeyByHost.TryGetValue(host, out key!);
    }

    /// <summary>The host of the product with this key (any case), or false when the product has none (or is not in the map).</summary>
    public bool TryGetHost(string key, out string host)
    {
        host = string.Empty;
        return _snapshot is { } snapshot && snapshot.HostByKey.TryGetValue(key, out host!);
    }

    /// <summary>Reads the product list now, whatever its age (or joins the read that is running), and waits for it.</summary>
    public Task RefreshAsync(CancellationToken cancellationToken)
    {
        Task flight;
        lock (_lock)
        {
            flight = _flight.IsCompleted ? Start() : _flight;
        }

        return flight.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// With a map, returns at once (an expired map starts one background read, unawaited). With no map yet, waits for the first read, or for the one that is running; a read that failed is not repeated inside the miss
    /// interval, so a down API is asked once in ten seconds.
    /// </summary>
    public async ValueTask EnsureFreshAsync(CancellationToken cancellationToken)
    {
        var snapshot = _snapshot;
        if (snapshot is null)
        {
            await (Begin() ?? _flight).WaitAsync(cancellationToken);
            return;
        }

        if (clock.GetUtcNow() - snapshot.LoadedAt >= ProductHostMapOptions.Ttl)
        {
            _ = Begin();
        }
    }

    /// <summary>
    /// The key of the product whose host this is, or null. On a miss the map is read once more (a host may have been set a moment ago) by the request that finds the read allowed: never twice inside
    /// <see cref="ProductHostMapOptions.MissRefreshInterval"/>, and a request that finds one running does not wait for it.
    /// </summary>
    public async ValueTask<string?> FindKeyAsync(string host, CancellationToken cancellationToken)
    {
        await EnsureFreshAsync(cancellationToken);
        if (TryResolve(host, out var key))
        {
            return key;
        }

        if (Begin() is { } read)
        {
            await read.WaitAsync(cancellationToken);
            return TryResolve(host, out key) ? key : null;
        }

        return null;
    }

    // Starts a read unless one is running or the last one began less than the miss interval ago; null then. The interval is checked before the lock, so a miss inside it never touches the lock.
    private Task? Begin()
    {
        if (WithinMissInterval())
        {
            return null;
        }

        lock (_lock)
        {
            return _flight.IsCompleted && !WithinMissInterval() ? Start() : null;
        }
    }

    private bool WithinMissInterval()
    {
        var last = Volatile.Read(ref _lastAttemptTicks);
        return last != 0 && clock.GetUtcNow().UtcTicks - last < ProductHostMapOptions.MissRefreshInterval.Ticks;
    }

    // Called with the lock held. The request's HttpContext lives in an AsyncLocal that the new task would inherit and the read must not touch, so the flow is suppressed for the start only.
    private Task Start()
    {
        Volatile.Write(ref _lastAttemptTicks, clock.GetUtcNow().UtcTicks);

        // The API call carries the address of the visitor whose request started it (as the sitemap build does): taken here, handed to the read as a context of its own.
        var visitor = accessor.HttpContext?.Connection.RemoteIpAddress;
        using (ExecutionContext.SuppressFlow())
        {
            _flight = Task.Run(() => LoadAsync(visitor));
        }

        return _flight;
    }

    private async Task LoadAsync(System.Net.IPAddress? visitor)
    {
        try
        {
            using var scope = scopes.CreateScope();
            if (scope.ServiceProvider.GetService<IHttpContextAccessor>() is { } scoped)
            {
                scoped.HttpContext = new DefaultHttpContext { Connection = { RemoteIpAddress = visitor } };
            }

            var listed = await scope.ServiceProvider.GetRequiredService<IPublicProductClient>().ListAsync(CancellationToken.None);
            if (listed.IsFailure)
            {
                logger.LogWarning("The product hosts could not be read ({Code}); the previous map is kept.", listed.Errors[0].Code);
                return;
            }

            _snapshot = Build(listed.Value, clock.GetUtcNow());
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The product hosts could not be read; the previous map is kept.");
        }
    }

    private static Snapshot Build(IReadOnlyList<PublicProductSummaryDto> products, DateTimeOffset loadedAt)
    {
        var keyByHost = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hostByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var product in products)
        {
            if (string.IsNullOrWhiteSpace(product.PortalHost))
            {
                continue;
            }

            var host = product.PortalHost.Trim().ToLowerInvariant();

            // Hosts are unique in the API; if one is ever listed twice, the first product keeps it.
            if (keyByHost.TryAdd(host, product.Key))
            {
                hostByKey[product.Key] = host;
            }
        }

        return new Snapshot(keyByHost, hostByKey, loadedAt);
    }
}
