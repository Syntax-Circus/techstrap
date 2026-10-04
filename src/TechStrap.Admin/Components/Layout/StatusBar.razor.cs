using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Components.Layout;

/// <summary>The footer: the current shortcut hints and one polite message slot (<c>role="status"</c>) for short confirmations.</summary>
public partial class StatusBar : IDisposable
{
    [Inject]
    private StatusMessageService Messages { get; set; } = default!;

    protected override void OnInitialized() => Messages.Changed += OnChanged;

    private void OnChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => Messages.Changed -= OnChanged;
}
