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
    /// Stages the hard delete of a ticket the current scope loaded (<see cref="GetByIdAsync"/>). The database cascades its messages,
    /// attachments, events, access tokens, tags, linked articles and idempotency keys; follow-ups are unlinked (D-039). Never uses a bulk
    /// delete: the ticket's events are append-only and only a tracked ticket delete is allowed to remove them.
    /// </summary>
    void Remove(Ticket ticket);

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
    /// Solved, non-spam tickets whose <c>solved_at</c> is before the cutoff, oldest first (auto-close, D-008). Spam is excluded so Solved
    /// spam can never starve the batch. Implementations normalize
    /// <paramref name="limit"/> through <see cref="Paging.NormalizeBatchSize"/> before querying.
    /// </summary>
    Task<IReadOnlyList<Ticket>> ListSolvedBeforeAsync(DateTimeOffset solvedBefore, int limit, CancellationToken cancellationToken);

    /// <summary>Ids of every ticket carrying the tag, any status. Used to detach a tag before deleting it (D-030).</summary>
    Task<IReadOnlyList<Guid>> ListTicketIdsWithTagAsync(Guid tagId, CancellationToken cancellationToken);

    void AddAccessToken(TicketAccessToken token);

    void UpdateAccessToken(TicketAccessToken token);

    Task<TicketAccessToken?> GetAccessTokenByHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>Per-view counts for the queue tabs; view membership exactly as <see cref="ListAsync"/> (Mine uses <paramref name="agentId"/>).</summary>
    Task<TicketViewCounts> CountViewsAsync(Guid agentId, CancellationToken cancellationToken);

    /// <summary>An attachment by its own id (agent download), or null. Agent-only: it does not hide internal notes.</summary>
    Task<Attachment?> GetAttachmentByIdAsync(Guid attachmentId, CancellationToken cancellationToken);

    /// <summary>A message by its own id (the Worker renders reply emails from it at send time, D-033), or null. Agent and worker only: it does not hide internal notes.</summary>
    Task<Message?> GetMessageAsync(Guid messageId, CancellationToken cancellationToken);

    /// <summary>An untracked read straight from the database; use after a commit to return the new Version.</summary>
    Task<TicketState?> GetStateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Follow-ups of a parent created at or after <paramref name="since"/>, newest first, each with its first public message.</summary>
    Task<IReadOnlyList<FollowUpCandidate>> ListRecentFollowUpsAsync(Guid parentTicketId, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>A requester's non-spam tickets, most recently active first, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<RequesterTicketLink>> ListRecentTicketsForRequesterAsync(Guid requesterId, int limit, CancellationToken cancellationToken);
}
