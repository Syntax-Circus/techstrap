using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence;

/// <summary>
/// Makes <c>ticket_events</c> and <c>admin_events</c> append-only inside EF: saving a modified event of either kind throws, and so does
/// a deleted ticket event whose ticket is not also being deleted. The only removal that is allowed is the hard delete of the
/// whole ticket (D-006); admin events have no exception. There is also no repository method that updates or deletes an event,
/// so this is the second line of defence. Bulk <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> bypass it; an architecture test scans for those.
/// </summary>
internal sealed class AppendOnlyEventInterceptor : SaveChangesInterceptor
{
    public const string Message = "Event tables (ticket_events, admin_events) are append-only: events cannot be modified or deleted.";

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Guard(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Guard(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Guard(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var deletedTicketIds = context.ChangeTracker.Entries<TicketRecord>()
            .Where(entry => entry.State == EntityState.Deleted)
            .Select(entry => entry.Entity.Id)
            .ToHashSet();

        foreach (var entry in context.ChangeTracker.Entries<TicketEventRecord>())
        {
            var allowed = entry.State switch
            {
                EntityState.Modified => false,
                EntityState.Deleted => deletedTicketIds.Contains(entry.Entity.TicketId),
                _ => true,
            };

            if (!allowed)
            {
                throw new InvalidOperationException(Message);
            }
        }

        // Admin events are never removed, not even with a parent: any change or delete is refused.
        if (context.ChangeTracker.Entries<AdminEventRecord>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(Message);
        }
    }
}
