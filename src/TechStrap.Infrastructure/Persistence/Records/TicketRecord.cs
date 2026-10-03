using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.Persistence.Records;

internal sealed class TicketRecord
{
    public Guid Id { get; set; }

    /// <summary>The stored full number, e.g. ACME-142 (D-009). Globally unique, never changes.</summary>
    public string Number { get; set; } = string.Empty;

    public Guid ProductId { get; set; }

    public Guid RequesterId { get; set; }

    public string Subject { get; set; } = string.Empty;

    public TicketStatus Status { get; set; }

    public TicketPriority Priority { get; set; }

    public Guid? AssigneeId { get; set; }

    public TicketChannel Channel { get; set; }

    public bool IsSpam { get; set; }

    public Guid? ParentTicketId { get; set; }

    public string? Metadata { get; set; }

    public bool MetadataTrusted { get; set; }

    public string? CustomFields { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? FirstResponseAt { get; set; }

    public DateTimeOffset? SolvedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public DateTimeOffset LastActivityAt { get; set; }

    /// <summary>Postgres <c>xmin</c>, the optimistic concurrency token.</summary>
    public uint Version { get; set; }

    public List<TicketTagRecord> Tags { get; set; } = [];
}

internal sealed class TicketTagRecord
{
    public Guid TicketId { get; set; }

    public Guid TagId { get; set; }
}
