using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using TechStrap.Admin.Auth;

namespace TechStrap.Admin.Features.Live;

/// <summary>
/// The live-connection indicator in <c>MainLayout</c> and the one component that starts the connection. The layout sits outside the agent gate, so it draws and starts only when the session is Ready.
/// The start is in <c>OnAfterRenderAsync</c> on purpose: prerendering (the pages render once on the HTTP request, with no circuit) never runs it, so only a real circuit ever opens a connection, and the
/// client's own start guard keeps it to one however often this runs. The status region is polite: a change of state is announced, never shouted.
/// </summary>
public sealed partial class LiveConnectionIndicator : IDisposable
{
    private bool _started;
    private bool _disposed;

    [Inject]
    private ITicketLiveClient LiveClient { get; set; } = default!;

    [Inject]
    private AgentSession Session { get; set; } = default!;

    [Inject]
    private ILogger<LiveConnectionIndicator> Logger { get; set; } = default!;

    private bool Visible => LiveClient.IsEnabled && Session.State == AgentSessionState.Ready;

    private string StateKey => LiveClient.State.ToString().ToLowerInvariant();

    protected override void OnInitialized()
    {
        LiveClient.StateChanged += OnStateChanged;
        Session.Changed += OnSessionChanged;
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (!_started && Visible)
        {
            _started = true;
            _ = StartAsync();
        }
    }

    /// <summary>Never awaited by the render and never throws: a live failure shows as the indicator's state, not as an error in the page.</summary>
    private async Task StartAsync()
    {
        try
        {
            await LiveClient.StartAsync();
        }
        catch (Exception exception)
        {
            Logger.LogWarning("The live connection could not be started ({ExceptionType}).", exception.GetType().Name);
        }
    }

    private void OnStateChanged(LiveConnectionState state) => Refresh();

    private void OnSessionChanged() => Refresh();

    private void Refresh()
    {
        if (!_disposed)
        {
            _ = InvokeAsync(StateHasChanged);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        LiveClient.StateChanged -= OnStateChanged;
        Session.Changed -= OnSessionChanged;
    }
}
