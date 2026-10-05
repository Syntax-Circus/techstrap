using System.Text.RegularExpressions;
using Microsoft.JSInterop;

namespace TechStrap.Admin.Features.Shell;

/// <summary>
/// The agent's time zone (D-042). The browser knows it (<c>Intl</c>, through <c>wwwroot/js/tz.js</c>); the server does not, and the API has no zone setting, so the zone is read once per
/// circuit after the first interactive render. Until then, and whenever the browser gives no usable zone, every time is UTC, which is also what the prerender shows. When the zone arrives,
/// <see cref="Changed"/> fires and <c>RelativeTime</c> draws again. The browser reports an IANA name; <see cref="TimeZoneInfo.FindSystemTimeZoneById(string)"/> reads IANA names on Linux and,
/// with ICU, on Windows. A name that is not found, or not shaped like an IANA name, is UTC. No call here throws for a script failure. Scoped: one per circuit.
/// </summary>
public sealed partial class LocalTimeService(IJSRuntime js) : IAsyncDisposable
{
    public const string ModulePath = "./js/tz.js";

    private const int MaxZoneIdLength = 64;

    private Task? _loading;
    private bool _disposed;
    private IJSObjectReference? _module;

    /// <summary>The agent's zone, or UTC until it is known (and whenever it cannot be known).</summary>
    public TimeZoneInfo Zone { get; private set; } = TimeZoneInfo.Utc;

    /// <summary>Raised after the zone was loaded, so every time on screen can draw again.</summary>
    public event Action? Changed;

    /// <summary>Loads once; concurrent and repeated callers share the first call. Never throws for a script failure.</summary>
    public Task LoadAsync() => _loading ??= LoadCoreAsync();

    private async Task LoadCoreAsync()
    {
        string? name = null;
        try
        {
            _module = await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            name = await _module.InvokeAsync<string?>("zone");
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // No script, no circuit or a prerender: keep UTC. The agent loses nothing but the local clock.
        }

        if (_disposed)
        {
            return;
        }

        Zone = Resolve(name);
        Changed?.Invoke();
    }

    /// <summary>The zone for an IANA name, or UTC for anything else: null, blank, too long, not shaped like a name, or not known to this machine.</summary>
    public static TimeZoneInfo Resolve(string? id)
    {
        if (id is null || id.Length > MaxZoneIdLength || !ZoneIdShape().IsMatch(id))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException or System.Security.SecurityException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    // Letters, digits, underscore, hyphen, plus and slash: Europe/London, America/Port-au-Prince, Etc/GMT+5. Never a dot or a backslash.
    [GeneratedRegex("^[A-Za-z0-9_+/-]+\\z", RegexOptions.CultureInvariant)]
    private static partial Regex ZoneIdShape();

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The circuit is already gone.
        }
    }
}
