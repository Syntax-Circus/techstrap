namespace TechStrap.Application.Persistence;

/// <summary>A stored intake response for one API key and Idempotency-Key (D-020). <see cref="ResponseJson"/> is the serialised SubmitTicketResponse.</summary>
public sealed record IntakeIdempotencyEntry(Guid Id, Guid ApiKeyId, Guid TicketId, string ResponseJson, DateTimeOffset CreatedAt);

/// <summary>
/// Idempotency-Key lookups for API-key intake (D-020), over the existing intake_idempotency_keys table. Keys are hashed before
/// storage. Writes are staged in the caller's unit of work; a concurrent duplicate surfaces as a "duplicate" commit conflict.
/// </summary>
public interface IIntakeIdempotencyStore
{
    /// <summary>24 hours (D-020).</summary>
    static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    Task<IntakeIdempotencyEntry?> FindAsync(Guid apiKeyId, string idempotencyKey, CancellationToken cancellationToken);

    void Add(Guid apiKeyId, string idempotencyKey, Guid ticketId, string responseJson, DateTimeOffset createdAt);

    /// <summary>Stages removal of an expired entry so its key can be reused.</summary>
    void Remove(IntakeIdempotencyEntry entry);

    /// <summary>Deletes up to <paramref name="limit"/> entries created before <paramref name="olderThan"/>; returns the count.</summary>
    Task<int> PruneAsync(DateTimeOffset olderThan, int limit, CancellationToken cancellationToken);
}
