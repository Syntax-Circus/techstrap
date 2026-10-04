using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Auditing;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Domain.Admin;

namespace TechStrap.Application.DeadLetters;

public interface IDiscardDeadLetterRequestHandler
{
    Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// DELETE /api/dead-letters/{id} (Admin, D-006, D-022, D-039): discards a dead-lettered email so it is never sent. Only a
/// dead-lettered row qualifies. The audit event holds the kind and the attempt count only.
/// </summary>
public sealed class DiscardDeadLetterRequestHandler(
    IEmailOutboxStore store,
    IAdminEventRepository adminEvents,
    IAgentRepository agents,
    ICurrentAgentClaims currentAgent,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IDiscardDeadLetterRequestHandler
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

        var discarded = item.Discard();
        if (discarded.IsFailure)
        {
            return discarded.ToResult();
        }

        store.Update(item);
        AdminAudit.Record(adminEvents, AdminEventType.DeadLetterDiscarded, actor.Value, AdminSubjectType.EmailOutbox, item.Id,
            new { kind = item.Kind, attempts = item.Attempts }, clock);
        return await scope.CommitAsync(cancellationToken);
    }
}
