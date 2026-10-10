using Microsoft.AspNetCore.Components;

namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// One message in a ticket timeline, drawn with the carbon tint code: white customer message, canary public reply, pink dashed
/// notched internal note. Every kind also carries a word (customer, agent reply, INTERNAL NOTE), so color is never the only cue.
/// </summary>
public partial class TintedEntry
{
    private const string InternalNoteLabel = "INTERNAL NOTE";

    [Parameter, EditorRequired]
    public EntryKind Kind { get; set; }

    [Parameter, EditorRequired]
    public string Author { get; set; } = string.Empty;

    /// <summary>The time as the caller wants it shown (already formatted). Ignored when <see cref="When"/> is set.</summary>
    [Parameter]
    public string Time { get; set; } = string.Empty;

    /// <summary>The instant, shown as a <see cref="RelativeTime"/> (local time, UTC in the tooltip). Messages use this; the style guide's fixed sample times use <see cref="Time"/>.</summary>
    [Parameter]
    public DateTimeOffset? When { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    private string CssClass => Kind switch
    {
        EntryKind.Customer => "ts-entry ts-entry--customer",
        EntryKind.PublicReply => "ts-entry ts-entry--public",
        EntryKind.InternalNote => "ts-entry ts-entry--note",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unknown entry kind."),
    };

    private string RoleLabel => Kind == EntryKind.Customer ? "customer" : "agent reply";
}
