using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Application.Tags;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tickets;

public interface IAddTicketTagRequestHandler
{
    Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, AddTicketTagRequest request, CancellationToken cancellationToken);
}

/// <summary>Adds a tag. The row version is required (D-036); adding a tag the ticket already has succeeds without an event.</summary>
public sealed class AddTicketTagRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    ITicketRepository tickets,
    ITagRepository tags,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IAddTicketTagRequestHandler
{
    public async Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, AddTicketTagRequest request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        var loaded = await TicketMutation.LoadAsync(ticketId, request.RowVersion, rowVersionRequired: true, currentAgent, agents, tickets, cancellationToken);
        if (loaded.IsFailure)
        {
            return TicketMutation.Fail(loaded.Errors[0]);
        }

        var (agent, ticket) = loaded.Value;

        if (request.TagId is not { } tagId)
        {
            return TicketMutation.Fail(TicketErrors.Invalid("tagId", "tag-required", "Choose a tag to add."));
        }

        if (await tags.GetByIdAsync(tagId, cancellationToken) is null)
        {
            return TicketMutation.Fail(TagErrors.NotFound());
        }

        var added = ticket.AddTag(tagId, Actor.ForAgent(agent.Id), clock);
        if (added.IsFailure)
        {
            return TicketMutation.Fail(added.Error!.ToError());
        }

        tickets.Update(ticket);
        return await TicketMutation.CommitAsync(scope, ticket.Id, tickets, cancellationToken);
    }
}
