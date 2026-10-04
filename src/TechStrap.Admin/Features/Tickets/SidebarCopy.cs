namespace TechStrap.Admin.Features.Tickets;

/// <summary>The sidebar's copy and the one conflict banner every write shares. The two banner sentences come from UX-BRIEF-admin word for word.</summary>
public static class SidebarCopy
{
    public const string Saving = "Saving\u2026";
    public const string AssignToMe = "Assign to me";
    public const string Unassigned = "Unassigned";
    public const string AddTagPlaceholder = "Add tag\u2026";
    public const string NoTags = "No tags";
    public const string ChangeUncertain = "The change may already have been saved. Reload to see the current state.";

    public const string ConflictTitle = "This ticket changed since you opened it";
    public const string ConflictKept = "Your reply draft is kept.";
    public const string ConflictReload = "Reload";
    public const string ConflictReloaded = "Reloaded.";
    public const string ConflictDismiss = "Dismiss";

    public static string RemoveTag(string name) => $"Remove tag {name}";

    public static string LatestChange(string text, string actor) => $"Latest change: {text} ({actor}).";
}

/// <summary>Where the conflict banner is: hidden, asking for a reload, or confirming the reload.</summary>
public enum ConflictState
{
    None,
    Stale,
    Reloaded,
}

/// <summary>The sidebar's fields, used as keys for its pending, error and re-render state.</summary>
public enum SidebarField
{
    Status,
    Assignee,
    Priority,
    Product,
    Tags,
}
