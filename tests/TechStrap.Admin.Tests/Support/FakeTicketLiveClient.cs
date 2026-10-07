using TechStrap.Admin.Features.Live;
using TechStrap.Contracts.Live;

namespace TechStrap.Admin.Tests.Support;

/// <summary>
/// The <see cref="ITicketLiveClient"/> every component test gets (<see cref="AdminComponentTest"/> registers it): events are raised synchronously by the test, calls are recorded, and nothing ever
/// reaches a hub. <see cref="HasSubscribers"/> proves a component unsubscribed when it was disposed.
/// </summary>
public sealed class FakeTicketLiveClient : ITicketLiveClient
{
    private LiveConnectionState _state = LiveConnectionState.Connected;

    public bool IsEnabled { get; set; } = true;

    public LiveConnectionState State => _state;

    public int StartCalls { get; private set; }

    public int DisposeCalls { get; private set; }

    public List<Guid> Joined { get; } = [];

    public List<Guid> Left { get; } = [];

    public List<(Guid TicketId, bool IsComposing)> Composing { get; } = [];

    /// <summary>What a join answers; null (the default) is "not connected yet".</summary>
    public Func<Guid, TicketPresenceDto?> JoinResult { get; set; } = _ => null;

    /// <summary>When set, every call throws it, to prove a failing client never breaks a page.</summary>
    public Exception? Failure { get; set; }

    public bool HasSubscribers => StateChanged is not null || TicketChanged is not null || PresenceChanged is not null;

    public event Action<LiveConnectionState>? StateChanged;

    public event Action<TicketChangedDto>? TicketChanged;

    public event Action<TicketPresenceDto>? PresenceChanged;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        StartCalls++;
        return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
    }

    public Task<TicketPresenceDto?> JoinTicketAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        Joined.Add(ticketId);
        return Failure is null ? Task.FromResult(JoinResult(ticketId)) : Task.FromException<TicketPresenceDto?>(Failure);
    }

    public Task LeaveTicketAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        Left.Add(ticketId);
        return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
    }

    public Task SetComposingAsync(Guid ticketId, bool isComposing, CancellationToken cancellationToken = default)
    {
        Composing.Add((ticketId, isComposing));
        return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        return ValueTask.CompletedTask;
    }

    /// <summary>Raises <see cref="StateChanged"/> on the calling thread, as the real client does on a pool thread; the component marshals.</summary>
    public void SetState(LiveConnectionState state)
    {
        _state = state;
        StateChanged?.Invoke(state);
    }

    public void RaiseChange(TicketChangedDto change) => TicketChanged?.Invoke(change);

    public void RaisePresence(TicketPresenceDto presence) => PresenceChanged?.Invoke(presence);
}
