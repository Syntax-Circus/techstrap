using Microsoft.AspNetCore.Components;

namespace TechStrap.Admin.Features.Queue;

/// <summary>
/// One ledger row: marker, number, subject with tag chips, product, requester, status stamp (straight), priority, assignee and last activity.
/// Parameters only.
/// </summary>
public sealed partial class TicketRow
{
    [Parameter, EditorRequired]
    public TicketRowViewModel Row { get; set; } = default!;

    /// <summary>The keyboard selection (<c>j</c>/<c>k</c>); it never reorders rows.</summary>
    [Parameter]
    public bool Selected { get; set; }
}
