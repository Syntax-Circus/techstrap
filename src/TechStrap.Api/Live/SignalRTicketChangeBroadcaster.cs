using Microsoft.AspNetCore.SignalR;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;

namespace TechStrap.Api.Live;

/// <summary>
/// The Api's <see cref="ITicketChangeBroadcaster"/>: pushes to the hub's groups (D-018). A change goes to the <c>queue</c> group, which every connection is in, and the client
/// filters by ticket id (D-046); presence goes to the ticket's own group. The payloads are the Contracts DTOs, which hold ids and names only.
/// </summary>
public sealed class SignalRTicketChangeBroadcaster(IHubContext<TicketHub> hub) : ITicketChangeBroadcaster
{
    public Task PublishAsync(TicketChange change, CancellationToken cancellationToken) =>
        hub.Clients.Group(TicketHubGroups.Queue).SendAsync(TicketHubMethods.TicketChanged, change.ToDto(), cancellationToken);

    public Task PublishPresenceAsync(TicketPresence presence, CancellationToken cancellationToken) =>
        hub.Clients.Group(TicketHubGroups.Ticket(presence.TicketId)).SendAsync(TicketHubMethods.PresenceChanged, presence.ToDto(), cancellationToken);
}
