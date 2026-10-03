using Microsoft.AspNetCore.Components;

namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// One message in a ticket timeline, drawn with the carbon tint code: white customer message, canary public reply, pink dashed
/// notched internal note. Every kind also carries a word (customer, agent reply, INTERNAL NOTE), so colour is never the only cue.
/// </summary>
public partial class TintedEntry
{
    private const string InternalNoteLabel = "INTERNAL NOTE";

    [Parameter, EditorRequired]
    public EntryKind Kind { get; set; }

    [Parameter, EditorRequired]
    public string Author { get; set; } = string.Empty;

    /// <summary>The time as the caller wants it shown (already formatted).</summary>
    [Parameter]
    public string Time { get; set; } = string.Empty;

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
