namespace TechStrap.Admin.Features.Shell;

/// <summary>The words of the command palette. Plain, sentence case (docs/BRAND.md section 3).</summary>
public static class PaletteCopy
{
    public const string Title = "Command palette";
    public const string InputLabel = "Type a command";
    public const string ListLabel = "Commands";
    public const string Empty = "No command matches.";
    public const string CommandFailed = "Couldn't run that command.";

    public const string GoToGroup = "Go to";
    public const string AdminGroup = "Admin";
    public const string TicketGroup = "Ticket";

    public const string ReplyCommand = "Reply to requester";
    public const string NoteCommand = "Add internal note";
    public const string AssignToMeCommand = "Assign to me";
    public const string NotSpamCommand = "Not spam";

    /// <summary>Read out when the list changes: "3 commands", "1 command".</summary>
    public static string Count(int count) => count == 1 ? "1 command" : $"{count} commands";
}
