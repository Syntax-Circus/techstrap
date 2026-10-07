using System.Collections.Concurrent;
using TechStrap.Application.Live;

namespace TechStrap.Infrastructure.IntegrationTests.Live;

/// <summary>An <see cref="ITicketChangeBroadcaster"/> that records every call and can be told to throw or to wait.</summary>
internal sealed class RecordingBroadcaster : ITicketChangeBroadcaster
{
    private readonly ConcurrentQueue<TicketChange> _attempts = new();

    /// <summary>Every change handed to <see cref="PublishAsync"/>, in order, including the ones that threw.</summary>
    public IReadOnlyList<TicketChange> Attempts => [.. _attempts];

    /// <summary>Runs after the call is recorded; throw from it or wait on the token to simulate a failing or a hanging hub.</summary>
    public Func<TicketChange, CancellationToken, Task>? Behaviour { get; set; }

    public Task PublishAsync(TicketChange change, CancellationToken cancellationToken)
    {
        _attempts.Enqueue(change);
        return Behaviour?.Invoke(change, cancellationToken) ?? Task.CompletedTask;
    }

    public Task PublishPresenceAsync(TicketPresence presence, CancellationToken cancellationToken) => Task.CompletedTask;
}
