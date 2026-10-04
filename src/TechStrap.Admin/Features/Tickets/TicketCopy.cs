namespace TechStrap.Admin.Features.Tickets;

/// <summary>
/// The ticket screen's copy, defined once. The strings from UX-BRIEF-admin are used word for word; the rest follows the voice rules
/// (plain cause plus next step, sentence case, no humour, no exclamation marks).
/// </summary>
public static class TicketCopy
{
    public const string Loading = "Loading ticket";
    public const string LoadFailed = "Couldn't load this ticket.";
    public const string NotFoundHeading = "Ticket not found";
    public const string NotFoundBody = "No ticket has that number. Check the number, or search the queue.";
    public const string GoneHeading = "This ticket no longer exists";
    public const string GoneBody = "It was deleted after you opened it.";
    public const string BackToQueue = "Back to the queue";
    public const string ClosedNotice = "Closed tickets are read-only; a customer reply starts a follow-up";
    public const string FollowUpTo = "Follow-up to";
    public const string Untrusted = "Untrusted";
    public const string Trusted = "Trusted";
    public const string UntrustedHelp = "Sent by an app with a public key. Don't rely on it.";
    public const string MetadataUnreadable = "The metadata couldn't be read.";
    public const string RequesterHeading = "Requester";
    public const string MetadataHeading = "Metadata";
    public const string TimelineLabel = "Timeline";
    public const string AttachmentsLabel = "Attachments";
    public const string ConversationLabel = "Conversation";
    public const string StatusLabel = "Status";
    public const string Unassigned = "Unassigned";
    public const string None = "None";
    public const string LinkedArticlesLabel = "Linked articles";
}
