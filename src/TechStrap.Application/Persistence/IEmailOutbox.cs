using SyntaxCircus.Common;
using TechStrap.Domain.Outbox;

namespace TechStrap.Application.Persistence;

/// <summary>Handlers enqueue email here. The row is staged and written by the caller's <see cref="IUnitOfWorkScope"/> (D-010).</summary>
public interface IEmailOutbox
{
    void Enqueue(EmailOutboxItem item);
}

/// <summary>
/// The worker and admin side of the outbox. <c>ClaimBatchAsync</c>, <c>MarkSentAsync</c> and <c>MarkFailedAsync</c> run in their
/// own short transactions (the worker has no unit of work). <c>GetAsync</c> and <c>Update</c> are for admin retry and discard
/// and stage into the caller's scope together with an admin event.
/// </summary>
public interface IEmailOutboxStore
{
    /// <summary>
    /// Claims up to <paramref name="batchSize"/> due rows (<c>FOR UPDATE SKIP LOCKED</c>) and returns them as claimed.
    /// <see cref="EmailOutboxItem.Claim"/> can fail with "outbox-dead-lettered" after it has already moved the item to
    /// <c>DeadLettered</c> (a worker lease that expired on the last allowed attempt). That failure still changed the item, so the
    /// implementation must persist it even though the claim failed, and must leave it out of the returned batch.
    /// </summary>
    Task<IReadOnlyList<EmailOutboxItem>> ClaimBatchAsync(string workerId, int batchSize, TimeSpan lease, CancellationToken cancellationToken);

    /// <summary>Conflict "outbox-claim-lost" when another worker has taken the row since.</summary>
    Task<Result> MarkSentAsync(Guid id, string workerId, CancellationToken cancellationToken);

    /// <summary>Schedules the next attempt, or dead-letters the row after the last allowed attempt.</summary>
    Task<Result> MarkFailedAsync(Guid id, string workerId, string error, CancellationToken cancellationToken);

    /// <summary>Newest first.</summary>
    Task<PagedResult<EmailOutboxItem>> ListDeadLettersAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<EmailOutboxItem?> GetAsync(Guid id, CancellationToken cancellationToken);

    void Update(EmailOutboxItem item);
}
