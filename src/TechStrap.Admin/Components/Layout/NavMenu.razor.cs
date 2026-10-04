using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Components.Ui;

namespace TechStrap.Admin.Components.Layout;

/// <summary>
/// The left rail. It shows the agent's links only once <see cref="AgentSession"/> is Ready, so the anonymous pages that share <c>MainLayout</c> (not found, error),
/// and a user the API refused, see the brand alone. It re-renders when the session changes (the gate loads it after the layout first renders).
/// The Admin-only links (settings, dead letters) arrive in PHASE-07b. Sign out is the shared <c>SignOutForm</c>.
/// </summary>
public sealed partial class NavMenu : IDisposable
{
    [Inject]
    private AgentSession Session { get; set; } = default!;

    private string DisplayName => Session.Agent is { } agent ? (string.IsNullOrWhiteSpace(agent.Name) ? agent.Email : agent.Name) : string.Empty;

    protected override void OnInitialized() => Session.Changed += OnSessionChanged;

    private void OnSessionChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => Session.Changed -= OnSessionChanged;
}
