using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.Persistence.Records;

/// <summary>Row of the append-only <c>ticket_events</c> table. Nothing in the application updates or deletes one.</summary>
internal sealed class TicketEventRecord
{
    public Guid Id { get; set; }

    public Guid TicketId { get; set; }

    public TicketEventType Type { get; set; }

    public ActorType ActorType { get; set; }

    public Guid? ActorId { get; set; }

    public string Payload { get; set; } = "{}";

    public DateTimeOffset OccurredAt { get; set; }
}

internal sealed class TicketAccessTokenRecord
{
    public Guid Id { get; set; }

    public Guid TicketId { get; set; }

    public Guid RequesterId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset IssuedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }
}
