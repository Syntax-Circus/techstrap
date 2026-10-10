using TechStrap.Contracts.Skins;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Hosting;

namespace TechStrap.Portal.Products;

/// <summary>
/// The key of the deployment's default theme pack (D-053), read from the API and kept for <see cref="ProductHostMapOptions.Ttl"/>. One singleton; the client is scoped, so every read opens a scope of its own.
/// <list type="bullet">
/// <item><b>Stale-while-revalidate.</b> With a value, every call answers at once; an expired value starts one background read and nobody waits for it. Only the very first call (nothing read yet) waits.</item>
/// <item><b>Never an error.</b> A failed read keeps the last good value; with none the answer is <see cref="SkinPacks.DefaultKey"/>, and a key the Portal does not know is Classic too. A read that failed is not repeated
/// inside <see cref="ProductHostMapOptions.MissRefreshInterval"/>, so a down API costs one call in ten seconds, not one per page.</item>
/// <item><b>One read at a time, on its own deadline.</b> The read runs as a task of its own, without the request's execution context or token (a hung call is cut off after <see cref="ProductHostMapOptions.ReadTimeout"/>).</item>
/// </list>
/// The age is measured by the <see cref="TimeProvider"/>, so a test moves the clock instead of waiting.
/// </summary>
public sealed class DefaultPackProvider(IServiceScopeFactory scopes, TimeProvider clock, ILogger<DefaultPackProvider> logger, IHttpContextAccessor? accessor = null)
{
    private sealed record Snapshot(string Pack, DateTimeOffset LoadedAt);

    private readonly Lock _lock = new();
    private volatile Snapshot? _snapshot;
    private volatile Task _flight = Task.CompletedTask;
    private long _lastAttemptTicks;

    /// <summary>The read that is running, or a completed task. For tests, which wait for it instead of sleeping.</summary>
    internal Task PendingRefresh => _flight;

    /// <summary>The default pack key: always a key <see cref="SkinPacks.IsKnown"/> accepts.</summary>
    public async ValueTask<string> GetAsync(CancellationToken cancellationToken)
    {
        var snapshot = _snapshot;
        if (snapshot is null)
        {
            await (Begin() ?? _flight).WaitAsync(cancellationToken);
            return _snapshot?.Pack ?? SkinPacks.DefaultKey;
        }

        if (clock.GetUtcNow() - snapshot.LoadedAt >= ProductHostMapOptions.Ttl)
        {
            _ = Begin();
        }

        return snapshot.Pack;
    }

    // Starts a read unless one is running or the last one began less than the miss interval ago; null then.
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

        // The API call carries the address of the visitor whose request started it (as the product host map does): taken here, handed to the read as a context of its own.
        var visitor = accessor?.HttpContext?.Connection.RemoteIpAddress;
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

            using var deadline = new CancellationTokenSource(ProductHostMapOptions.ReadTimeout, clock);
            var read = await scope.ServiceProvider.GetRequiredService<ISiteSettingsClient>().GetAsync(deadline.Token);
            if (read.IsFailure)
            {
                logger.LogWarning("The default theme pack could not be read ({Code}); the previous value is kept.", read.Errors[0].Code);
                return;
            }

            // A key the Portal does not know (a newer API, a corrupt row) is Classic, and is logged by the fact only, never by the value.
            var key = read.Value.DefaultPack;
            if (!SkinPacks.IsKnown(key))
            {
                logger.LogWarning("The default theme pack is not one this Portal knows; Classic is used.");
                key = SkinPacks.DefaultKey;
            }

            _snapshot = new Snapshot(key, clock.GetUtcNow());
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The default theme pack could not be read; the previous value is kept.");
        }
    }
}
