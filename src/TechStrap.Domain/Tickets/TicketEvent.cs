namespace TechStrap.Domain.Tickets;

public enum ActorType
{
    Agent,
    Requester,
    System,
}

/// <summary>Who caused a change: an agent, a requester or the system (workers, auto-close).</summary>
public readonly record struct Actor(ActorType Type, Guid? Id)
{
    public static Actor System => new(ActorType.System, null);

    public static Actor ForAgent(Guid agentId) => new(ActorType.Agent, agentId);

    public static Actor ForRequester(Guid requesterId) => new(ActorType.Requester, requesterId);
}

public enum TicketEventType
{
    Created,
    MessageAdded,
    StatusChanged,
    Assigned,
    ProductChanged,
    PriorityChanged,
    TagAdded,
    TagRemoved,
    MarkedSpam,
    FollowUpCreated,
}

/// <summary>
/// One immutable entry in a ticket's audit trail. There is no way to change or remove one. The payload holds ids, enum names and
/// the ticket number only, never free text, so erasing a requester never needs to rewrite events (D-006).
/// </summary>
public sealed class TicketEvent
{
    private TicketEvent(Guid id, Guid ticketId, TicketEventType type, ActorType actorType, Guid? actorId, string payloadJson, DateTimeOffset occurredAt)
    {
        Id = id;
        TicketId = ticketId;
        Type = type;
        ActorType = actorType;
        ActorId = actorId;
        PayloadJson = payloadJson;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; }

    public Guid TicketId { get; }

    public TicketEventType Type { get; }

    public ActorType ActorType { get; }

    public Guid? ActorId { get; }

    /// <summary>A JSON object (stored as jsonb).</summary>
    public string PayloadJson { get; }

    public DateTimeOffset OccurredAt { get; }

    internal static TicketEvent Raise(Guid ticketId, TicketEventType type, Actor actor, string payloadJson, DateTimeOffset occurredAt, TimeProvider clock) =>
        new(EntityId.New(clock), ticketId, type, actor.Type, actor.Id, payloadJson, occurredAt);

    public static TicketEvent Restore(Guid id, Guid ticketId, TicketEventType type, ActorType actorType, Guid? actorId, string payloadJson, DateTimeOffset occurredAt) =>
        new(id, ticketId, type, actorType, actorId, payloadJson, occurredAt);
}
