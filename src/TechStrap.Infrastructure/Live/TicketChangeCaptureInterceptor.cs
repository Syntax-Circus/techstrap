using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Live;

/// <summary>
/// The first half of the post-commit hook (D-018, D-046): while SaveChanges runs it notes the <c>TicketEvent</c> rows being inserted (and whether a ticket is being deleted) and
/// stages them in <see cref="PendingTicketChanges"/>. It publishes nothing, because at this point the unit of work has not committed. The
/// <see cref="TicketChangePublishingInterceptor"/> publishes after the commit and drops the staging on a rollback; this interceptor drops it when SaveChanges itself fails (a concurrency conflict, a
/// constraint violation), and publishes straight away only when there is no explicit transaction (nothing will commit later).
/// It reads only what the context already tracks, never the database, so it adds no query to a save.
/// </summary>
internal sealed class TicketChangeCaptureInterceptor(PendingTicketChanges pending, TicketChangePublisher publisher) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is { Database.CurrentTransaction: null } context)
        {
            publisher.Publish(context);
        }

        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { Database.CurrentTransaction: null } context)
        {
            await publisher.PublishAsync(context);
        }

        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => publisher.Discard(eventData.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        publisher.Discard(eventData.Context);
        return Task.CompletedTask;
    }

    private void Capture(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var added = context.ChangeTracker.Entries<TicketEventRecord>().Where(entry => entry.State == EntityState.Added).Select(entry => entry.Entity).ToList();
        var tickets = context.ChangeTracker.Entries<TicketRecord>().Where(entry => entry.State != EntityState.Detached).ToDictionary(entry => entry.Entity.Id, entry => entry.Entity);
        var deleted = context.ChangeTracker.Entries<TicketRecord>().Any(entry => entry.State == EntityState.Deleted);
        if (added.Count == 0 && !deleted)
        {
            return;
        }

        pending.Add(
            context,
            added.Select(record => tickets.TryGetValue(record.TicketId, out var ticket)
                ? new CapturedTicketEvent(record.Id, record.TicketId, ticket.Number, ticket.ProductId, record.Type, ActorAgent(record), record.OccurredAt)
                : new CapturedTicketEvent(record.Id, record.TicketId, null, null, record.Type, ActorAgent(record), record.OccurredAt)),
            resyncNeeded: deleted);
    }

    /// <summary>The agent row id for an agent actor; null for a requester or the system (so nothing about a customer is ever sent).</summary>
    private static Guid? ActorAgent(TicketEventRecord record) => record.ActorType == Domain.Tickets.ActorType.Agent ? record.ActorId : null;
}
