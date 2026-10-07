namespace TechStrap.Application.Live;

/// <summary>
/// The one fan-in for live updates (D-018, D-046). The Api's implementation pushes to the SignalR hub, the Worker's sends a Postgres NOTIFY, and
/// <c>NullTicketChangeBroadcaster</c> does nothing for hosts that have neither. Delivery is best effort and at most once: correctness never depends
/// on it, because pages always load full state through REST.
/// </summary>
public interface ITicketChangeBroadcaster
{
    /// <summary>Tells every agent's queue that a ticket changed. It carries the change's ids and names only.</summary>
    Task PublishAsync(TicketChange change, CancellationToken cancellationToken);

    /// <summary>
    /// Tells the agents who have the ticket open who is there now. Presence lives in the Api process only, so the Worker's implementation ignores it.
    /// </summary>
    Task PublishPresenceAsync(TicketPresence presence, CancellationToken cancellationToken);
}
