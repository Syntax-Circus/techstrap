using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Auth;

namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// Shows its content only to an admin (D-041, Review Focus 1). The role is the API's answer in <see cref="AgentSession"/> (D-040), never a copy of the group claim.
/// While the session has not loaded yet it shows "Checking" (a session that reloads keeps Ready, so this is not a flicker); in a failed state (no access, expired, unavailable) it shows nothing, because <c>AgentGate</c> owns those screens; for a Ready agent who is not an admin it shows the
/// page-level no-access page and does not build the content at all, so a component inside it never starts, and never makes an admin call. The API still answers
/// 403 admin-access-required to every admin call.
/// <para>
/// Put the data loading in the component INSIDE the guard. A page component that loads in its own <c>OnInitializedAsync</c> runs before this guard decides, so a
/// route page is a thin shell: <c>&lt;AdminOnly&gt;&lt;ProductsList /&gt;&lt;/AdminOnly&gt;</c>.
/// </para>
/// </summary>
public sealed partial class AdminOnly : ComponentBase, IDisposable
{
    [Inject]
    private AgentSession Session { get; set; } = default!;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected override void OnInitialized() => Session.Changed += OnSessionChanged;

    private void OnSessionChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => Session.Changed -= OnSessionChanged;
}
