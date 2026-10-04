using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Application.Tickets.Notifications;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tickets;

public interface IChangeTicketStatusRequestHandler
{
    Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, ChangeTicketStatusRequest request, CancellationToken cancellationToken);
}

/// <summary>An explicit status change. The RowVersion is required (D-036); solving plans the solved notice in the same unit of work.</summary>
public sealed class ChangeTicketStatusRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    ITicketRepository tickets,
    ITicketNotificationPlanner planner,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IChangeTicketStatusRequestHandler
{
    public async Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, ChangeTicketStatusRequest request, CancellationToken cancellationToken)
    {
        if (!TicketNameParser.TryStatus(request.Status, out var to))
        {
            return TicketMutation.Fail(TicketErrors.Invalid("status", "status-invalid", "Choose a status: New, Open, Pending, Solved or Closed."));
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        var loaded = await TicketMutation.LoadAsync(ticketId, request.RowVersion, rowVersionRequired: true, currentAgent, agents, tickets, cancellationToken);
        if (loaded.IsFailure)
        {
            return TicketMutation.Fail(loaded.Errors[0]);
        }

        var (agent, ticket) = loaded.Value;

        var changed = ticket.ChangeStatus(to, Actor.ForAgent(agent.Id), clock);
        if (changed.IsFailure)
        {
            return TicketMutation.Fail(changed.Error!.ToError());
        }

        tickets.Update(ticket);
        if (to == TicketStatus.Solved)
        {
            await planner.PlanSolvedAsync(ticket, cancellationToken);
        }

        return await TicketMutation.CommitAsync(scope, ticket.Id, tickets, cancellationToken);
    }
}
