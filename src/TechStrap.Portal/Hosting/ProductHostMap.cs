using SyntaxCircus.Common;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Hosting;

/// <summary>
/// The product hosts, read from the API's public product list and kept for <see cref="ProductHostMapOptions.Ttl"/> (PHASE-11e). One singleton; the product client is scoped, so every read opens a scope of its own.
/// <list type="bullet">
/// <item>Reads are single-flight: while one runs, the others wait for it and then use its result instead of reading again.</item>
/// <item>A host the map does not know may refresh it, but never twice inside <see cref="ProductHostMapOptions.MissRefreshInterval"/>; a failed read counts as a read, so a down API is asked once in ten seconds.</item>
/// <item>A failed read keeps the previous map (logged as a warning, nothing thrown); with no previous map every host is unknown, which is the default host's behaviour.</item>
/// </list>
/// The age is measured by the <see cref="TimeProvider"/>, so a test moves the clock instead of waiting (<c>MemoryCacheOptions</c> has no <see cref="TimeProvider"/>, so no <c>IMemoryCache</c> entry is used).
/// </summary>
public sealed class ProductHostMap(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ProductHostMap> logger)
{
    private sealed record Snapshot(IReadOnlyDictionary<string, string> KeyByHost, IReadOnlyDictionary<string, string> HostByKey, DateTimeOffset LoadedAt);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Snapshot? _snapshot;
    private DateTimeOffset? _lastAttempt;

    /// <summary>
    /// False for a host that can never be a product host: one label (<c>localhost</c>) or an IP address, which the host rule refuses. Such a request is never looked up and never redirected, so a probe, a health check
    /// and local development on <c>localhost</c> cost the API nothing and keep every page under <c>/p/{key}</c>. The host is the Host header without its port.
    /// </summary>
    public static bool CanBeProductHost(string host) => host.Contains('.', StringComparison.Ordinal) && !System.Net.IPAddress.TryParse(host, out _);

    /// <summary>The key of the product whose host this is (lower-case), from the map as it is now.</summary>
    public bool TryResolve(string host, out string key)
    {
        key = string.Empty;
        return _snapshot is { } snapshot && snapshot.KeyByHost.TryGetValue(host, out key!);
    }

    /// <summary>The host of the product with this key, or false when the product has none (or is not in the map).</summary>
    public bool TryGetHost(string key, out string host)
    {
        host = string.Empty;
        return _snapshot is { } snapshot && snapshot.HostByKey.TryGetValue(key, out host!);
    }

    /// <summary>Reads the product list now, whatever its age.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await LoadAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Makes sure the map is no older than its lifetime (a read that failed is not repeated inside the miss interval).</summary>
    public async ValueTask EnsureFreshAsync(CancellationToken cancellationToken)
    {
        if (_snapshot is { } snapshot && clock.GetUtcNow() - snapshot.LoadedAt < ProductHostMapOptions.Ttl)
        {
            return;
        }

        await RefreshIfOlderAsync(ProductHostMapOptions.MissRefreshInterval, cancellationToken);
    }

    /// <summary>
    /// The key of the product whose host this is, or null. The map is first made fresh; on a miss, and only when <paramref name="refreshOnMiss"/> is set and the last read is at least
    /// <see cref="ProductHostMapOptions.MissRefreshInterval"/> old, it is read once more (a host may have been set a moment ago).
    /// </summary>
    public async ValueTask<string?> FindKeyAsync(string host, bool refreshOnMiss, CancellationToken cancellationToken)
    {
        var lookup = host.ToLowerInvariant();
        await EnsureFreshAsync(cancellationToken);
        if (TryResolve(lookup, out var key))
        {
            return key;
        }

        if (!refreshOnMiss)
        {
            return null;
        }

        await RefreshIfOlderAsync(ProductHostMapOptions.MissRefreshInterval, cancellationToken);
        return TryResolve(lookup, out key) ? key : null;
    }

    private async ValueTask RefreshIfOlderAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Whoever waited on the gate finds the read that was running and does not repeat it.
            if (_lastAttempt is { } last && clock.GetUtcNow() - last < interval)
            {
                return;
            }

            await LoadAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        _lastAttempt = clock.GetUtcNow();
        try
        {
            using var scope = scopes.CreateScope();
            var listed = await scope.ServiceProvider.GetRequiredService<IPublicProductClient>().ListAsync(cancellationToken);
            if (listed.IsFailure)
            {
                logger.LogWarning("The product hosts could not be read ({Code}); the previous map is kept.", listed.Errors[0].Code);
                return;
            }

            _snapshot = Build(listed.Value, clock.GetUtcNow());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "The product hosts could not be read; the previous map is kept.");
        }
    }

    private static Snapshot Build(IReadOnlyList<PublicProductSummaryDto> products, DateTimeOffset loadedAt)
    {
        var keyByHost = new Dictionary<string, string>(StringComparer.Ordinal);
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
