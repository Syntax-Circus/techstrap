using Microsoft.Extensions.Logging;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Live;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Live;

public interface IUpdateTicketPresenceHandler
{
    /// <returns>The ticket's presence after the action; for <c>LeaveAll</c>, an empty presence for no ticket.</returns>
    Task<Result<TicketPresence>> HandleAsync(UpdateTicketPresenceRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The hub's presence use case: Join, Leave, LeaveAll and SetComposing (one handler, one action discriminator). The caller's identity arrives in the
/// request, filled from the hub's own context, because <c>ICurrentAgentClaims</c> and <c>IHttpContextAccessor</c> are not reliable inside a hub
/// (D-046). Join and SetComposing re-check that the agent is still active; Leave and LeaveAll never do, so anyone can always clean up. Presence is
/// pushed to the ticket's group only when what other agents would see really changed, and a broadcast failure never fails the call.
/// </summary>
public sealed class UpdateTicketPresenceHandler(
    IAgentRepository agents,
    ITicketRepository tickets,
    ITicketPresenceStore presence,
    ITicketChangeBroadcaster broadcaster,
    ILogger<UpdateTicketPresenceHandler> logger) : IUpdateTicketPresenceHandler
{
    public async Task<Result<TicketPresence>> HandleAsync(UpdateTicketPresenceRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionId) || request.Action is null)
        {
            return Result<TicketPresence>.Failure(LiveErrors.PresenceInvalid());
        }

        return request.Action switch
        {
            TicketPresenceActions.LeaveAll => await LeaveAllAsync(request, cancellationToken),
            TicketPresenceActions.Leave when request.TicketId is { } ticketId => await LeaveAsync(request, ticketId, cancellationToken),
            TicketPresenceActions.Join when request.TicketId is { } ticketId => await JoinAsync(request, ticketId, cancellationToken),
            TicketPresenceActions.SetComposing when request.TicketId is { } ticketId => await SetComposingAsync(request, ticketId, cancellationToken),
            _ => Result<TicketPresence>.Failure(LiveErrors.PresenceInvalid()),
        };
    }

    private async Task<Result<TicketPresence>> JoinAsync(UpdateTicketPresenceRequest request, Guid ticketId, CancellationToken cancellationToken)
    {
        var agent = await RequireActiveAgentAsync(request.AgentSubject, cancellationToken);
        if (agent.IsFailure)
        {
            return Result<TicketPresence>.Failure(agent.Errors[0]);
        }

        // The existence check is what stops a client from probing group names with arbitrary ids.
        if (await tickets.GetStateAsync(ticketId, cancellationToken) is null)
        {
            return Result<TicketPresence>.Failure(LiveErrors.TicketNotFound());
        }

        var name = agent.Value.Name ?? agent.Value.Email;
        var change = presence.Join(request.ConnectionId, ticketId, agent.Value.Id, name);
        await BroadcastAsync(change, cancellationToken);
        return Result<TicketPresence>.Success(change.Presence);
    }

    private async Task<Result<TicketPresence>> SetComposingAsync(UpdateTicketPresenceRequest request, Guid ticketId, CancellationToken cancellationToken)
    {
        var agent = await RequireActiveAgentAsync(request.AgentSubject, cancellationToken);
        if (agent.IsFailure)
        {
            return Result<TicketPresence>.Failure(agent.Errors[0]);
        }

        if (presence.SetComposing(request.ConnectionId, ticketId, request.IsComposing) is not { } change)
        {
            return Result<TicketPresence>.Failure(LiveErrors.PresenceNotJoined());
        }

        await BroadcastAsync(change, cancellationToken);
        return Result<TicketPresence>.Success(change.Presence);
    }

    private async Task<Result<TicketPresence>> LeaveAsync(UpdateTicketPresenceRequest request, Guid ticketId, CancellationToken cancellationToken)
    {
        var change = presence.Leave(request.ConnectionId, ticketId);
        await BroadcastAsync(change, cancellationToken);
        return Result<TicketPresence>.Success(change.Presence);
    }

    private async Task<Result<TicketPresence>> LeaveAllAsync(UpdateTicketPresenceRequest request, CancellationToken cancellationToken)
    {
        foreach (var change in presence.LeaveAll(request.ConnectionId))
        {
            await BroadcastAsync(change, cancellationToken);
        }

        return Result<TicketPresence>.Success(new TicketPresence(Guid.Empty, []));
    }

    private async Task<Result<Agent>> RequireActiveAgentAsync(string subject, CancellationToken cancellationToken)
    {
        var agent = string.IsNullOrWhiteSpace(subject) ? null : await agents.GetBySubjectAsync(subject, cancellationToken);
        if (agent is null)
        {
            return Result<Agent>.Failure(AgentErrors.NotProvisioned());
        }

        return agent.IsActive ? Result<Agent>.Success(agent) : Result<Agent>.Failure(AgentErrors.Inactive());
    }

    private async Task BroadcastAsync(PresenceChange change, CancellationToken cancellationToken)
    {
        if (!change.Changed)
        {
            return;
        }

        try
        {
            await broadcaster.PublishPresenceAsync(change.Presence, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Presence is a hint: a failed push is logged by type name only and never fails the agent's call.
            logger.LogWarning("Broadcasting presence failed ({ExceptionType}).", exception.GetType().Name);
        }
    }
}
