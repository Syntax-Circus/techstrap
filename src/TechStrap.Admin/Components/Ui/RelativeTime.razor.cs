using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// "5 min ago" as visible text, with the machine-readable <c>datetime</c> (always UTC) and the absolute time in the tooltip: the agent's local time and the UTC time. The prerender
/// and a circuit that has not learned the browser's zone yet show UTC; when <see cref="LocalTimeService"/> loads the zone, this draws again.
/// </summary>
public partial class RelativeTime
{
    [Inject]
    private TimeProvider Time { get; set; } = default!;

    [Inject]
    private LocalTimeService LocalTime { get; set; } = default!;

    [Parameter, EditorRequired]
    public DateTimeOffset When { get; set; }

    protected override void OnInitialized() => LocalTime.Changed += OnZoneChanged;

    private void OnZoneChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => LocalTime.Changed -= OnZoneChanged;
}
