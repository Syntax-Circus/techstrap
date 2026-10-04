using Microsoft.AspNetCore.Components;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Tickets;

/// <summary>
/// The tags on one ticket: chips with a remove button, and a select offering the tags it does not have yet. It reports an add or a remove and nothing else; the sidebar owns
/// the request, the RowVersion and the error. It uses <see cref="TagDto"/> directly (the recorded direct-DTO decision: read-only display, no reshaping).
/// </summary>
public sealed partial class TagPicker
{
    private static int _nextId;

    private readonly int _id = Interlocked.Increment(ref _nextId);
    private int _rev;

    [Parameter, EditorRequired]
    public IReadOnlyList<TicketTagDto> Selected { get; set; } = [];

    [Parameter, EditorRequired]
    public IReadOnlyList<TagDto> Available { get; set; } = [];

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public EventCallback<Guid> OnAdd { get; set; }

    [Parameter]
    public EventCallback<Guid> OnRemove { get; set; }

    private string AddId => $"ts-tag-add-{_id}";

    private IReadOnlyList<TagDto> Addable => Available.Where(tag => Selected.All(s => s.Id != tag.Id)).ToList();

    private async Task OnPickedAsync(ChangeEventArgs e)
    {
        // Back to the placeholder whatever the outcome, so the select never shows a tag that was not added.
        _rev++;
        if (Guid.TryParse(e.Value as string, out var id))
        {
            await OnAdd.InvokeAsync(id);
        }
    }
}
