using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tickets;

public interface IMarkTicketSpamRequestHandler
{
    Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, MarkTicketSpamRequest request, CancellationToken cancellationToken);
}

/// <summary>Marks or unmarks spam. The row version is required (D-036); repeating the current value is a no-op. Nobody is notified.</summary>
public sealed class MarkTicketSpamRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    ITicketRepository tickets,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IMarkTicketSpamRequestHandler
{
    public async Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, MarkTicketSpamRequest request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        var loaded = await TicketMutation.LoadAsync(ticketId, request.RowVersion, rowVersionRequired: true, currentAgent, agents, tickets, cancellationToken);
        if (loaded.IsFailure)
        {
            return TicketMutation.Fail(loaded.Errors[0]);
        }

        var (agent, ticket) = loaded.Value;

        if (request.IsSpam is not { } isSpam)
        {
            return TicketMutation.Fail(TicketErrors.Invalid("isSpam", "is-spam-required", "Say whether the ticket is spam."));
        }

        var marked = ticket.MarkSpam(isSpam, Actor.ForAgent(agent.Id), clock);
        if (marked.IsFailure)
        {
            return TicketMutation.Fail(marked.Error!.ToError());
        }

        tickets.Update(ticket);
        return await TicketMutation.CommitAsync(scope, ticket.Id, tickets, cancellationToken);
    }
}
