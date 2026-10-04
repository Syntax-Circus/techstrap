namespace TechStrap.Admin.Features.Shell;

/// <summary>One row of the shortcut help dialog.</summary>
public sealed record ShortcutEntry(string Keys, string Context, string Description);

/// <summary>Every shortcut the help dialog lists. Two entries never share a key (pinned by a test), so the list is also the clash check.</summary>
public static class ShortcutCatalog
{
    public static IReadOnlyList<ShortcutEntry> All { get; } =
    [
        new("j / k", "Queue", "Move the selection down or up (the arrow keys work too)"),
        new("Enter", "Queue", "Open the selected ticket"),
        new("/", "Anywhere", "Focus search (opens the queue from other screens)"),
        new("r", "Ticket", "Public reply: open the tab and focus the box"),
        new("n", "Ticket", "Internal note: open the tab and focus the box"),
        new("e", "Ticket", "Focus the assignee control"),
        new("u", "Spam view, flagged ticket", "Not spam: restore the selected ticket from spam, no dialog"),
        new("Ctrl+Enter", "Reply box", "Send in the current mode"),
        new("Esc", "Anywhere", "Leave a field (your text is kept), close a dialog, or go back to the queue"),
        new("?", "Anywhere", "Show this list"),
    ];

    /// <summary>The one-line hints in the status bar, in display order.</summary>
    public static IReadOnlyList<(string Keys, string Label)> Hints { get; } =
    [
        ("j k", "move"),
        ("Enter", "open"),
        ("r", "reply"),
        ("n", "note"),
        ("e", "assign"),
        ("/", "search"),
        ("?", "help"),
    ];
}
