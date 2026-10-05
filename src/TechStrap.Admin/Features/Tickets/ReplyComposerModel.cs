using System.Globalization;
using TechStrap.Admin.Features.Kb;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Tickets;

public enum ComposerMode
{
    PublicReply,
    InternalNote,
}

/// <summary>What an agent has typed for one ticket: a separate text per mode, the chosen status-after, and the files picked for a public reply.</summary>
public sealed class ComposerDraft
{
    public ComposerMode Mode { get; set; } = ComposerMode.PublicReply;

    public string PublicText { get; set; } = string.Empty;

    public string NoteText { get; set; } = string.Empty;

    /// <summary>
    /// The knowledge base articles the public reply will link (at most <see cref="TicketOperationLimits.MaxLinkedArticles"/>). They live in the draft, so switching to a note and back, a conflict reload and a
    /// failed send all keep them; an accepted send takes out exactly the ones it sent. A note never sends them.
    /// </summary>
    public List<ArticleChoice> LinkedArticles { get; } = [];

    /// <summary>The status to apply on send: Pending by default, or empty for "leave unchanged". "Send and solve" does not use this.</summary>
    public string StatusAfter { get; set; } = ReplyComposerCopy.PendingValue;

    /// <summary>
    /// Set when a write may have been saved although its answer was lost (timeout, unreachable API, unreadable or 5xx answer). It holds the mode that was sent
    /// and survives the screen being closed; the composer shows the "check the timeline" notice until the next send.
    /// </summary>
    public ComposerMode? UncertainSend { get; set; }

    /// <summary>
    /// Set when a composer was closed while files were attached. The files are NOT kept here: an <c>IBrowserFile</c> is only readable while the
    /// <c>InputFile</c> element that produced it is alive, so the next composer tells the agent to attach them again.
    /// </summary>
    public bool FilesDropped { get; set; }

    /// <summary>True from the moment a write starts until it settles. It lives in the store, so a composer mounted meanwhile shows the sending state and cannot send again.</summary>
    public bool InFlight { get; set; }

    public ComposerMode InFlightMode { get; set; }

    /// <summary>Raised when an in-flight write settles, so every composer showing this draft re-renders. It can fire on any thread.</summary>
    public event Action? Changed;

    internal void RaiseChanged() => Changed?.Invoke();
}

/// <summary>
/// Keeps each ticket's <see cref="ComposerDraft"/> for the life of the circuit, so leaving a ticket, a reload after a conflict, or the screen
/// being rebuilt never loses typed text (UX-BRIEF-admin, Composer). A lost circuit loses it: the leave-warning is the guard for that (UX open question 6).
/// </summary>
public sealed class DraftStore
{
    private readonly Dictionary<Guid, ComposerDraft> _drafts = [];

    public ComposerDraft Get(Guid ticketId)
    {
        if (!_drafts.TryGetValue(ticketId, out var draft))
        {
            draft = new ComposerDraft();
            _drafts[ticketId] = draft;
        }

        return draft;
    }
}

/// <summary>
/// Makes a browser-supplied file name safe to put in a multipart part. <c>MultipartFormDataContent.Add</c> throws on an empty name and the API connection
/// does not map <see cref="ArgumentException"/>, so an odd name must never reach it: quotes, control and Unicode format characters (such as the right-to-left override) and path separators are removed, the
/// extension is kept, and a name that ends up empty becomes <see cref="Fallback"/>.
/// </summary>
internal static class AttachmentFileName
{
    public const string Fallback = "attachment";

    public static string Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Fallback;
        }

        var cleaned = string.Concat(name.Where(c => !char.IsControl(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format && c is not ('"' or '\'' or '/' or '\\'))).Trim();
        return cleaned.Length == 0 ? Fallback : cleaned;
    }
}

public static class ReplyComposerCopy
{
    public const string PendingValue = TicketStatuses.Pending;
    public const string LeaveUnchangedValue = "";

    public const string PublicTab = "Public reply";
    public const string NoteTab = "Internal note";
    public const string PublicWarning = "PUBLIC: this will be emailed to the customer.";
    public const string InternalWarning = "INTERNAL: the customer will NOT see this note.";
    public const string InternalAudience = "Visible to agents only";
    public const string NotePlaceholder = "Note for the team only";
    public const string ReplyPlaceholder = "Write your reply";
    public const string SendReply = "Send reply";
    public const string SendAndSolve = "Send and solve";
    public const string AddNote = "Add internal note";
    public const string SetPending = "Set to Pending";
    public const string LeaveUnchanged = "Leave status unchanged";
    public const string AttachFiles = "Attach files";
    public const string EmptyReply = "Write a reply before sending.";
    public const string EmptyNote = "Write a note before adding it.";
    private const string Kept = "Your text is kept, and your files stay attached while you stay on this ticket.";

    public const string ReplyFailed = $"Couldn't send the reply. {Kept}";
    public const string NoteFailed = "Couldn't add the note. Your text is kept.";

    /// <summary>The API refused a linked article: it is not published, or it belongs to another product, or it is gone. It does not say which one.</summary>
    public const string ArticleNotLinkable = "One of the linked articles can't be linked: it may have been unpublished, moved to another product or removed. Remove the articles you no longer want, then send again. Your text is kept.";

    /// <summary>A write that timed out, could not reach the API or got an unreadable answer may still have been saved: never offer a bare "Try again".</summary>
    public const string ReplyUncertain = $"The reply may already have been sent. {Kept} Check the timeline before sending again.";
    public const string NoteUncertain = "The note may already have been added. Your text is kept. Check the timeline before adding it again.";
    public const string Conflict = "This ticket changed since you opened it. Your text is kept, and your files stay attached while you stay on this ticket; reload the ticket, then send again.";
    public const string TooManyFiles = "You can attach up to {0} files.";
    public const string FileTooLarge = "{0} is larger than {1}.";
    public const string FileTypeNotAllowed = "{0}: this file type isn't allowed.";

    public static string ReplySent(string number) => $"Reply sent on {number}";

    public static string NoteAdded(string number) => $"Internal note added to {number}";
}
