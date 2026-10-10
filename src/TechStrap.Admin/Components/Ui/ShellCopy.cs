namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// Copy shared by the shell and the reusable states. Plain, sentence case, no humor (docs/BRAND.md section 3); brand moments live in
/// <see cref="BrandMomentCopy"/> and blocking failures in <see cref="UiCopy"/>.
/// </summary>
public static class ShellCopy
{
    public const string Loading = "Loading";
    public const string Cancel = "Cancel";
    public const string Close = "Close";
    public const string Previous = "Previous";
    public const string Next = "Next";
    public const string PaginationLabel = "Pagination";
    public const string QueueLink = "Queue";
    public const string KbLink = "Knowledge base";
    public const string MySettingsLink = "My settings";
    public const string AdminLinksLabel = "Admin";
    public const string ProductsLink = "Products";
    public const string AgentsLink = "Agents";
    public const string TagsLink = "Tags";
    public const string AuditLink = "Audit";
    public const string FailedEmailsLink = "Failed emails";
    public const string NavigationLabel = "Admin navigation";

    /// <summary>The line at the foot of the rail: "v" and the build's semver, for example v0.4.1.</summary>
    public static string BuildVersionLine(string semver) => $"v{semver}";

    /// <summary>The button that opens and closes the rail below 992 px.</summary>
    public const string MenuToggle = "Menu";
    public const string ShortcutHelpTitle = "Keyboard shortcuts";
    public const string ShortcutHelpOpen = "Shortcuts";
    public const string StatusBarLabel = "Keyboard hints and messages";

    /// <summary>Read out after the "Failed emails" link when the badge shows a number.</summary>
    public static string FailedEmailsCountLabel(int count) => $"{count} waiting";
}
