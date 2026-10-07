namespace TechStrap.Application.Live;

/// <summary>What an agent is doing on a ticket page.</summary>
public enum TicketViewerState
{
    Viewing,
    Composing,
}

/// <summary>An agent who has a ticket open. One entry per agent however many connections they have; Composing wins over Viewing.</summary>
public sealed record TicketViewer(Guid AgentId, string DisplayName, TicketViewerState State);

/// <summary>Who has a ticket open, ordered by name then id so equal states compare equal.</summary>
public sealed record TicketPresence(Guid TicketId, IReadOnlyList<TicketViewer> Viewers);

/// <summary>The presence of a ticket after an update and whether the update changed what other agents would see.</summary>
public sealed record PresenceChange(bool Changed, TicketPresence Presence);

/// <summary>
/// Who has which ticket open (single Api instance, D-007). Entries belong to a hub connection; a composing hint expires after
/// <c>TicketLiveLimits.ComposingTtlSeconds</c> unless refreshed. Every method is safe to call concurrently.
/// </summary>
public interface ITicketPresenceStore
{
    PresenceChange Join(string connectionId, Guid ticketId, Guid agentId, string displayName);

    /// <summary>Null when the connection has not joined the ticket.</summary>
    PresenceChange? SetComposing(string connectionId, Guid ticketId, bool isComposing);

    PresenceChange Leave(string connectionId, Guid ticketId);

    /// <summary>Removes the connection from every ticket (it disconnected) and returns one entry per ticket it was on.</summary>
    IReadOnlyList<PresenceChange> LeaveAll(string connectionId);

    TicketPresence Get(Guid ticketId);
}
