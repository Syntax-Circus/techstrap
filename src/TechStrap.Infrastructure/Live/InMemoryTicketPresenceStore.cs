using TechStrap.Application.Live;
using TechStrap.Contracts.Live;

namespace TechStrap.Infrastructure.Live;

/// <summary>
/// Presence in the Api process's memory (D-007: one instance). One lock guards everything: the data is tiny (a few agents on a few tickets), every operation is
/// a handful of dictionary writes, and a single lock makes "compare before and after" trivially atomic.
/// An entry belongs to a hub connection; an agent with several tabs is one viewer. A composing hint is a lease of <see cref="TicketLiveLimits.ComposingTtlSeconds"/>
/// that only a refresh extends; an expired lease is never reported, so nothing needs to sweep.
/// "Changed" means what other agents would see differs, or a composing lease was extended: that refresh is the heartbeat the clients' own clear-after-TTL
/// depends on, so it is sent (the hub is not asked to send anything else on a repeat that moved nothing).
/// </summary>
internal sealed class InMemoryTicketPresenceStore(TimeProvider clock) : ITicketPresenceStore
{
    private static readonly TimeSpan ComposingLease = TimeSpan.FromSeconds(TicketLiveLimits.ComposingTtlSeconds);

    private readonly object _gate = new();
    private readonly Dictionary<Guid, Dictionary<string, Entry>> _byTicket = [];
    private readonly Dictionary<string, HashSet<Guid>> _byConnection = [];

    private sealed record Entry(Guid AgentId, string DisplayName, DateTimeOffset? ComposingUntil);

    /// <summary>True when nothing is held: every connection that joined has left. Lets a test prove the store does not grow forever.</summary>
    internal bool IsEmpty
    {
        get
        {
            lock (_gate)
            {
                return _byTicket.Count == 0 && _byConnection.Count == 0;
            }
        }
    }

    public PresenceChange Join(string connectionId, Guid ticketId, Guid agentId, string displayName)
    {
        lock (_gate)
        {
            var before = Snapshot(ticketId);
            Entries(ticketId)[connectionId] = new Entry(agentId, displayName, null);
            if (!_byConnection.TryGetValue(connectionId, out var tickets))
            {
                _byConnection[connectionId] = tickets = [];
            }

            tickets.Add(ticketId);
            return Change(before, Snapshot(ticketId), leaseMoved: false);
        }
    }

    public PresenceChange? SetComposing(string connectionId, Guid ticketId, bool isComposing)
    {
        lock (_gate)
        {
            if (!_byTicket.TryGetValue(ticketId, out var entries) || !entries.TryGetValue(connectionId, out var entry))
            {
                return null;
            }

            var before = Snapshot(ticketId);
            var until = isComposing ? clock.GetUtcNow() + ComposingLease : (DateTimeOffset?)null;
            entries[connectionId] = entry with { ComposingUntil = until };

            // A lease that was already over and is cleared again moves nothing anyone saw.
            var leaseMoved = isComposing && entry.ComposingUntil != until;
            return Change(before, Snapshot(ticketId), leaseMoved);
        }
    }

    public PresenceChange Leave(string connectionId, Guid ticketId)
    {
        lock (_gate)
        {
            var before = Snapshot(ticketId);
            Remove(connectionId, ticketId);
            return Change(before, Snapshot(ticketId), leaseMoved: false);
        }
    }

    public IReadOnlyList<PresenceChange> LeaveAll(string connectionId)
    {
        lock (_gate)
        {
            if (!_byConnection.TryGetValue(connectionId, out var tickets))
            {
                return [];
            }

            var changes = new List<PresenceChange>(tickets.Count);
            foreach (var ticketId in tickets.ToArray())
            {
                var before = Snapshot(ticketId);
                Remove(connectionId, ticketId);
                changes.Add(Change(before, Snapshot(ticketId), leaseMoved: false));
            }

            return changes;
        }
    }

    public TicketPresence Get(Guid ticketId)
    {
        lock (_gate)
        {
            return Snapshot(ticketId);
        }
    }

    private Dictionary<string, Entry> Entries(Guid ticketId)
    {
        if (!_byTicket.TryGetValue(ticketId, out var entries))
        {
            _byTicket[ticketId] = entries = [];
        }

        return entries;
    }

    private void Remove(string connectionId, Guid ticketId)
    {
        if (_byTicket.TryGetValue(ticketId, out var entries) && entries.Remove(connectionId) && entries.Count == 0)
        {
            _byTicket.Remove(ticketId);
        }

        if (_byConnection.TryGetValue(connectionId, out var tickets) && tickets.Remove(ticketId) && tickets.Count == 0)
        {
            _byConnection.Remove(connectionId);
        }
    }

    private static PresenceChange Change(TicketPresence before, TicketPresence after, bool leaseMoved) =>
        new(leaseMoved || !before.Viewers.SequenceEqual(after.Viewers), after);

    /// <summary>The viewers now: one per agent (composing if any of their connections holds a live lease), by name then id.</summary>
    private TicketPresence Snapshot(Guid ticketId)
    {
        if (!_byTicket.TryGetValue(ticketId, out var entries))
        {
            return new TicketPresence(ticketId, []);
        }

        var now = clock.GetUtcNow();
        var viewers = entries.Values
            .GroupBy(entry => entry.AgentId)
            .Select(group => new TicketViewer(
                group.Key,
                group.OrderBy(entry => entry.DisplayName, StringComparer.Ordinal).First().DisplayName,
                group.Any(entry => entry.ComposingUntil > now) ? TicketViewerState.Composing : TicketViewerState.Viewing))
            .OrderBy(viewer => viewer.DisplayName, StringComparer.Ordinal)
            .ThenBy(viewer => viewer.AgentId)
            .ToList();
        return new TicketPresence(ticketId, viewers);
    }
}
