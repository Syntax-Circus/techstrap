using Microsoft.JSInterop;

namespace TechStrap.Admin.Features.Shell;

/// <summary>The color theme choice. <see cref="Auto"/> follows the operating system and is the default (BRAND.md: Light, Dark, Auto).</summary>
public enum ThemeChoice
{
    Auto,
    Light,
    Dark,
}

/// <summary>What <c>preferences.js</c> <c>load()</c> returns. The theme is the lower-case value the script stores: auto, light or dark.</summary>
public sealed record StoredPreferences(bool SingleKeyShortcuts, string Theme);

/// <summary>
/// The agent's browser preferences (UX-BRIEF-admin, My settings): the single-key keyboard shortcuts and the theme, kept in the browser by <c>wwwroot/js/preferences.js</c>.
/// <see cref="LoadAsync"/> runs once on the first interactive render (<c>MainLayout</c>): it reads the stored values, applies the theme to the page and sets
/// <see cref="ShortcutService.SingleKeyEnabled"/>. The setters change the live value first, then store it, so a blocked or full storage never undoes the choice for this
/// page. No call here throws for a script or storage failure: a page that cannot remember a preference still works. Scoped: one per circuit.
/// </summary>
public sealed class PreferencesService(IJSRuntime js, ShortcutService shortcuts) : IAsyncDisposable
{
    public const string ModulePath = "./js/preferences.js";

    private const string SingleKeyKey = "singleKeyShortcuts";
    private const string ThemeKey = "theme";

    private Task<IJSObjectReference?>? _import;
    private Task? _loading;
    private bool _disposed;
    private bool _themeSet;
    private bool _singleKeySet;

    /// <summary>True once <see cref="LoadAsync"/> has finished (with the stored values or, when the script was unavailable, the defaults).</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>The current theme choice.</summary>
    public ThemeChoice Theme { get; private set; } = ThemeChoice.Auto;

    /// <summary>The single-key shortcuts switch. It lives on <see cref="ShortcutService.SingleKeyEnabled"/>, which the key listener reads on every key press.</summary>
    public bool SingleKeyShortcuts => shortcuts.SingleKeyEnabled;

    /// <summary>Raised after a value changed or the stored values were loaded, so My settings can show the current choice.</summary>
    public event Action? Changed;

    /// <summary>Loads once; concurrent and repeated callers share the first call. Never throws for a script or storage failure.</summary>
    public Task LoadAsync() => _loading ??= LoadCoreAsync();

    private async Task LoadCoreAsync()
    {
        try
        {
            var module = await ImportAsync();
            if (module is null)
            {
                return;
            }

            var stored = await module.InvokeAsync<StoredPreferences>("load");

            // A choice the agent made while the stored values were on their way is newer than they are and wins.
            if (!_disposed && !_singleKeySet)
            {
                shortcuts.SingleKeyEnabled = stored.SingleKeyShortcuts;
            }

            if (!_disposed && !_themeSet)
            {
                Theme = ParseTheme(stored.Theme);
            }
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // No script, no circuit or a prerender: keep the defaults. The agent loses nothing but a remembered choice.
        }

        if (_disposed)
        {
            return;
        }

        IsLoaded = true;
        Changed?.Invoke();
    }

    /// <summary>Turns the single-key shortcuts on or off (WCAG 2.1.4) and remembers the choice.</summary>
    public async Task SetSingleKeyShortcutsAsync(bool enabled)
    {
        _singleKeySet = true;
        shortcuts.SingleKeyEnabled = enabled;
        await SaveAsync(SingleKeyKey, enabled);
        if (!_disposed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Applies a theme to the page now and remembers the choice. A value that is not one of the three choices is ignored.</summary>
    public async Task SetThemeAsync(ThemeChoice theme)
    {
        if (!Enum.IsDefined(theme))
        {
            return;
        }

        _themeSet = true;
        Theme = theme;
        await SaveAsync(ThemeKey, theme.ToString().ToLowerInvariant());
        if (!_disposed)
        {
            Changed?.Invoke();
        }
    }

    private async Task SaveAsync(string key, object value)
    {
        try
        {
            var module = await ImportAsync();
            if (module is not null)
            {
                await module.InvokeAsync<bool>("save", key, value);
            }
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException)
        {
            // Not stored: the choice holds for this page and the next visit starts from the defaults.
        }
    }

    /// <summary>
    /// One import shared by a concurrent load and setter. Null when the service was disposed while the import was in flight. A failed import is forgotten
    /// (only if no newer attempt replaced it), so the next call tries again, for example once the circuit is ready.
    /// </summary>
    private async Task<IJSObjectReference?> ImportAsync()
    {
        var import = _import ??= ImportCoreAsync();
        try
        {
            return await import;
        }
        catch
        {
            if (ReferenceEquals(_import, import))
            {
                _import = null;
            }

            throw;
        }
    }

    private async Task<IJSObjectReference?> ImportCoreAsync()
    {
        var module = await js.InvokeAsync<IJSObjectReference>("import", ModulePath);

        if (!_disposed)
        {
            return module;
        }

        await DisposeModuleAsync(module);
        return null;
    }

    // Only the exact names count: a numeric string such as "1" must not parse into the Light value.
    private static ThemeChoice ParseTheme(string? value) => value?.ToLowerInvariant() switch
    {
        "light" => ThemeChoice.Light,
        "dark" => ThemeChoice.Dark,
        _ => ThemeChoice.Auto,
    };

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_import is null)
        {
            return;
        }

        IJSObjectReference? module;
        try
        {
            module = await _import;
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException)
        {
            return;
        }

        // An import that finished after the disposal has already disposed its own module and answered null.
        if (module is not null)
        {
            await DisposeModuleAsync(module);
        }
    }

    private static async ValueTask DisposeModuleAsync(IJSObjectReference module)
    {
        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The circuit is already gone.
        }
    }
}
