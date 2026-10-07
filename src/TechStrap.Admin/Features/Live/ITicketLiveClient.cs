using TechStrap.Contracts.Live;

namespace TechStrap.Admin.Features.Live;

/// <summary>The state of the live connection, as the indicator draws it. An Admin type: Contracts holds no enums (D-046).</summary>
public enum LiveConnectionState
{
    /// <summary>Not connected and not trying: before the first start, after a stop, after a lapsed session or a refused token, and when live updates are switched off.</summary>
    Disconnected,

    /// <summary>The first connection attempt is running.</summary>
    Connecting,

    /// <summary>Connected: changes and presence arrive.</summary>
    Connected,

    /// <summary>The connection was lost, or the first attempt failed; the client keeps trying with a capped backoff.</summary>
    Reconnecting,
}

/// <summary>
/// The Admin's one live connection per circuit (PHASE-10b). Components depend on this and never on SignalR. Every method is safe to call at any time and never throws: a failure of the hub is logged
/// and shows as <see cref="LiveConnectionState.Reconnecting"/> or <see cref="LiveConnectionState.Disconnected"/>, so live updates can never break a page. The events are raised on a thread-pool thread: a component
/// marshals with <c>InvokeAsync</c>, and unsubscribes in its own disposal.
/// </summary>
public interface ITicketLiveClient : IAsyncDisposable
{
    /// <summary>False for the client that is used when <c>LiveUpdates:Enabled</c> is off: the indicator, the banners and the presence bar are not drawn at all.</summary>
    bool IsEnabled { get; }

    LiveConnectionState State { get; }

    /// <summary>Raised when <see cref="State"/> changes, with the new state.</summary>
    event Action<LiveConnectionState>? StateChanged;

    /// <summary>
    /// A change to a ticket, in the order it arrived, each event id once. After a reconnect (and whenever the hub says so) a change of kind <c>Resync</c> arrives: reload everything. It is not filtered for the
    /// agent's own changes: <see cref="LiveChangeRules.IsOwn"/> does that, where the signed-in agent is known.
    /// </summary>
    event Action<TicketChangedDto>? TicketChanged;

    /// <summary>The viewers of a ticket the client has joined, including the agent. Also raised with the answer of every re-join after a connect.</summary>
    event Action<TicketPresenceDto>? PresenceChanged;

    /// <summary>
    /// Opens the connection, once per client however many components ask. Call it only from <c>OnAfterRenderAsync</c>, never during prerendering: a prerender scope has no circuit and no token path.
    /// The task ends when the connection is first established or the client gives up (a lapsed session, no token, a disposal); callers need not await it.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks to see who else has the ticket open. The ticket is also remembered and joined again after every reconnect. Returns the current viewers, or null when not connected yet, when the hub does not
    /// know the ticket, or when the call failed.
    /// </summary>
    Task<TicketPresenceDto?> JoinTicketAsync(Guid ticketId, CancellationToken cancellationToken = default);

    Task LeaveTicketAsync(Guid ticketId, CancellationToken cancellationToken = default);

    Task SetComposingAsync(Guid ticketId, bool isComposing, CancellationToken cancellationToken = default);
}
