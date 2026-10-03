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

    /// <summary>
    /// Stages a new ticket. <c>AcceptChanges</c> runs while staging, so the ticket's pending messages and events are cleared at once, not at
    /// commit. After a failed commit, reload the ticket and redo the change; do not retry the same object, because its pending changes are gone.
    /// </summary>
    void Add(Ticket ticket);

    /// <summary>
    /// Stages the changes of a ticket loaded in this scope. <c>AcceptChanges</c> runs while staging (see <see cref="Add"/>), so after a
    /// failed commit reload the ticket and redo the change; do not retry the same object. After a successful commit the Domain
    /// <c>Version</c> is stale: reload before updating again.
    /// </summary>
    void Update(Ticket ticket);

    /// <summary>
    /// Agent queue page. Implementations normalize the page and page size in <paramref name="query"/> through <see cref="Paging"/>
    /// before querying.
    /// </summary>
    Task<PagedResult<TicketSummary>> ListAsync(TicketQuery query, CancellationToken cancellationToken);

    /// <summary>Oldest first. <paramref name="publicOnly"/> hides internal notes (customer view).</summary>
    Task<IReadOnlyList<Message>> GetMessagesAsync(Guid ticketId, bool publicOnly, CancellationToken cancellationToken);

    /// <summary>
    /// Oldest first. Agent-only (D-024): the events include the ids of internal notes, so customer timelines are built from
    /// <c>GetMessagesAsync(ticketId, publicOnly: true)</c>, never from events.
    /// </summary>
    Task<IReadOnlyList<TicketEvent>> GetEventsAsync(Guid ticketId, CancellationToken cancellationToken);

    /// <summary>
    /// One attachment of the ticket. With <paramref name="publicOnly"/> only an attachment whose parent message is customer-visible
    /// can be found: a lookup of an attachment on an internal note returns null (not found). Customer-facing callers must pass
    /// <c>publicOnly: true</c> (D-024).
    /// </summary>
    Task<Attachment?> GetAttachmentAsync(Guid ticketId, Guid attachmentId, bool publicOnly, CancellationToken cancellationToken);

    /// <summary>
    /// All attachments of the ticket, so a ticket view can list them under each message (match on message id). With
    /// <paramref name="publicOnly"/> only attachments whose parent message is customer-visible are returned. Customer-facing
    /// callers must pass <c>publicOnly: true</c> (D-024).
    /// </summary>
    Task<IReadOnlyList<Attachment>> GetAttachmentsAsync(Guid ticketId, bool publicOnly, CancellationToken cancellationToken);

    /// <summary>
    /// Solved tickets whose <c>solved_at</c> is before the cutoff, oldest first (auto-close, D-008). Implementations normalize
    /// <paramref name="limit"/> through <see cref="Paging.NormalizeBatchSize"/> before querying.
    /// </summary>
    Task<IReadOnlyList<Ticket>> ListSolvedBeforeAsync(DateTimeOffset solvedBefore, int limit, CancellationToken cancellationToken);

    /// <summary>Ids of every ticket carrying the tag, any status. Used to detach a tag before deleting it (D-030).</summary>
    Task<IReadOnlyList<Guid>> ListTicketIdsWithTagAsync(Guid tagId, CancellationToken cancellationToken);

    void AddAccessToken(TicketAccessToken token);

    void UpdateAccessToken(TicketAccessToken token);

    Task<TicketAccessToken?> GetAccessTokenByHashAsync(string tokenHash, CancellationToken cancellationToken);
}
