using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tickets.AutoClose;

public sealed record AutoCloseResult(int Examined, int Closed, int Conflicts);

public interface IAutoCloseSolvedTicketsHandler
{
    Task<Result<AutoCloseResult>> HandleAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Closes tickets that have been Solved for longer than <see cref="AutoCloseOptions.Days"/> (D-008). Each ticket is reloaded and closed in its
/// own unit of work as the system actor, so a customer reply that races one close conflicts that ticket alone; it is re-evaluated on the next
/// run. No email is sent (D-037). Logs counts only.
/// </summary>
public sealed class AutoCloseSolvedTicketsHandler(
    ITicketRepository tickets,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    IOptions<AutoCloseOptions> options,
    ILogger<AutoCloseSolvedTicketsHandler> logger) : IAutoCloseSolvedTicketsHandler
{
    public async Task<Result<AutoCloseResult>> HandleAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var cutoff = clock.GetUtcNow() - TimeSpan.FromDays(settings.Days);

        // The read returns tracked tickets. An uncommitted scope rolls back and clears the tracker when it is disposed, so only the ids
        // leave this block and every ticket is loaded fresh (current concurrency token) inside its own unit of work below.
        IReadOnlyList<Guid> ids;
        await using (await unitOfWork.BeginAsync(cancellationToken))
        {
            ids = [.. (await tickets.ListSolvedBeforeAsync(cutoff, settings.BatchSize, cancellationToken)).Select(t => t.Id)];
        }

        int closed = 0, conflicts = 0;
        foreach (var id in ids)
        {
            switch (await CloseOneAsync(id, cutoff, cancellationToken))
            {
                case Outcome.Closed:
                    closed++;
                    break;
                case Outcome.Conflict:
                    conflicts++;
                    break;
            }
        }

        logger.LogInformation("Auto-close examined {Examined} tickets: {Closed} closed, {Conflicts} conflicts.", ids.Count, closed, conflicts);
        return Result<AutoCloseResult>.Success(new AutoCloseResult(ids.Count, closed, conflicts));
    }

    private async Task<Outcome> CloseOneAsync(Guid id, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var ticket = await tickets.GetByIdAsync(id, cancellationToken);
        if (ticket is not { Status: TicketStatus.Solved, IsSpam: false } || ticket.SolvedAt is not { } solvedAt || solvedAt >= cutoff)
        {
            return Outcome.Skipped;
        }

        if (ticket.ChangeStatus(TicketStatus.Closed, Actor.System, clock).IsFailure)
        {
            return Outcome.Skipped;
        }

        tickets.Update(ticket);
        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsSuccess)
        {
            return Outcome.Closed;
        }

        if (committed.Errors[0].Code == PersistenceErrorCodes.ConcurrencyConflict)
        {
            logger.LogInformation("Auto-close of a ticket conflicted with another change; it is re-evaluated on the next run.");
            return Outcome.Conflict;
        }

        // Anything else is not a race to retry quietly: surface it to the worker loop, which logs it and waits.
        throw new InvalidOperationException($"Auto-close commit failed: {committed.Errors[0].Code}.");
    }

    private enum Outcome
    {
        Skipped,
        Closed,
        Conflict,
    }
}
