using TechStrap.Contracts.Live;

namespace TechStrap.Application.Live;

/// <summary>
/// A change to one ticket, as Application sees it (D-018): the post-commit hook builds it from the committed event, the broadcasters send it.
/// It holds ids, a wire name and the ticket number only. <paramref name="Kind"/> is a <see cref="TicketChangeKinds"/> name.
/// </summary>
public sealed record TicketChange(
    Guid EventId, Guid TicketId, string TicketNumber, Guid ProductId, string EventType, Guid? ActorAgentId, DateTimeOffset OccurredAt, string Kind)
{
    /// <summary>"Something may have changed, reload everything": sent when the listener had to reconnect, because notifications sent meanwhile are lost.</summary>
    public static TicketChange Resync(Guid eventId, DateTimeOffset occurredAt) =>
        new(eventId, Guid.Empty, string.Empty, Guid.Empty, string.Empty, null, occurredAt, TicketChangeKinds.Resync);

    public static TicketChange From(TicketChangedDto dto) =>
        new(dto.EventId, dto.TicketId, dto.TicketNumber, dto.ProductId, dto.EventType, dto.ActorAgentId, dto.OccurredAt, dto.Kind);

    public TicketChangedDto ToDto() =>
        new(EventId, TicketId, TicketNumber, ProductId, EventType, ActorAgentId, OccurredAt, Kind);
}
