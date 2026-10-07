using TechStrap.Contracts.Live;

namespace TechStrap.Admin.Features.Live;

/// <summary>
/// The client used when <c>LiveUpdates:Enabled</c> is off: it never connects, never raises an event and answers every call at once. <see cref="IsEnabled"/> is false, so the indicator, the banners and
/// the presence bar are not drawn and the pages behave exactly as they did before live updates.
/// </summary>
public sealed class NullTicketLiveClient : ITicketLiveClient
{
    public bool IsEnabled => false;

    public LiveConnectionState State => LiveConnectionState.Disconnected;

    // Declared so a component can subscribe and unsubscribe; nothing ever raises them.
#pragma warning disable CS0067
    public event Action<LiveConnectionState>? StateChanged;

    public event Action<TicketChangedDto>? TicketChanged;

    public event Action<TicketPresenceDto>? PresenceChanged;
#pragma warning restore CS0067

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<TicketPresenceDto?> JoinTicketAsync(Guid ticketId, CancellationToken cancellationToken = default) => Task.FromResult<TicketPresenceDto?>(null);

    public Task LeaveTicketAsync(Guid ticketId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SetComposingAsync(Guid ticketId, bool isComposing, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
