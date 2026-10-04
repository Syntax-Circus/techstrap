namespace TechStrap.Admin.Features.Settings.Agents;

/// <summary>The copy of the agents page. Roles are read-only here (D-041): they come from the identity provider's groups, and an admin can only activate or deactivate an agent.</summary>
public static class AgentsCopy
{
    public const int PageSize = 25;

    public const string Heading = "Agents";
    public const string RolesNote = "Roles come from your identity provider's groups.";
    public const string Loading = "Loading agents";
    public const string LoadFailed = "Couldn't load the agents.";
    public const string NoAgents = "No agents yet";

    public const string ColumnName = "Name";
    public const string ColumnEmail = "Email";
    public const string ColumnRole = "Role";
    public const string ColumnStatus = "Status";
    public const string ColumnLastSeen = "Last seen";
    public const string ColumnActions = "Actions";
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public const string NeverSeen = "Never";
    public const string Unknown = "\u2014";
    public const string You = "(you)";
    public const string Activate = "Activate";
    public const string Deactivate = "Deactivate";
    public const string DeactivateConfirm = "Deactivate agent";
    public const string ReloadList = "Reload list";

    public const string DeactivateBody = "They will lose access to TechStrap straight away. You can activate them again later.";
    public const string DeactivateSelfWarning = "This is your own account. You will lose access as soon as you confirm, and another admin will have to activate you again.";
    public const string DeactivateUncertain = "The change may have gone through. Reload the list to check before you try again.";
    public const string AgentGone = "That agent no longer exists.";

    public static string DeactivateTitle(string name) => $"Deactivate {name}?";

    public static string DeactivateFailed(string reason) => $"Couldn't deactivate the agent. Nothing was changed. {reason}";

    public static string ActivateFailed(string name, string reason) => $"Couldn't activate {name}. Nothing was changed. {reason}";

    public static string ActivateUncertain(string name) => $"The change to {name} may have gone through. Reload the list to check before you try again.";

    public static string Activated(string name) => $"Activated {name}";

    public static string Deactivated(string name) => $"Deactivated {name}";
}
