using Microsoft.AspNetCore.Components;

namespace TechStrap.Admin.Components.Ui;

/// <summary>A square marker plus the priority word (never color alone). Urgent uses the spam red, High the pending amber, Normal and Low the secondary ink; Low's marker is dashed.</summary>
public partial class PriorityMark
{
    [Parameter, EditorRequired]
    public PriorityLevel Level { get; set; }

    private string CssClass => $"ts-priority ts-priority--{Level.ToString().ToLowerInvariant()}";
}
