using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Components.Layout;

/// <summary>
/// The left rail. It shows the agent's links only once <see cref="AgentSession"/> is Ready, so the anonymous pages that share <c>MainLayout</c> (not found, error),
/// and a user the API refused, see the brand alone. It re-renders when the session changes (the gate loads it after the layout first renders). Every agent sees
/// Queue and My settings; the admin links (Products, Agents, Tags, Audit, Failed emails with a count badge) show only for <see cref="AgentSession.IsAdmin"/>, the API's
/// answer (D-040, D-041). Hiding a link is not access control: each admin page is wrapped in <c>AdminOnly</c> and the API answers 403 to every admin call. The badge call
/// is made only for an admin, after the first render (never while prerendering), and only once. Sign out is the shared <c>SignOutForm</c>.
/// </summary>
public sealed partial class NavMenu : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private bool _badgeRequested;
    private bool _disposed;

    [Inject]
    private AgentSession Session { get; set; } = default!;

    [Inject]
    private ILogger<NavMenu> Logger { get; set; } = default!;

    [Inject]
    private FailedEmailCounter Failed { get; set; } = default!;

    private string DisplayName => Session.Agent is { } agent ? (string.IsNullOrWhiteSpace(agent.Name) ? agent.Email : agent.Name) : string.Empty;

    protected override void OnInitialized()
    {
        Session.Changed += OnChanged;
        Failed.Changed += OnChanged;
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
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
