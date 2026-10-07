using System.Net;
using TechStrap.Admin.Features.Live;
using TechStrap.Contracts.Live;

namespace TechStrap.Admin.Tests.Live;

/// <summary>
/// A scripted <see cref="ILiveConnection"/>. Like the real one, <c>StartAsync</c> asks for the access token first and fails when there is none (the hub answers 401), so a test sees what
/// the client does with a null token or a throwing provider. The test raises the events the hub connection would raise.
/// </summary>
internal sealed class FakeLiveConnection(LiveConnectionOptions options) : ILiveConnection
{
    public LiveConnectionOptions Options { get; } = options;

    public int StartCalls { get; private set; }

    public int DisposeCalls { get; private set; }

    /// <summary>How many of the next starts fail with a transport error, after the token has been asked for.</summary>
    public int FailStarts { get; set; }

    public List<Guid> Joined { get; } = [];

    public List<Guid> Left { get; } = [];

    public List<(Guid TicketId, bool IsComposing)> Composing { get; } = [];

    /// <summary>What a join answers; the default is an empty presence for the ticket. A test sets it to throw.</summary>
    public Func<Guid, Task<TicketPresenceDto>> JoinHandler { get; set; } = ticketId => Task.FromResult(new TicketPresenceDto(ticketId, []));

    /// <summary>When set, <c>LeaveTicket</c> and <c>SetComposing</c> throw it.</summary>
    public Exception? InvokeFailure { get; set; }

    public bool HasSubscribers => TicketChanged is not null || PresenceChanged is not null || _reconnecting is not null || _reconnected is not null || _closed is not null;

    public event Action<TicketChangedDto>? TicketChanged;

    public event Action<TicketPresenceDto>? PresenceChanged;

    // Each of the three connection events also remembers every handler ever added, as a hub callback that was already running at disposal would still hold it.
    private Func<Task>? _reconnecting;
    private Func<Task>? _reconnectingEver;
    private Func<Task>? _reconnected;
    private Func<Task>? _reconnectedEver;
    private Func<Task>? _closed;
    private Func<Task>? _closedEver;

    public event Func<Task>? Reconnecting
    {
        add
        {
            _reconnecting += value;
            _reconnectingEver += value;
        }
        remove => _reconnecting -= value;
    }

    public event Func<Task>? Reconnected
    {
        add
        {
            _reconnected += value;
            _reconnectedEver += value;
        }
        remove => _reconnected -= value;
    }

    public event Func<Task>? Closed
    {
        add
        {
            _closed += value;
            _closedEver += value;
        }
        remove => _closed -= value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        StartCalls++;
        var token = await Options.AccessToken();
        if (token is null)
        {
            throw new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized);
        }

        if (FailStarts > 0)
        {
            FailStarts--;
            throw new HttpRequestException("Connection refused");
        }
    }

    public Task<TicketPresenceDto> JoinTicketAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        Joined.Add(ticketId);
        return JoinHandler(ticketId);
    }

    public Task LeaveTicketAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        Left.Add(ticketId);
        return InvokeFailure is null ? Task.CompletedTask : Task.FromException(InvokeFailure);
    }

    public Task SetComposingAsync(Guid ticketId, bool isComposing, CancellationToken cancellationToken)
    {
        Composing.Add((ticketId, isComposing));
        return InvokeFailure is null ? Task.CompletedTask : Task.FromException(InvokeFailure);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        return ValueTask.CompletedTask;
    }

    public void RaiseChanged(TicketChangedDto change) => TicketChanged?.Invoke(change);

    public void RaisePresence(TicketPresenceDto presence) => PresenceChanged?.Invoke(presence);

    public Task RaiseReconnectingAsync() => _reconnecting?.Invoke() ?? Task.CompletedTask;

    public Task RaiseReconnectedAsync() => _reconnected?.Invoke() ?? Task.CompletedTask;

    public Task RaiseClosedAsync() => _closed?.Invoke() ?? Task.CompletedTask;

    /// <summary>The Reconnecting, Reconnected and Closed callbacks of a hub that were already running when the client was disposed: they still reach the handlers the client had added.</summary>
    public async Task RaiseStaleConnectionEventsAsync()
    {
        await (_reconnectingEver?.Invoke() ?? Task.CompletedTask);
        await (_reconnectedEver?.Invoke() ?? Task.CompletedTask);
        await (_closedEver?.Invoke() ?? Task.CompletedTask);
    }
}

internal sealed class FakeLiveConnectionFactory : ILiveConnectionFactory
{
    public List<FakeLiveConnection> Created { get; } = [];

    public FakeLiveConnection Only => Created.Single();

    public ILiveConnection Create(LiveConnectionOptions options)
    {
        var connection = new FakeLiveConnection(options);
        Created.Add(connection);
        return connection;
    }
}
