using TechStrap.Admin.Clients;

namespace TechStrap.Admin.Components.Pages;

/// <summary>What to tell a signed-in user the API refused, and what to do next. Plain copy: cause plus next step.</summary>
public static class NoAccessCopy
{
    public const string Title = "You don't have access to TechStrap.";

    /// <summary>For a signed-in user refused one page only (the router's not-authorized case): the rest of the app still works for them.</summary>
    public const string PageTitle = "You don't have access to this page.";

    public const string PageReason = "You are signed in, but you are not allowed to open this page.";
    public const string BackToQueue = "Back to the queue";

    public static string Reason(string? code, string? apiMessage, string agentGroup) => code switch
    {
        ApiErrorCodes.AgentAccessRequired => $"You are signed in, but your account is not in the {agentGroup} group. Ask an administrator to add you, then sign in again.",
        ApiErrorCodes.AgentInactive => "Your agent account has been deactivated. Ask an administrator to reactivate it.",
        ApiErrorCodes.AgentEmailRequired => "Your sign-in did not include an email address. Ask an administrator to check the email scope of the sign-in provider.",
        ApiErrorCodes.AgentIdentityInvalid => "Your sign-in could not be matched to an agent profile. Sign out and in again; if it keeps happening, tell an administrator.",
        _ => string.IsNullOrWhiteSpace(apiMessage) ? "The API refused your account. Ask an administrator for help." : apiMessage,
    };
}
