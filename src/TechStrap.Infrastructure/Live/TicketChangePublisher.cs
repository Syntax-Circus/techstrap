using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TechStrap.Application.Live;

namespace TechStrap.Infrastructure.Live;

/// <summary>
/// The second half of the post-commit hook (D-018): hands what a context staged to the process's <see cref="ITicketChangeBroadcaster"/>, one call per ticket.
/// It never throws: a broadcaster that fails or hangs is logged by exception type name (never the message) and skipped, so a committed change is never reported as a
/// failed request, and one ticket's failure does not stop the next. The publish has its own time limit, because it runs on the request's own thread of work.
/// </summary>
internal sealed class TicketChangePublisher(PendingTicketChanges pending, ITicketChangeBroadcaster broadcaster, ILogger<TicketChangePublisher> logger)
{
    /// <summary>How long one broadcast may take before it is abandoned.</summary>
    public static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(5);

    public async Task PublishAsync(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var change in pending.Take(context))
        {
            try
            {
                using var timeout = new CancellationTokenSource(PublishTimeout);
                await broadcaster.PublishAsync(change, timeout.Token);
            }
            catch (Exception exception)
            {
                // Type name only: an exception message may carry data (log redaction rule). The change was committed; the clients catch up on their next load.
                logger.LogWarning("Publishing a ticket change failed ({ExceptionType}).", exception.GetType().Name);
            }
        }
    }

    public void Publish(DbContext? context) => PublishAsync(context).GetAwaiter().GetResult();

    public void Discard(DbContext? context)
    {
        if (context is not null)
        {
            pending.Discard(context);
        }
    }
}
