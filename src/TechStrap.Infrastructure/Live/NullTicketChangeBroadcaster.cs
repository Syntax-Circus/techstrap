using TechStrap.Application.Live;

namespace TechStrap.Infrastructure.Live;

/// <summary>
/// The default <see cref="ITicketChangeBroadcaster"/>: does nothing. <c>AddTechStrapPersistence</c> registers it so the post-commit hook always has something
/// to call (the Admin-less test hosts and tools have no hub and no NOTIFY); the Api and the Worker replace it with their own.
/// </summary>
internal sealed class NullTicketChangeBroadcaster : ITicketChangeBroadcaster
{
    public Task PublishAsync(TicketChange change, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task PublishPresenceAsync(TicketPresence presence, CancellationToken cancellationToken) => Task.CompletedTask;
}
