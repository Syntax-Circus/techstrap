using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Application.Security;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tickets.Customer;

public interface IGetCustomerTicketRequestHandler
{
    Task<Result<CustomerTicketDto>> HandleAsync(string? token, CancellationToken cancellationToken);
}

/// <summary>
/// GET /api/customer/ticket. The customer's view of their ticket: public messages only, agents shown by their public name (D-024).
/// Every access failure is the one uniform NotFound (D-038). A successful view records the token use, which slides its expiry.
/// </summary>
public sealed class GetCustomerTicketRequestHandler(
    IAccessTokenService accessTokens,
    ITicketRepository tickets,
    IRequesterRepository requesters,
    IAgentRepository agents,
    IProductRepository products,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IGetCustomerTicketRequestHandler
{
    public async Task<Result<CustomerTicketDto>> HandleAsync(string? token, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        var access = await CustomerAccess.ResolveAsync(token, accessTokens, tickets, requesters, clock, cancellationToken);
        if (access.IsFailure)
        {
            return NotFound();
        }

        var (accessToken, ticket, _) = access.Value;
        var product = await products.GetByIdAsync(ticket.ProductId, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        if (accessToken.RecordUse(clock).IsFailure)
        {
            return NotFound();
        }

        tickets.UpdateAccessToken(accessToken);

        var messages = await tickets.GetMessagesAsync(ticket.Id, publicOnly: true, cancellationToken);
        var attachments = (await tickets.GetAttachmentsAsync(ticket.Id, publicOnly: true, cancellationToken))
            .ToLookup(attachment => attachment.MessageId);
        var agentIds = messages.Where(m => m.AuthorType == AuthorType.Agent && m.AuthorId is not null)
            .Select(m => m.AuthorId!.Value).Distinct().ToList();
        var authors = agentIds.Count == 0
            ? []
            : (await agents.GetByIdsAsync(agentIds, cancellationToken)).ToDictionary(agent => agent.Id);

        var displayName = product.Branding.DisplayName;
        string? AuthorName(Message message)
        {
            if (message.AuthorType != AuthorType.Agent)
            {
                return null;
            }

            return message.AuthorId is { } id && authors.TryGetValue(id, out var agent)
                ? AgentPublicIdentity.Resolve(agent, displayName)
                : AgentPublicIdentity.SupportName(displayName);
        }

        var dtos = messages.Select(message => new CustomerMessageDto(
            message.Id, message.AuthorType.ToWire(), AuthorName(message), message.Body, message.CreatedAt,
            [.. attachments[message.Id].Select(attachment => attachment.ToDto())])).ToList();

        var committed = await TicketMutation.CommitAsync(scope, cancellationToken);
        if (committed.IsFailure && committed.Errors[0].Code != PersistenceErrorCodes.ConcurrencyConflict)
        {
            return NotFound();
        }

        return Result<CustomerTicketDto>.Success(
            new CustomerTicketDto(ticket.Number.ToString(), ticket.Subject, ticket.Status.ToWire(), ticket.CreatedAt, dtos));
    }

    private static Result<CustomerTicketDto> NotFound() => Result<CustomerTicketDto>.Failure(CustomerErrors.NotFound());
}
