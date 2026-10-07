using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SyntaxCircus.Blazor.Auth;
using TechStrap.Admin.Auth;
using TechStrap.Contracts.Live;

namespace TechStrap.Admin.Features.Live;

/// <summary>
/// The Admin's live client (D-046): a scoped service, so one per circuit, and the only owner of the hub connection. It starts once, when a component that is already rendered in the circuit asks (never during
/// prerendering), asks <see cref="IUserAccessTokenProvider"/> for the agent's token on every connection attempt (the token travels in the Authorization header only), keeps retrying with
/// <see cref="LiveRetryPolicy"/> while the session lives, joins its tickets again after every connect, tells its subscribers to resync after a reconnect (the notifications of the gap are gone), and
/// drops an event id it has just delivered. It does not marshal threads and never throws: every failure is logged by type (never the message, which could carry a token or a name) and shows as a state.
/// </summary>
public sealed class SignalRTicketLiveClient : ITicketLiveClient
{
    /// <summary>How many event ids are remembered for de-duplication. A repeat older than this is delivered again, which a page treats as one more reason to offer a refresh.</summary>
    public const int DeduplicationCapacity = 256;

    private readonly ILiveConnectionFactory _connections;
    private readonly IUserAccessTokenProvider _tokens;
    private readonly SessionExpiry _expiry;
    private readonly TimeProvider _time;
    private readonly ILogger<SignalRTicketLiveClient> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _stopping;
    private readonly object _gate = new();
    private readonly HashSet<Guid> _joined = [];
    private readonly Queue<Guid> _seenOrder = new();
    private readonly HashSet<Guid> _seen = [];
    private ILiveConnection? _connection;
    private Task? _starting;
    private LiveConnectionState _state;
    private bool _disposed;
    private volatile bool _noToken;

    public SignalRTicketLiveClient(ILiveConnectionFactory connections, IUserAccessTokenProvider tokens, SessionExpiry expiry, TimeProvider time, ILogger<SignalRTicketLiveClient> logger)
    {
        _connections = connections;
        _tokens = tokens;
        _expiry = expiry;
        _time = time;
        _logger = logger;
        _stopping = _lifetime.Token;
    }

    public bool IsEnabled => true;

    public LiveConnectionState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public event Action<LiveConnectionState>? StateChanged;

    public event Action<TicketChangedDto>? TicketChanged;

    public event Action<TicketPresenceDto>? PresenceChanged;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            // On the pool, so the start loop, the retry delays and the re-joins never run on the renderer's context (the caller is a component).
            return _starting ??= Task.Run(RunAsync, CancellationToken.None);
        }
    }

    public async Task<TicketPresenceDto?> JoinTicketAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        ILiveConnection? connection;
        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            _joined.Add(ticketId);
            connection = _state == LiveConnectionState.Connected ? _connection : null;
        }

        return connection is null ? null : await JoinAsync(connection, ticketId, cancellationToken);
    }

    public async Task LeaveTicketAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        ILiveConnection? connection;
        lock (_gate)
        {
            _joined.Remove(ticketId);
            connection = _state == LiveConnectionState.Connected && !_disposed ? _connection : null;
        }

        if (connection is not null)
        {
            await InvokeSafelyAsync(() => connection.LeaveTicketAsync(ticketId, cancellationToken), nameof(ITicketLiveClient.LeaveTicketAsync));
        }
    }

    public async Task SetComposingAsync(Guid ticketId, bool isComposing, CancellationToken cancellationToken = default)
    {
        ILiveConnection? connection;
        lock (_gate)
        {
            connection = _state == LiveConnectionState.Connected && !_disposed ? _connection : null;
        }

        if (connection is not null)
        {
            await InvokeSafelyAsync(() => connection.SetComposingAsync(ticketId, isComposing, cancellationToken), nameof(ITicketLiveClient.SetComposingAsync));
        }
    }

    public async ValueTask DisposeAsync()
    {
        ILiveConnection? connection;
        Task? starting;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _state = LiveConnectionState.Disconnected;
            connection = _connection;
            starting = _starting;
        }

        await _lifetime.CancelAsync();
        if (connection is not null)
        {
            connection.TicketChanged -= OnTicketChanged;
            connection.PresenceChanged -= OnPresenceChanged;
            connection.Reconnecting -= OnReconnectingAsync;
            connection.Reconnected -= OnReconnectedAsync;
            connection.Closed -= OnClosedAsync;
            await DisposeQuietlyAsync(connection);
        }

        if (starting is not null)
        {
            await starting;
        }

        TicketChanged = null;
        PresenceChanged = null;
        StateChanged = null;
        _lifetime.Dispose();
    }

    private async Task DisposeQuietlyAsync(ILiveConnection connection)
    {
        try
        {
            await connection.DisposeAsync();
        }
        catch (Exception exception)
        {
            _logger.LogWarning("The live connection could not be closed cleanly ({ExceptionType}).", exception.GetType().Name);
        }
    }

    /// <summary>No token, or a lapsed session: no attempt, start or reconnect, can succeed any more.</summary>
    private bool ShouldStop() => _noToken || _expiry.IsLapsed;

    private bool IsDisposed
    {
        get
        {
            lock (_gate)
            {
                return _disposed;
            }
        }
    }

    /// <summary>The first connection: attempts with the retry policy's delays until one succeeds, the session lapses, no token comes, or the client is disposed. Never throws.</summary>
    private async Task RunAsync()
    {
        try
        {
            SetState(LiveConnectionState.Connecting);
            var connection = _connections.Create(new LiveConnectionOptions(GetTokenAsync, new LiveRetryPolicy(ShouldStop)));
            bool disposedMeanwhile;
            lock (_gate)
            {
                // DisposeAsync reads _connection under this lock: publish only while it has not run, otherwise nobody else would dispose this connection.
                disposedMeanwhile = _disposed;
                if (!disposedMeanwhile)
                {
                    _connection = connection;
                }
            }

            if (disposedMeanwhile)
            {
                await DisposeQuietlyAsync(connection);
                return;
            }

            connection.TicketChanged += OnTicketChanged;
            connection.PresenceChanged += OnPresenceChanged;
            connection.Reconnecting += OnReconnectingAsync;
            connection.Reconnected += OnReconnectedAsync;
            connection.Closed += OnClosedAsync;

            for (var attempt = 0; !_stopping.IsCancellationRequested; attempt++)
            {
                try
                {
                    await connection.StartAsync(_stopping);
                }
                catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning("The live connection could not be started ({ExceptionType}).", exception.GetType().Name);
                    if (ShouldStop())
                    {
                        SetState(LiveConnectionState.Disconnected);
                        return;
                    }

                    SetState(LiveConnectionState.Reconnecting);
                    await Task.Delay(LiveRetryPolicy.DelayFor(attempt), _time, _stopping);
                    continue;
                }

                SetState(LiveConnectionState.Connected);
                await RejoinAsync(connection);
                return;
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // Disposed while waiting to retry: nothing more to do.
        }
        catch (Exception exception)
        {
            _logger.LogWarning("The live connection stopped unexpectedly ({ExceptionType}).", exception.GetType().Name);
            SetState(LiveConnectionState.Disconnected);
        }
    }

    /// <summary>
    /// The hub calls this on every connect and reconnect. A null token means there is no signed-in user: the client stops for good (a hub request without a token is refused, and every retry would be too).
    /// An exception means the token could not be refreshed this time: it propagates, the attempt fails, and the next one asks again.
    /// </summary>
    private async Task<string?> GetTokenAsync()
    {
        try
        {
            var token = await _tokens.GetAccessTokenAsync(_stopping);
            if (string.IsNullOrEmpty(token))
            {
                _noToken = true;
                _logger.LogWarning("No access token is available for the live connection; it will not be retried.");
                return null;
            }

            return token;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning("The access token for the live connection could not be obtained ({ExceptionType}).", exception.GetType().Name);
            throw;
        }
    }

    private void OnTicketChanged(TicketChangedDto change)
    {
        if (IsDisposed)
        {
            return;
        }

        if (change.EventId != Guid.Empty && !Remember(change.EventId))
        {
            return;
        }

        Raise(TicketChanged, change);
    }

    private void OnPresenceChanged(TicketPresenceDto presence)
    {
        if (!IsDisposed)
        {
            Raise(PresenceChanged, presence);
        }
    }

    private Task OnReconnectingAsync()
    {
        SetState(LiveConnectionState.Reconnecting);
        return Task.CompletedTask;
    }

    private async Task OnReconnectedAsync()
    {
        if (IsDisposed)
        {
            return;
        }

        ILiveConnection? connection;
        lock (_gate)
        {
            connection = _connection;
        }

        SetState(LiveConnectionState.Connected);

        // Whatever happened during the gap was not delivered (at most once): every page offers a refresh. The server forgot the old connection's groups: join the open tickets again.
        Raise(TicketChanged, LiveChangeRules.NewResync(_time.GetUtcNow()));
        if (connection is not null)
        {
            await RejoinAsync(connection);
        }
    }

    private Task OnClosedAsync()
    {
        SetState(LiveConnectionState.Disconnected);
        return Task.CompletedTask;
    }

    private async Task RejoinAsync(ILiveConnection connection)
    {
        Guid[] tickets;
        lock (_gate)
        {
            tickets = [.. _joined];
        }

        foreach (var ticketId in tickets)
        {
            // A ticket left (or a client disposed) since the snapshot is not joined again.
            if (IsDisposed || !IsJoined(ticketId))
            {
                continue;
            }

            if (await JoinAsync(connection, ticketId, _stopping) is { } presence)
            {
                if (!IsJoined(ticketId))
                {
                    // The page left the ticket while the join was in flight, and its leave may have reached the hub first: send one more leave (the connection handles invocations in order), or the others see a ghost viewer.
                    await InvokeSafelyAsync(() => connection.LeaveTicketAsync(ticketId, _stopping), nameof(ITicketLiveClient.LeaveTicketAsync));
                    continue;
                }

                if (!IsDisposed)
                {
                    Raise(PresenceChanged, presence);
                }
            }
        }
    }

    private bool IsJoined(Guid ticketId)
    {
        lock (_gate)
        {
            return _joined.Contains(ticketId);
        }
    }

    private async Task<TicketPresenceDto?> JoinAsync(ILiveConnection connection, Guid ticketId, CancellationToken cancellationToken)
    {
        try
        {
            return await connection.JoinTicketAsync(ticketId, cancellationToken);
        }
        catch (HubException exception) when (exception.Message.Contains(TicketHubMessages.TicketNotFound, StringComparison.Ordinal))
        {
            // The ticket is gone: the page finds out from its own load. Do not ask again after the next reconnect.
            lock (_gate)
            {
                _joined.Remove(ticketId);
            }

            return null;
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Joining a ticket on the live connection failed ({ExceptionType}).", exception.GetType().Name);
            return null;
        }
    }

    private async Task InvokeSafelyAsync(Func<Task> call, string operation)
    {
        try
        {
            await call();
        }
        catch (Exception exception)
        {
            _logger.LogWarning("The live call {Operation} failed ({ExceptionType}).", operation, exception.GetType().Name);
        }
    }

    /// <summary>True the first time an event id is seen; the newest <see cref="DeduplicationCapacity"/> ids are remembered, so the memory is bounded.</summary>
    private bool Remember(Guid eventId)
    {
        lock (_gate)
        {
            if (!_seen.Add(eventId))
            {
                return false;
            }

            _seenOrder.Enqueue(eventId);
            if (_seenOrder.Count > DeduplicationCapacity)
            {
                _seen.Remove(_seenOrder.Dequeue());
            }

            return true;
        }
    }

    private void SetState(LiveConnectionState state)
    {
        lock (_gate)
        {
            if (_disposed || _state == state)
            {
                return;
            }

            _state = state;
        }

        Raise(StateChanged, state);
    }

    /// <summary>Calls every subscriber on its own: one that throws is logged and does not keep the others (or the hub's receive loop) from running.</summary>
    private void Raise<T>(Action<T>? handlers, T argument)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action<T>>())
        {
            try
            {
                handler(argument);
            }
            catch (Exception exception)
            {
                _logger.LogWarning("A live-update subscriber failed ({ExceptionType}).", exception.GetType().Name);
            }
        }
    }
}
