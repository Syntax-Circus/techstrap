using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using TechStrap.Admin.Auth;

namespace TechStrap.Admin.Components.Layout;

/// <summary>
/// The non-destructive notice <see cref="AgentGate"/> shows when <see cref="AgentSession.ExpiredWhileWorking"/> is true. The link goes to
/// <c>/signin/start?returnUrl=</c> with the current local address (path and query), checked again by <see cref="LocalReturnUrl.Sanitize"/> on the server, so it can
/// never name another host. It follows the agent as they navigate, so signing in again returns them to wherever they are.
/// </summary>
public sealed partial class SessionExpiredBanner : ComponentBase, IDisposable
{
    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private string SignInHref
    {
        get
        {
            var local = LocalReturnUrl.Sanitize("/" + Navigation.ToBaseRelativePath(Navigation.Uri));
            return $"{AdminAuthentication.SignInStartPath}?{AdminAuthentication.ReturnUrlParameter}={Uri.EscapeDataString(local)}";
        }
    }

    protected override void OnInitialized() => Navigation.LocationChanged += OnLocationChanged;

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => Navigation.LocationChanged -= OnLocationChanged;
}
