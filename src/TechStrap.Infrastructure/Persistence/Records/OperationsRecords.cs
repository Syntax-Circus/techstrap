using TechStrap.Domain.Admin;
using TechStrap.Domain.Outbox;

namespace TechStrap.Infrastructure.Persistence.Records;

internal sealed class AdminEventRecord
{
    public Guid Id { get; set; }

    public AdminEventType Type { get; set; }

    public Guid ActorId { get; set; }

    public AdminSubjectType SubjectType { get; set; }

    public Guid SubjectId { get; set; }

    public string Payload { get; set; } = "{}";

    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>Row of <c>email_outbox</c>.</summary>
internal sealed class EmailOutboxRecord
{
    public Guid Id { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string ToAddress { get; set; } = string.Empty;

    public string Payload { get; set; } = "{}";

    public Guid? ProductId { get; set; }

    public Guid? TicketId { get; set; }

    public OutboxStatus Status { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public string? ClaimedBy { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }
}

/// <summary>Row of <c>intake_idempotency_keys</c> (D-020). The store that uses it is PHASE-05; the table lives with the rest of the schema.</summary>
internal sealed class IntakeIdempotencyKeyRecord
{
    public Guid Id { get; set; }

    public Guid ApiKeyId { get; set; }

    public string KeyHash { get; set; } = string.Empty;

    public Guid TicketId { get; set; }

    public string Response { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }
}
