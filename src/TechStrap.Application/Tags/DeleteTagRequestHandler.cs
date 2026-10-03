using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Auditing;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tags;

public interface IDeleteTagRequestHandler
{
    Task<Result> HandleAsync(Guid tagId, bool force, CancellationToken cancellationToken);
}

/// <summary>
/// DELETE /api/tags/{id} (Admin, D-030). A tag in use is refused with the ticket count unless <c>force</c> is set; then every ticket
/// carrying it records TagRemoved (reason tag-deleted) and the tag is deleted, all in one transaction.
/// </summary>
public sealed class DeleteTagRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    ITagRepository tags,
    ITicketRepository tickets,
    IAdminEventRepository adminEvents,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IDeleteTagRequestHandler
{
    public async Task<Result> HandleAsync(Guid tagId, bool force, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result.Failure(actor.Errors[0]);
        }

        var tag = await tags.GetByIdAsync(tagId, cancellationToken);
        if (tag is null)
        {
            return Result.Failure(TagErrors.NotFound());
        }

        var carriers = await tickets.ListTicketIdsWithTagAsync(tagId, cancellationToken);
        if (carriers.Count > 0 && !force)
        {
            return Result.Failure(TagErrors.InUse(carriers.Count));
        }

        foreach (var ticketId in carriers)
        {
            var ticket = await tickets.GetByIdAsync(ticketId, cancellationToken)
                ?? throw new InvalidOperationException($"Ticket {ticketId} carries tag {tagId} but could not be loaded.");
            var detached = ticket.DetachDeletedTag(tagId, Actor.ForAgent(actor.Value.Id), clock);
            if (detached.IsFailure)
            {
                return detached.ToResult();
            }

            tickets.Update(ticket);
        }

        tags.Remove(tag);
        AdminAudit.Record(adminEvents, AdminEventType.TagDeleted, actor.Value, AdminSubjectType.Tag, tag.Id,
            new { slug = tag.Slug, detachedTicketCount = carriers.Count }, clock);
        return await scope.CommitAsync(cancellationToken);
    }
}
