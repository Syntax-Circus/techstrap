using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Components.Layout;

/// <summary>
/// The left rail. It shows the agent's links only once <see cref="AgentSession.IsAdmitted"/> (Ready, or expired while working), so the anonymous pages that share <c>MainLayout</c> (not found, error),
/// and a user the API refused, see the brand alone. It re-renders when the session changes (the gate loads it after the layout first renders). Every agent sees
/// Queue and My settings; the admin links (Products, Agents, Tags, Audit, Failed emails with a count badge) show only for <see cref="AgentSession.IsAdmin"/>, the API's
/// answer (D-040, D-041). Hiding a link is not access control: each admin page is wrapped in <c>AdminOnly</c> and the API answers 403 to every admin call. The badge call
/// is made only for an admin, after the first render (never while prerendering), and only once. Sign out is the shared <c>SignOutForm</c>.
/// </summary>
public sealed partial class NavMenu : IDisposable, IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private bool _badgeRequested;
    private bool _disposed;
    private bool _railOpen;
    private ElementReference _toggle;
    private ElementReference _panel;
    private IJSObjectReference? _menuScript;

    [Inject]
    private AgentSession Session { get; set; } = default!;

    [Inject]
    private ILogger<NavMenu> Logger { get; set; } = default!;

    [Inject]
    private FailedEmailCounter Failed { get; set; } = default!;

    [Inject]
    private IJSRuntime Js { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private const string MenuModule = "./js/menu.js";

    private static string PanelId => "ts-rail-panel";

    private void ToggleRail() => _railOpen = !_railOpen;

    private string DisplayName => Session.Agent is { } agent ? (string.IsNullOrWhiteSpace(agent.Name) ? agent.Email : agent.Name) : string.Empty;

    protected override void OnInitialized()
    {
        Session.Changed += OnChanged;
        Failed.Changed += OnChanged;
        Navigation.LocationChanged += OnLocationChanged;
    }

    // Choosing a link folds the rail away again, so the page that was chosen is not left behind the menu on a phone.
    private void OnLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e)
    {
        if (_railOpen)
        {
            _ = CloseRailAfterNavigationAsync();
        }
    }

    /// <summary>
    /// The panel is hidden when it folds away, and a hidden element cannot keep focus, so the browser would drop it on the page. When focus is inside the panel (the link that was chosen)
    /// it is handed to the Menu button first; focus that is somewhere else is left alone. The rail folds even when the script cannot run.
    /// </summary>
    private async Task CloseRailAfterNavigationAsync()
    {
        try
        {
            _menuScript ??= await Js.InvokeAsync<IJSObjectReference>("import", MenuModule);
            await _menuScript.InvokeVoidAsync("focusIfWithin", _panel, _toggle);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException)
        {
            // Focus is a courtesy, not worth an error; log the type only.
            Logger.LogWarning("The menu button could not take focus ({ExceptionType}).", ex.GetType().Name);
        }

        if (!_disposed && _railOpen)
        {
            _railOpen = false;
            OnChanged();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // A plain agent never asks: the dead letters call is a 403 for them. Ready is only true after the API answered, so there is no admin guess here.
        if (_badgeRequested || !Session.IsAdmin)
        {
            return;
        }

        _badgeRequested = true;
        try
        {
            await Failed.RefreshAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The circuit or the layout went away while the read was in flight.
        }
        catch (Exception ex)
        {
            // The rail sits outside every error boundary, so an exception out of OnAfterRenderAsync would end the circuit. A badge is not worth that: the count stays
            // as it was and the rail keeps working. Only the type is logged, never the message.
            Logger.LogWarning("The failed-email badge could not be refreshed ({ExceptionType}).", ex.GetType().Name);
        }
    }

    private void OnChanged()
    {
        if (!_disposed)
        {
            _ = InvokeAsync(StateHasChanged);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Session.Changed -= OnChanged;
        Failed.Changed -= OnChanged;
        Navigation.LocationChanged -= OnLocationChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        if (_menuScript is null)
        {
            return;
        }

        try
        {
            await _menuScript.DisposeAsync();
        }
        catch (Exception ex) when (ex is JSDisconnectedException or JSException or TaskCanceledException)
        {
            // The circuit is already gone.
        }
    }
}
