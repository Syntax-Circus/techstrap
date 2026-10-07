namespace TechStrap.Portal.Tickets;

/// <summary>The words of the ticket page (UX brief): the customer's status wording, the notes under it, who wrote a message, and the reply form. The product's name is the only name on the page.</summary>
public static class TicketCopy
{
    // The status words a customer sees, for the five statuses (UX brief: "Received", "In progress", "Waiting for your reply", "Solved", "Closed").
    public const string Received = "Received";
    public const string InProgress = "In progress";
    public const string WaitingForYou = "Waiting for your reply";
    public const string Solved = "Solved";
    public const string Closed = "Closed";

    public const string SolvedNote = "This ticket is solved. If you reply, it will be reopened.";
    public const string ClosedNote = "This ticket is closed. If you reply, we will start a new follow-up ticket linked to it.";

    // Who wrote a message. An agent is named by the API (first name and the product's support name, or the public display name) and shown as it came; the customer's own messages read "You".
    public const string You = "You";
    public const string Support = "Support";
    public const string System = "Update";

    public const string StatusLabel = "Status";
    public const string ConversationHeading = "Conversation";
    public const string NoMessages = "There are no messages to show yet.";
    public const string AttachmentsLabel = "Attachments";

    /// <summary>The link at the top of a long conversation that jumps to the reply form.</summary>
    public const string JumpToReply = "Jump to your reply";

    /// <summary>Required is said in words, not by an asterisk (UX brief).</summary>
    public const string ReplyNote = "A message is required. Attachments are optional.";

    public const string ReplyHeading = "Reply";
    public const string ReplyLabel = "Your reply";
    public const string ReplyButton = "Send reply";
    public const string FollowUpButton = "Send and start a new follow-up ticket";

    /// <summary>Shown instead of the reply form when the API started a follow-up but its link could not be read: a true statement, with nothing to follow.</summary>
    public const string FollowUpStarted = "Your reply was received and we started a new follow-up ticket for it. We have emailed you its link.";

    public const string Unavailable = "This ticket could not be loaded just now. Reload the page in a moment.";

    public static string Title(string number) => $"Ticket {number}";
}
