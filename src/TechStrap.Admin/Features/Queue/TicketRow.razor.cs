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

    /// <summary>The Spam view only: a visible Not spam button, the mouse equivalent of the <c>u</c> key.</summary>
    [Parameter]
    public bool ShowNotSpam { get; set; }

    [Parameter]
    public EventCallback<TicketRowViewModel> OnNotSpam { get; set; }
}
