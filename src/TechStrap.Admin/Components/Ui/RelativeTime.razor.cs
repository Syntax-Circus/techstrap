using Microsoft.AspNetCore.Components;

namespace TechStrap.Admin.Components.Ui;

/// <summary>"5 min ago" as visible text, with the machine-readable <c>datetime</c> and the absolute UTC time in the tooltip.</summary>
public partial class RelativeTime
{
    [Inject]
    private TimeProvider Time { get; set; } = default!;

    [Parameter, EditorRequired]
    public DateTimeOffset When { get; set; }
}
