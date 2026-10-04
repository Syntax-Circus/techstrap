using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Application.Tickets.Notifications;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tickets;

public interface IAssignTicketRequestHandler
{
    Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, AssignTicketRequest request, CancellationToken cancellationToken);
}

/// <summary>Assigns the ticket to an active agent, or unassigns it when no assignee is given. The row version is required (D-036).</summary>
public sealed class AssignTicketRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    ITicketRepository tickets,
    ITicketNotificationPlanner planner,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IAssignTicketRequestHandler
{
    public async Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, AssignTicketRequest request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        var loaded = await TicketMutation.LoadAsync(ticketId, request.RowVersion, rowVersionRequired: true, currentAgent, agents, tickets, cancellationToken);
        if (loaded.IsFailure)
        {
            return Fail(loaded.Errors[0]);
        }

        var (agent, ticket) = loaded.Value;

        Agent? assignee = null;
        if (request.AssigneeId is { } assigneeId)
        {
            assignee = await agents.GetByIdAsync(assigneeId, cancellationToken);
            if (assignee is null)
            {
                return Fail(TicketErrors.AgentNotFound());
            }

            if (!assignee.IsActive)
            {
                return Fail(TicketErrors.Invalid("assigneeId", "assignee-inactive", "That agent is inactive. Choose an active agent."));
            }
        }

        var before = ticket.AssigneeId;
        var assigned = ticket.Assign(assignee?.Id, Actor.ForAgent(agent.Id), clock);
        if (assigned.IsFailure)
        {
            return Fail(assigned.Error!.ToError());
        }

        tickets.Update(ticket);
        if (assignee is not null && before != assignee.Id)
        {
            await planner.PlanAssignedAsync(ticket, assignee, agent, cancellationToken);
        }

        return await TicketMutation.CommitAsync(scope, ticket.Id, tickets, cancellationToken);
    }

    private static Result<TicketStateDto> Fail(ResultError error) => Result<TicketStateDto>.Failure(error);
}
