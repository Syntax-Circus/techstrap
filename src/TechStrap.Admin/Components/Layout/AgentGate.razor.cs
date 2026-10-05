using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TechStrap.Admin.Auth;

namespace TechStrap.Admin.Components.Layout;

/// <summary>
/// Shows its content only to an agent the API accepts (Review Focus 1). For a signed-in user it asks <see cref="AgentSession"/> (<c>GET /api/agents/me</c>) before
/// rendering anything inside it, so no ticket page can start a call, and the no-access page replaces the content on any 403. For an anonymous visitor
/// (the error and not-found pages) and on any static page it renders the content directly.
/// </summary>
public sealed partial class AgentGate : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private bool? _authenticated;

    [Inject]
    private AgentSession Session { get; set; } = default!;

    [Inject]
    private ILogger<AgentGate> Logger { get; set; } = default!;

    [CascadingParameter]
    private HttpContext? HttpContext { get; set; }

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationState { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// True on a statically rendered page (/error, /not-found: <c>ExcludeFromInteractiveRouting</c>), where there is no circuit, so nothing could answer the
    /// Retry button. Those pages hold no agent data: the gate shows their content directly and does not ask the API. Decided from the request's endpoint,
    /// the same test App.razor uses to pick the render mode; there is no HttpContext in a circuit, so an interactive page is never static.
    /// </summary>
    private bool IsStaticPage => HttpContext is not null && !HttpContext.AcceptsInteractiveRouting();

    protected override async Task OnInitializedAsync()
    {
        Session.Changed += OnSessionChanged;
        if (AuthenticationState is null)
        {
            return;
        }

        var state = await AuthenticationState;
        _authenticated = state.User.Identity?.IsAuthenticated == true;
        if (_authenticated == true && !IsStaticPage)
        {
            try
            {
                await Session.EnsureLoadedAsync(_lifetime.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                // The circuit or the layout went away while the read was in flight.
            }
            catch (Exception ex)
            {
                // An unexpected fault (not an API answer, which the session already maps): degrade to the Unavailable state with its Retry button. Only the type
                // is logged, never the message, which can carry an address or a name.
                Logger.LogWarning("The agent session could not be loaded ({ExceptionType}).", ex.GetType().Name);
                Session.MarkUnavailable();
            }
        }
    }

    private Task RetryAsync() => Session.ReloadAsync(_lifetime.Token);

    private void OnSessionChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        Session.Changed -= OnSessionChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
