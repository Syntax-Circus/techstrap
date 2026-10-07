using Microsoft.AspNetCore.SignalR.Client;
using TechStrap.Contracts.Live;

namespace TechStrap.Admin.Features.Live;

/// <summary>What a connection needs from the client: where its access token comes from, and when to retry.</summary>
/// <param name="AccessToken">Asked for on every start and every reconnect. Null means "not signed in": the client stops. It may throw; that fails the attempt.</param>
/// <param name="RetryPolicy">Decides the delay before each reconnect attempt, or that there will be none.</param>
public sealed record LiveConnectionOptions(Func<Task<string?>> AccessToken, IRetryPolicy RetryPolicy);

/// <summary>
/// The hub connection the client drives, as a small abstraction, so the client's logic (state, de-duplication, re-joining, disposal) is tested with a scripted connection and the real one
/// is exercised against the real hub. The events mirror the hub's; the lifecycle ones are asynchronous because the client re-joins its tickets from them.
/// </summary>
public interface ILiveConnection : IAsyncDisposable
{
    event Action<TicketChangedDto>? TicketChanged;

    event Action<TicketPresenceDto>? PresenceChanged;

    /// <summary>The connection was lost and the retry policy is running.</summary>
    event Func<Task>? Reconnecting;

    /// <summary>A reconnect succeeded. The server has forgotten every group of the old connection.</summary>
    event Func<Task>? Reconnected;

    /// <summary>The connection ended for good: the retry policy said stop, or it was stopped.</summary>
    event Func<Task>? Closed;

    Task StartAsync(CancellationToken cancellationToken);

    Task<TicketPresenceDto> JoinTicketAsync(Guid ticketId, CancellationToken cancellationToken);

    Task LeaveTicketAsync(Guid ticketId, CancellationToken cancellationToken);

    Task SetComposingAsync(Guid ticketId, bool isComposing, CancellationToken cancellationToken);
}

public interface ILiveConnectionFactory
{
    ILiveConnection Create(LiveConnectionOptions options);
}
