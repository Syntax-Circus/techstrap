using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Application.Tickets;

public interface ICountTicketViewsRequestHandler
{
    Task<Result<TicketViewCountsResponse>> HandleAsync(CancellationToken cancellationToken);
}

/// <summary>GET /api/tickets/counts. The numbers on the queue tabs, for the signed-in agent.</summary>
public sealed class CountTicketViewsRequestHandler(ICurrentAgentClaims currentAgent, IAgentRepository agents, ITicketRepository tickets)
    : ICountTicketViewsRequestHandler
{
    public async Task<Result<TicketViewCountsResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var agent = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (agent.IsFailure)
        {
            return Result<TicketViewCountsResponse>.Failure(agent.Errors[0]);
        }

        var counts = await tickets.CountViewsAsync(agent.Value.Id, cancellationToken);
        return Result<TicketViewCountsResponse>.Success(
            new TicketViewCountsResponse(counts.Unassigned, counts.Mine, counts.Open, counts.Pending, counts.All, counts.Spam));
    }
}
