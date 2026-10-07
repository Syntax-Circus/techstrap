using System.Runtime.CompilerServices;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;
using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.Live;

/// <summary>One ticket event captured while a unit of work is open. The number and product are null when the ticket was not tracked by the context.</summary>
internal sealed record CapturedTicketEvent(
    Guid EventId, Guid TicketId, string? TicketNumber, Guid? ProductId, TicketEventType Type, Guid? ActorAgentId, DateTimeOffset OccurredAt);

/// <summary>
/// What each open <see cref="Microsoft.EntityFrameworkCore.DbContext"/> has staged but not yet committed (the first half of the post-commit hook, D-018). The interceptors are
/// singletons while a context is per request, so the state is kept per context, in a table that forgets a context when it is collected: a context that is
/// disposed with its transaction open can never leak an entry. All access is under the entry's own lock.
/// </summary>
internal sealed class PendingTicketChanges(TimeProvider clock)
{
    private readonly ConditionalWeakTable<object, Staged> _staged = [];

    private sealed class Staged
    {
        public List<CapturedTicketEvent> Events { get; } = [];

        public bool ResyncNeeded { get; set; }
    }

    /// <param name="resyncNeeded">True when something happened that no event describes, such as a deleted ticket: the clients must reload everything.</param>
    public void Add(object context, IEnumerable<CapturedTicketEvent> events, bool resyncNeeded)
    {
        var staged = _staged.GetOrCreateValue(context);
        lock (staged)
        {
            staged.Events.AddRange(events);
            staged.ResyncNeeded |= resyncNeeded;
        }
    }

    public void Discard(object context) => _staged.Remove(context);

    /// <summary>
    /// Empties the context's staging and returns what to publish: one change per ticket however many events it got (a <c>Created</c> event makes it a <c>Created</c>
    /// change named by that event, otherwise the newest event names it), oldest first. A ticket the context could not describe, or a deletion, becomes a single <c>Resync</c> at the end.
    /// </summary>
    public IReadOnlyList<TicketChange> Take(object context)
    {
        if (!_staged.TryGetValue(context, out var staged))
        {
            return [];
        }

        _staged.Remove(context);
        lock (staged)
        {
            var changes = new List<TicketChange>();
            var resync = staged.ResyncNeeded;
            foreach (var group in staged.Events.GroupBy(captured => captured.TicketId))
            {
                var described = group.LastOrDefault(captured => captured.TicketNumber is not null && captured.ProductId is not null);
                if (described is null)
                {
                    resync = true;
                    continue;
                }

                // A new ticket is named by its Created event; otherwise the newest event names the change. OrderBy is stable, so events with the same time keep the order they were staged in.
                var created = group.FirstOrDefault(captured => captured.Type == TicketEventType.Created);
                var named = created ?? group.OrderBy(captured => captured.OccurredAt).Last();
                var kind = created is null ? TicketChangeKinds.Updated : TicketChangeKinds.Created;
                changes.Add(new TicketChange(
                    named.EventId, group.Key, described.TicketNumber!, described.ProductId!.Value, named.Type.ToString(), named.ActorAgentId, named.OccurredAt, kind));
            }

            var ordered = changes.OrderBy(change => change.OccurredAt).ToList();
            if (resync)
            {
                ordered.Add(TicketChange.Resync(Guid.CreateVersion7(clock.GetUtcNow()), clock.GetUtcNow()));
            }

            return ordered;
        }
    }
}
