namespace TechStrap.Contracts.Live;

/// <summary>
/// One change to one ticket, pushed to every agent's queue. It carries ids, a wire name and the ticket number only: never customer text or a name
/// (D-018). Clients refresh through the normal REST calls. <paramref name="EventId"/> lets a client de-duplicate (D-046); a <c>Resync</c> has
/// empty ids and means "reload everything".
/// </summary>
public sealed record TicketChangedDto(
    Guid EventId, Guid TicketId, string TicketNumber, Guid ProductId, string EventType, Guid? ActorAgentId, DateTimeOffset OccurredAt, string Kind);

/// <summary>An agent who has a ticket open. <paramref name="DisplayName"/> is the agent's internal name and goes to other agents only (D-046).</summary>
public sealed record TicketViewerDto(Guid AgentId, string DisplayName, string State);

/// <summary>Who has a ticket open right now: one entry per agent, however many tabs they have.</summary>
public sealed record TicketPresenceDto(Guid TicketId, IReadOnlyList<TicketViewerDto> Viewers);
