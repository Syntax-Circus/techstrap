using TechStrap.Contracts.Live;

namespace TechStrap.Application.Live;

/// <summary>Maps presence to its wire shape.</summary>
public static class TicketPresenceMapping
{
    public static TicketPresenceDto ToDto(this TicketPresence presence) =>
        new(presence.TicketId, [.. presence.Viewers.Select(viewer => new TicketViewerDto(viewer.AgentId, viewer.DisplayName, viewer.State.ToWire()))]);

    public static string ToWire(this TicketViewerState state) =>
        state == TicketViewerState.Composing ? TicketPresenceStates.Composing : TicketPresenceStates.Viewing;
}
