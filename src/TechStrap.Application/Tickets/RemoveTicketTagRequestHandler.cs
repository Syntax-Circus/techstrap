using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Application.Tags;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tickets;

public interface IRemoveTicketTagRequestHandler
{
    Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, Guid tagId, uint? rowVersion, CancellationToken cancellationToken);
}

/// <summary>Removes a tag. The row version is required (D-036); removing a tag the ticket does not have succeeds without an event.</summary>
public sealed class RemoveTicketTagRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    ITicketRepository tickets,
    ITagRepository tags,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRemoveTicketTagRequestHandler
{
    public async Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, Guid tagId, uint? rowVersion, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        var loaded = await TicketMutation.LoadAsync(ticketId, rowVersion, rowVersionRequired: true, currentAgent, agents, tickets, cancellationToken);
        if (loaded.IsFailure)
        {
            return TicketMutation.Fail(loaded.Errors[0]);
        }

        var (agent, ticket) = loaded.Value;

        if (await tags.GetByIdAsync(tagId, cancellationToken) is null)
        {
            return TicketMutation.Fail(TagErrors.NotFound());
        }

        var removed = ticket.RemoveTag(tagId, Actor.ForAgent(agent.Id), clock);
        if (removed.IsFailure)
        {
            return TicketMutation.Fail(removed.Error!.ToError());
        }

        tickets.Update(ticket);
        return await TicketMutation.CommitAsync(scope, ticket.Id, tickets, cancellationToken);
    }
}
