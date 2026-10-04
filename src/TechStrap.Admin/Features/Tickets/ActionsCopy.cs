namespace TechStrap.Admin.Features.Tickets;

/// <summary>
/// The copy of the spam, delete and erase flows. The dialogs name the object and the consequence; the irreversible ones also ask for a typed confirmation.
/// There are no counts ("12 tickets, 31 attachments"): the API offers none, so the dialogs say "every ticket from this requester" instead (recorded gap, D-040).
/// </summary>
public static class ActionsCopy
{
    public const string MoreActions = "More actions";
    public const string MarkSpam = "Mark as spam";
    public const string NotSpam = "Not spam";
    public const string DeleteTicket = "Delete ticket (permanent)";
    public const string EraseRequester = "Erase requester (permanent)";

    public const string SpamConfirm = "Mark as spam";
    public const string DeleteConfirm = "Delete ticket";
    public const string EraseConfirm = "Erase requester";

    public static string SpamTitle(string number) => $"Mark {number} as spam?";

    public static string SpamBody(string number) =>
        $"This hides {number} from the normal queue views and flags it as spam. You can restore it from the Spam view.";

    public static string DeleteTitle(string number) => $"Delete {number} permanently?";

    public const string DeleteBody =
        "This permanently removes the ticket with all of its messages and attachments. Follow-up tickets stay, but lose their link to it. This can't be undone.";

    public const string EraseTitle = "Erase this requester?";

    public static string EraseBody(string email) =>
        $"This erases {email} from every ticket from this requester: their name and address, the subjects and the messages they wrote, and their attachments are removed. " +
        "Agent replies and internal notes stay. This can't be undone.";

    public static string SpamFailed(string detail) => $"Couldn't mark the ticket as spam. Nothing was changed. {detail}";

    public static string DeleteFailed(string detail) => $"Couldn't delete the ticket. Nothing was changed. {detail}";

    public static string EraseFailed(string detail) => $"Couldn't erase the requester. Nothing was changed. {detail}";

    public static string NotSpamFailed(string detail) => $"Couldn't restore the ticket from spam. {detail}";

    // A write that failed without a clear answer (timeout, outage, unreadable reply) may still have been applied: say so, and offer a reload or the queue, never a bare retry.
    public const string SpamUncertain = "The ticket may already have been marked as spam. Reload to see its current state, or check the queue.";
    public const string RestoreUncertain = "The ticket may already have been restored from spam. Reload to see its current state, or check the queue.";
    public const string DeleteUncertain = "The ticket may already have been deleted. Reload to see whether it still exists, or check the queue.";
    public const string EraseUncertain = "The requester may already have been erased. Reload to see the current state, or check the queue.";
    public const string Reload = "Reload";
    public const string CheckQueue = "Check the queue";

    public static string MarkedSpam(string number) => $"Marked {number} as spam";

    public static string Restored(string number) => $"Restored {number} from spam";

    public static string Deleted(string number) => $"Deleted {number}";

    public static string Erased(string number) => $"Erased the requester of {number}";

    public static string ChangedMeanwhile(string number) => $"{number} changed meanwhile. Open it to review, then restore it from spam.";
}
