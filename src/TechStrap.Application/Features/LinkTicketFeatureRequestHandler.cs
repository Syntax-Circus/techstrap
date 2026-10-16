using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Application.Tickets;
using TechStrap.Contracts.Features;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Features;

public interface ILinkTicketFeatureRequestHandler
{
    Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, LinkTicketFeatureRequest request, CancellationToken cancellationToken);
}

public sealed class LinkTicketFeatureRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    ITicketRepository tickets,
    IFeatureRepository features,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ILinkTicketFeatureRequestHandler
{
    public async Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, LinkTicketFeatureRequest request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var loaded = await TicketMutation.LoadAsync(ticketId, request.RowVersion, true, currentAgent, agents, tickets, cancellationToken);
        if (loaded.IsFailure)
        {
            return TicketMutation.Fail(loaded.Errors[0]);
        }
        var (agent, ticket) = loaded.Value;
        if (request.FeatureId is { } id)
        {
            var feature = await features.GetAsync(id, cancellationToken);
            if (feature is null)
            {
                return TicketMutation.Fail(new("feature-not-found", "That feature does not exist.", ResultErrorKind.Validation, "featureId"));
            }
            if (feature.ProductId != ticket.ProductId)
            {
                return TicketMutation.Fail(new("feature-product-mismatch", "Choose a feature belonging to this ticket's product.", ResultErrorKind.Conflict));
            }
        }
        var changed = ticket.LinkFeature(request.FeatureId, Actor.ForAgent(agent.Id), clock);
        if (changed.IsFailure)
        {
            return TicketMutation.Fail(changed.Error!.ToError());
        }
        tickets.Update(ticket);
        return await TicketMutation.CommitAsync(scope, ticket.Id, tickets, cancellationToken);
    }
}
