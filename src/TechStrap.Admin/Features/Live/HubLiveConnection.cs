using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using SyntaxCircus.Blazor.Auth;
using TechStrap.Contracts.Live;

namespace TechStrap.Admin.Features.Live;

/// <summary>
/// The real connection: a <see cref="HubConnection"/> to the Api's ticket hub, server to server (the browser never reaches <c>/hubs</c>, D-046). The address is the Api base address plus
/// <see cref="TicketHubRoutes.Path"/>; the token goes in the Authorization header through <c>AccessTokenProvider</c> (never in the URL); the method and group names come from Contracts.
/// </summary>
internal sealed class HubLiveConnection : ILiveConnection
{
    private readonly HubConnection _hub;

    public HubLiveConnection(Uri hubUri, LiveConnectionOptions options, Action<HttpConnectionOptions>? configure)
    {
        // No logging is configured on purpose: a logger provider would route SignalR's internals, possibly with exception text (a URL, a token), to Serilog.
        _hub = new HubConnectionBuilder()
            .WithUrl(hubUri, http =>
            {
                http.AccessTokenProvider = options.AccessToken;
                configure?.Invoke(http);
            })
            .WithAutomaticReconnect(options.RetryPolicy)
            .Build();
        _hub.On<TicketChangedDto>(TicketHubMethods.TicketChanged, change => TicketChanged?.Invoke(change));
        _hub.On<TicketPresenceDto>(TicketHubMethods.PresenceChanged, presence => PresenceChanged?.Invoke(presence));
        _hub.Reconnecting += _ => Reconnecting?.Invoke() ?? Task.CompletedTask;
        _hub.Reconnected += _ => Reconnected?.Invoke() ?? Task.CompletedTask;
        _hub.Closed += _ => Closed?.Invoke() ?? Task.CompletedTask;
    }

    public event Action<TicketChangedDto>? TicketChanged;

    public event Action<TicketPresenceDto>? PresenceChanged;

    public event Func<Task>? Reconnecting;

    public event Func<Task>? Reconnected;

    public event Func<Task>? Closed;

    public Task StartAsync(CancellationToken cancellationToken) => _hub.StartAsync(cancellationToken);

    public Task<TicketPresenceDto> JoinTicketAsync(Guid ticketId, CancellationToken cancellationToken) =>
        _hub.InvokeAsync<TicketPresenceDto>(TicketHubMethods.JoinTicket, ticketId, cancellationToken);

    public Task LeaveTicketAsync(Guid ticketId, CancellationToken cancellationToken) =>
        _hub.InvokeAsync(TicketHubMethods.LeaveTicket, ticketId, cancellationToken);

    public Task SetComposingAsync(Guid ticketId, bool isComposing, CancellationToken cancellationToken) =>
        _hub.InvokeAsync(TicketHubMethods.SetComposing, ticketId, isComposing, cancellationToken);

    public ValueTask DisposeAsync() => _hub.DisposeAsync();
}

/// <summary>
/// Builds <see cref="HubLiveConnection"/>s for the Api address in the validated <c>Api</c> options. A singleton: it holds options only, never a scoped service. <paramref name="configure"/> is for tests
/// (a handler that reaches an in-memory server, a transport); production passes none.
/// </summary>
public sealed class HubLiveConnectionFactory(IOptions<ApiOptions> api, Action<HttpConnectionOptions>? configure = null) : ILiveConnectionFactory
{
    /// <summary>The Api base address with the hub path appended. The base may carry a path (a reverse proxy prefix), so the path is joined, not replaced.</summary>
    public static Uri HubUri(string apiBaseUrl) =>
        new(new Uri(apiBaseUrl.EndsWith('/') ? apiBaseUrl : apiBaseUrl + "/"), TicketHubRoutes.Path.TrimStart('/'));

    public ILiveConnection Create(LiveConnectionOptions options) => new HubLiveConnection(HubUri(api.Value.BaseUrl), options, configure);
}
