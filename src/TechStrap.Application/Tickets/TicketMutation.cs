using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tickets;

internal static class TicketMutation
{
    /// <summary>A failed ticket-state result for the given error.</summary>
    public static Result<TicketStateDto> Fail(ResultError error) => Result<TicketStateDto>.Failure(error);

    /// <summary>Resolves the active agent and loads the ticket, enforcing the client's RowVersion (D-036).</summary>
    public static async Task<Result<(Agent Agent, Ticket Ticket)>> LoadAsync(
        Guid ticketId,
        uint? rowVersion,
        bool rowVersionRequired,
        ICurrentAgentClaims currentAgent,
        IAgentRepository agents,
        ITicketRepository tickets,
        CancellationToken cancellationToken)
    {
        if (rowVersionRequired && rowVersion is null)
        {
            return Result<(Agent Agent, Ticket Ticket)>.Failure(TicketErrors.RowVersionRequired());
        }

        var agent = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (agent.IsFailure)
        {
            return Result<(Agent Agent, Ticket Ticket)>.Failure(agent.Errors[0]);
        }

        var ticket = await tickets.GetByIdAsync(ticketId, cancellationToken);
        if (ticket is null)
        {
            return Result<(Agent Agent, Ticket Ticket)>.Failure(TicketErrors.NotFound());
        }

        if (rowVersion is { } expected && expected != ticket.Version)
        {
            return Result<(Agent Agent, Ticket Ticket)>.Failure(TicketErrors.Stale());
        }

        return Result<(Agent Agent, Ticket Ticket)>.Success((agent.Value, ticket));
    }

    /// <summary>Commits the scope; commit conflicts come back as the failure.</summary>
    public static async Task<Result> CommitAsync(IUnitOfWorkScope scope, CancellationToken cancellationToken)
    {
        var committed = await scope.CommitAsync(cancellationToken);
        return committed.IsFailure ? Result.Failure(committed.Errors[0]) : Result.Success();
    }

    /// <summary>
    /// Reads the fresh state (new RowVersion). The read deliberately ignores request cancellation: a write that has already
    /// committed should still report its state rather than turn into an aborted request.
    /// </summary>
    public static async Task<Result<TicketStateDto>> ReadStateAsync(Guid ticketId, ITicketRepository tickets, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var state = await tickets.GetStateAsync(ticketId, CancellationToken.None);
        return state is null
            ? Result<TicketStateDto>.Failure(TicketErrors.NotFound())
            : Result<TicketStateDto>.Success(state.ToDto());
    }

    /// <summary>Commits the scope and returns the fresh state (see <see cref="ReadStateAsync"/>).</summary>
    public static async Task<Result<TicketStateDto>> CommitAsync(
        IUnitOfWorkScope scope,
        Guid ticketId,
        ITicketRepository tickets,
        CancellationToken cancellationToken)
    {
        var committed = await CommitAsync(scope, cancellationToken);
        return committed.IsFailure
            ? Result<TicketStateDto>.Failure(committed.Errors[0])
            : await ReadStateAsync(ticketId, tickets, cancellationToken);
    }
}
