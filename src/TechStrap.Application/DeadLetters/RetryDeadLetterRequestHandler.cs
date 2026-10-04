using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Auditing;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Domain.Admin;

namespace TechStrap.Application.DeadLetters;

public interface IRetryDeadLetterRequestHandler
{
    Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// POST /api/dead-letters/{id}/retry (Admin, D-006, D-022, D-039): puts a dead-lettered email back in the queue with a fresh attempt
/// count. Only a dead-lettered row qualifies. The audit event holds the kind and the attempt count only.
/// </summary>
public sealed class RetryDeadLetterRequestHandler(
    IEmailOutboxStore store,
    IAdminEventRepository adminEvents,
    IAgentRepository agents,
    ICurrentAgentClaims currentAgent,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRetryDeadLetterRequestHandler
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result.Failure(actor.Errors[0]);
        }

        var item = await store.GetAsync(id, cancellationToken);
        if (item is null)
        {
            return Result.Failure(DeadLetterErrors.NotFound());
        }

        var attemptsBefore = item.Attempts; // Retry resets the count to 0
        var retried = item.Retry(clock);
        if (retried.IsFailure)
        {
            return retried.ToResult();
        }

        store.Update(item);
        AdminAudit.Record(adminEvents, AdminEventType.DeadLetterRetried, actor.Value, AdminSubjectType.EmailOutbox, item.Id,
            new { kind = item.Kind, attempts = attemptsBefore }, clock);
        return await scope.CommitAsync(cancellationToken);
    }
}
