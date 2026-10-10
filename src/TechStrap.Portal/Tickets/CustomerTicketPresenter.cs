using System.Globalization;
using TechStrap.Contracts.Tickets;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Tickets;

/// <summary>
/// Builds what the ticket page shows from the API's <see cref="CustomerTicketDto"/> (P09-T08, T23). The status becomes the customer's words (UX brief); a message's author becomes "You" for the customer, the name
/// the API resolved for an agent exactly as it came (no email, id or avatar exists in the DTO or here) and a neutral word for a system message; an attachment becomes the Portal's own link
/// (<see cref="PortalRoutes.TicketAttachment(TicketToken, Guid)"/>, built from the real token, never from its printed form). Every text field stays plain text: the page encodes them, and only the sanitized message
/// body is ever rendered as markup. The messages keep the order the API gave (chronological).
/// </summary>
public static class CustomerTicketPresenter
{
    public static CustomerTicketViewModel Present(CustomerTicketDto ticket, TicketToken token)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var (label, note) = Status(ticket.Status);
        return new CustomerTicketViewModel(
            ticket.Number,
            ticket.ProductKey,
            ticket.Subject,
            label,
            note,
            string.Equals(ticket.Status, TicketStatuses.Solved, StringComparison.OrdinalIgnoreCase),
            string.Equals(ticket.Status, TicketStatuses.Closed, StringComparison.OrdinalIgnoreCase),
            [.. ticket.Messages.Select(message => Message(message, token))]);
    }

    /// <summary>The customer's wording for a status, and the note shown with it. A status this build does not know is "In progress": the ticket is not finished as far as the customer can tell.</summary>
    public static (string Label, string? Note) Status(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "new" => (TicketCopy.Received, null),
        "open" => (TicketCopy.InProgress, null),
        "pending" => (TicketCopy.WaitingForYou, null),
        "solved" => (TicketCopy.Solved, TicketCopy.SolvedNote),
        "closed" => (TicketCopy.Closed, TicketCopy.ClosedNote),
        _ => (TicketCopy.InProgress, null),
    };

    public static string Author(string authorType, string? displayName) => authorType.Trim().ToLowerInvariant() switch
    {
        "requester" => TicketCopy.You,
        "agent" => string.IsNullOrWhiteSpace(displayName) ? TicketCopy.Support : displayName,
        _ => TicketCopy.System,
    };

    /// <summary>A size a person can read: bytes, kilobytes or megabytes, one decimal at most.</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => string.Create(CultureInfo.InvariantCulture, $"{Math.Max(bytes, 0)} B"),
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.#} MB"),
    };

    private static CustomerMessageViewModel Message(CustomerMessageDto message, TicketToken token) => new(
        message.Id,
        Author(message.AuthorType, message.AuthorDisplayName),
        string.Equals(message.AuthorType, MessageAuthorTypes.Requester, StringComparison.OrdinalIgnoreCase),
        message.BodyHtml,
        message.CreatedAt,
        [.. message.Attachments.Select(a => new CustomerAttachmentViewModel(a.Id, a.FileName, Size(a.Size), PortalRoutes.TicketAttachment(token, a.Id)))]);
}
