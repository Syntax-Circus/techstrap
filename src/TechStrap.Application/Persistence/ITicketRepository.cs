using SyntaxCircus.Common;
using TechStrap.Application.Tickets;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Persistence;

/// <summary>
/// Persistence of the ticket aggregate. <c>Add</c> and <c>Update</c> only stage: the ticket, its pending messages (with their
/// attachments), its tags and its pending <see cref="TicketEvent"/>s are all written by <see cref="IUnitOfWorkScope.CommitAsync"/>
/// in one transaction. <c>Update</c> requires the ticket to have been loaded through this repository in the same scope.
/// Nothing here exposes a query object or a persistence type (D-026).
/// </summary>
public interface ITicketRepository
{
    Task<Ticket?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Looks up by the stored full number, e.g. "ACME-142" (case-insensitive).</summary>
    Task<Ticket?> GetByNumberAsync(string number, CancellationToken cancellationToken);

    void Add(Ticket ticket);

    void Update(Ticket ticket);

    Task<PagedResult<TicketSummary>> ListAsync(TicketQuery query, CancellationToken cancellationToken);

    /// <summary>Oldest first. <paramref name="publicOnly"/> hides internal notes (customer view).</summary>
    Task<IReadOnlyList<Message>> GetMessagesAsync(Guid ticketId, bool publicOnly, CancellationToken cancellationToken);

    /// <summary>Oldest first.</summary>
    Task<IReadOnlyList<TicketEvent>> GetEventsAsync(Guid ticketId, CancellationToken cancellationToken);

    Task<Attachment?> GetAttachmentAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>All attachments of the ticket, so a ticket view can list them under each message (match on message id).</summary>
    Task<IReadOnlyList<Attachment>> GetAttachmentsAsync(Guid ticketId, CancellationToken cancellationToken);

    /// <summary>Solved tickets whose <c>solved_at</c> is before the cutoff, oldest first (auto-close, D-008).</summary>
    Task<IReadOnlyList<Ticket>> ListSolvedBeforeAsync(DateTimeOffset solvedBefore, int limit, CancellationToken cancellationToken);

    void AddAccessToken(TicketAccessToken token);

    void UpdateAccessToken(TicketAccessToken token);

    Task<TicketAccessToken?> GetAccessTokenByHashAsync(string tokenHash, CancellationToken cancellationToken);
}
