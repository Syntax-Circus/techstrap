using Microsoft.Extensions.Logging;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Attachments;
using TechStrap.Application.Auditing;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Admin;

namespace TechStrap.Application.Requesters;

public interface IEraseRequesterRequestHandler
{
    Task<Result> HandleAsync(Guid requesterId, CancellationToken cancellationToken);
}

/// <summary>
/// POST /api/requesters/{id}/erase (Admin, D-006, D-022, D-039). Anonymizes the requester and their personal data (customer message
/// bodies, ticket subjects and fields, customer attachments, access links, outbox rows) and keeps ticket numbers and events. Safe to repeat.
/// The audit event holds counts only. Customer files are deleted after the commit.
/// </summary>
public sealed class EraseRequesterRequestHandler(
    IRequesterRepository requesters,
    IRequesterErasure erasure,
    IAttachmentStore attachments,
    IAdminEventRepository adminEvents,
    IAgentRepository agents,
    ICurrentAgentClaims currentAgent,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<EraseRequesterRequestHandler> logger) : IEraseRequesterRequestHandler
{
    public async Task<Result> HandleAsync(Guid requesterId, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result.Failure(actor.Errors[0]);
        }

        var requester = await requesters.GetByIdAsync(requesterId, cancellationToken);
        if (requester is null)
        {
            return Result.Failure(RequesterErrors.NotFound());
        }

        // Capture the address first: after Erase it is the tombstone and the old address could no longer be matched.
        var email = requester.Email;
        var erased = await erasure.EraseDataAsync(requester.Id, email, clock.GetUtcNow(), cancellationToken);

        requester.Erase(clock);
        requesters.Update(requester);
        AdminAudit.Record(adminEvents, AdminEventType.RequesterErased, actor.Value, AdminSubjectType.Requester, requester.Id,
            new { tickets = erased.Tickets, messages = erased.Messages, attachments = erased.Attachments, links = erased.Tokens, outboxRows = erased.OutboxRows },
            clock);

        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            return committed;
        }

        await StoredFileCleanup.DeleteAllAsync(attachments, erased.StorageKeys, logger);
        return Result.Success();
    }
}
