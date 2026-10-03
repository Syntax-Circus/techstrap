using SyntaxCircus.Common;

namespace TechStrap.Application.Persistence;

/// <summary>
/// Atomic multi-write. <see cref="BeginAsync"/> opens one database transaction; repositories and the outbox only stage changes;
/// <see cref="IUnitOfWorkScope.CommitAsync"/> writes everything (ticket, messages, events, tokens, outbox rows) in that one
/// transaction. The concurrency token travels with the Domain object (<c>Version</c>); repositories apply it as the original token on update.
/// Disposing a scope that was not committed rolls everything back, including a ticket number that was allocated.
/// After a successful commit the Domain <c>Version</c> of every updated object is stale (the database assigns a new token): reload before
/// another update. A scope commits once: a second <see cref="IUnitOfWorkScope.CommitAsync"/> throws. A conflict result writes nothing,
/// so no events (ticket or admin) are saved for a rolled-back commit.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Starts the transaction. Scopes do not nest: beginning a second one on the same unit of work is a programming error.</summary>
    Task<IUnitOfWorkScope> BeginAsync(CancellationToken cancellationToken);
}

public interface IUnitOfWorkScope : IAsyncDisposable
{
    /// <summary>
    /// Saves and commits. Failures that a caller can act on come back as a Conflict result and the transaction is rolled back:
    /// a stale concurrency token ("concurrency-conflict"), a unique violation ("duplicate"), a foreign-key violation
    /// ("reference-violation"). Any other exception propagates.
    /// </summary>
    Task<Result> CommitAsync(CancellationToken cancellationToken);
}

/// <summary>Persistence error codes, named once for handlers and tests (commit conflicts and outbox outcomes).</summary>
public static class PersistenceErrorCodes
{
    public const string ConcurrencyConflict = "concurrency-conflict";
    public const string Duplicate = "duplicate";
    public const string ReferenceViolation = "reference-violation";
    /// <summary>Equals the Domain code returned by <c>MarkSent</c> and <c>MarkFailed</c> for a worker that does not own the claim.</summary>
    public const string OutboxNotClaimOwner = "outbox-not-claim-owner";

    public const string OutboxNotFound = "outbox-not-found";

    /// <summary>Equals the Domain code returned by <c>EmailOutboxItem.Claim</c> when the last allowed attempt's lease expired.</summary>
    public const string OutboxDeadLettered = "outbox-dead-lettered";
}
