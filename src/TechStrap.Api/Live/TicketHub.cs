using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using SyntaxCircus.Common;
using TechStrap.Api.Options;
using TechStrap.Api.Security;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;
using TechStrap.Infrastructure.Live;

namespace TechStrap.Api.Live;

/// <summary>
/// The agent hub at <see cref="TicketHubRoutes.Path"/> (D-007, D-018, D-046). Agents only: the Agent policy is on the route and repeated on the class, the token travels in the
/// <c>Authorization</c> header (a query token is not read), and the connection closes when the token expires. Every connection joins the <c>queue</c> group, which carries
/// <c>TicketChanged</c>; a ticket group carries presence only and is joined only for a ticket that exists. The methods are thin: they build a request from the hub's own
/// caller (<c>Context.User</c>, <c>Context.ConnectionId</c>) and let <see cref="IUpdateTicketPresenceHandler"/> decide, so a client can never speak for another agent
/// and <c>IHttpContextAccessor</c> is never needed inside a hub.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.Agent)]
public sealed class TicketHub(IUpdateTicketPresenceHandler presence, IOptions<AgentAccessOptions> access, TechStrapMetrics metrics) : Hub
{
    public override async Task OnConnectedAsync()
    {
        metrics.AgentConnected(Context.ConnectionId, Subject());
        await Groups.AddToGroupAsync(Context.ConnectionId, TicketHubGroups.Queue, Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    /// <summary>Opens a ticket: returns who has it open now (the caller included); everyone else on the ticket is told when that changed.</summary>
    public async Task<TicketPresenceDto> JoinTicket(Guid ticketId)
    {
        var result = await presence.HandleAsync(Request(TicketPresenceActions.Join, ticketId), Context.ConnectionAborted);
        if (result.IsFailure)
        {
            throw Refusal(result.Errors[0]);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, TicketHubGroups.Ticket(ticketId), Context.ConnectionAborted);
        return result.Value.ToDto();
    }

    public async Task LeaveTicket(Guid ticketId)
    {
        var result = await presence.HandleAsync(Request(TicketPresenceActions.Leave, ticketId), Context.ConnectionAborted);
        if (result.IsFailure)
        {
            throw Refusal(result.Errors[0]);
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, TicketHubGroups.Ticket(ticketId), Context.ConnectionAborted);
    }

    public async Task SetComposing(Guid ticketId, bool isComposing)
    {
        var result = await presence.HandleAsync(Request(TicketPresenceActions.SetComposing, ticketId, isComposing), Context.ConnectionAborted);
        if (result.IsFailure)
        {
            throw Refusal(result.Errors[0]);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        metrics.AgentDisconnected(Context.ConnectionId);

        // The connection is already gone, so its abort token is spent: cleaning up must not depend on it. SignalR removes the connection from its groups itself.
        await presence.HandleAsync(Request(TicketPresenceActions.LeaveAll, null), CancellationToken.None);
        await base.OnDisconnectedAsync(exception);
    }

    private UpdateTicketPresenceRequest Request(string action, Guid? ticketId, bool isComposing = false) =>
        new(action, Subject(), Context.ConnectionId, ticketId, isComposing);

    private string Subject() => ClaimsCurrentAgentClaims.FromPrincipal(Context.User!, access.Value)?.Subject ?? string.Empty;

    /// <summary>Only the fixed texts of the handler's outcomes reach the client; an unknown ticket is the one the spec names.</summary>
    private static HubException Refusal(ResultError error) =>
        new(error.Kind == ResultErrorKind.NotFound ? TicketHubMessages.TicketNotFound : error.Message);
}
