using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Products;
using TechStrap.Application.Results;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tickets;

public interface IMoveTicketProductRequestHandler
{
    Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, MoveTicketProductRequest request, CancellationToken cancellationToken);
}

/// <summary>Moves the ticket to another active product. The ticket number never changes (D-009); the row version is required (D-036).</summary>
public sealed class MoveTicketProductRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    ITicketRepository tickets,
    IProductRepository products,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IMoveTicketProductRequestHandler
{
    public async Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, MoveTicketProductRequest request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        var loaded = await TicketMutation.LoadAsync(ticketId, request.RowVersion, rowVersionRequired: true, currentAgent, agents, tickets, cancellationToken);
        if (loaded.IsFailure)
        {
            return Fail(loaded.Errors[0]);
        }

        var (agent, ticket) = loaded.Value;

        if (request.ProductId is not { } productId)
        {
            return Fail(TicketErrors.Invalid("productId", "product-required", "Choose the product to move this ticket to."));
        }

        var product = await products.GetByIdAsync(productId, cancellationToken);
        if (product is null)
        {
            return Fail(ProductErrors.NotFound());
        }

        if (!product.IsActive)
        {
            return Fail(TicketErrors.Invalid("productId", "product-inactive", "That product is inactive. Choose an active product."));
        }

        var moved = ticket.MoveToProduct(product.Id, Actor.ForAgent(agent.Id), clock);
        if (moved.IsFailure)
        {
            return Fail(moved.Error!.ToError());
        }

        tickets.Update(ticket);
        return await TicketMutation.CommitAsync(scope, ticket.Id, tickets, cancellationToken);
    }

    private static Result<TicketStateDto> Fail(ResultError error) => Result<TicketStateDto>.Failure(error);
}
