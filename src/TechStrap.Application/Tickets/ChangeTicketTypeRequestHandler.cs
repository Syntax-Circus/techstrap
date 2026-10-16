using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tickets;

public interface IChangeTicketTypeRequestHandler
{
    Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, ChangeTicketTypeRequest request, CancellationToken cancellationToken);
}

/// <summary>Changes the type. The row version is required (D-036); setting the current type is a no-op that still succeeds.</summary>
public sealed class ChangeTicketTypeRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    ITicketRepository tickets,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IChangeTicketTypeRequestHandler
{
    public async Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, ChangeTicketTypeRequest request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        var loaded = await TicketMutation.LoadAsync(ticketId, request.RowVersion, rowVersionRequired: true, currentAgent, agents, tickets, cancellationToken);
        if (loaded.IsFailure)
        {
            return TicketMutation.Fail(loaded.Errors[0]);
        }

        var (agent, ticket) = loaded.Value;

        if (!TicketNameParser.TryType(request.Type, out var type))
        {
            return TicketMutation.Fail(TicketErrors.Invalid("type", "type-invalid", "Choose a type: Support or FeatureRequest."));
        }

        var changed = ticket.ChangeType(type, Actor.ForAgent(agent.Id), clock);
        if (changed.IsFailure)
        {
            return TicketMutation.Fail(changed.Error!.ToError());
        }

        tickets.Update(ticket);
        return await TicketMutation.CommitAsync(scope, ticket.Id, tickets, cancellationToken);
    }
}
