using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tickets;

public interface IChangeTicketPriorityRequestHandler
{
    Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, ChangeTicketPriorityRequest request, CancellationToken cancellationToken);
}

/// <summary>Changes the priority. The row version is required (D-036); setting the current priority is a no-op that still succeeds.</summary>
public sealed class ChangeTicketPriorityRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    ITicketRepository tickets,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IChangeTicketPriorityRequestHandler
{
    public async Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, ChangeTicketPriorityRequest request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        var loaded = await TicketMutation.LoadAsync(ticketId, request.RowVersion, rowVersionRequired: true, currentAgent, agents, tickets, cancellationToken);
        if (loaded.IsFailure)
        {
            return Fail(loaded.Errors[0]);
        }

        var (agent, ticket) = loaded.Value;

        if (!TicketNameParser.TryPriority(request.Priority, out var priority))
        {
            return Fail(TicketErrors.Invalid("priority", "priority-invalid", "Choose a priority: Low, Normal, High or Urgent."));
        }

        var changed = ticket.ChangePriority(priority, Actor.ForAgent(agent.Id), clock);
        if (changed.IsFailure)
        {
            return Fail(changed.Error!.ToError());
        }

        tickets.Update(ticket);
        return await TicketMutation.CommitAsync(scope, ticket.Id, tickets, cancellationToken);
    }

    private static Result<TicketStateDto> Fail(ResultError error) => Result<TicketStateDto>.Failure(error);
}
