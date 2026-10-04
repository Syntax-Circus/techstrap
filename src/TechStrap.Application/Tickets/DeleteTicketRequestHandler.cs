using Microsoft.Extensions.Logging;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Attachments;
using TechStrap.Application.Auditing;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Admin;

namespace TechStrap.Application.Tickets;

public interface IDeleteTicketRequestHandler
{
    Task<Result> HandleAsync(Guid ticketId, CancellationToken cancellationToken);
}

/// <summary>
/// DELETE /api/tickets/{id} (Admin, D-006, D-022, D-039). Removes the ticket (the database cascades its messages, attachments, events,
/// tokens, tags, articles and idempotency keys), its outbox rows of any status and, after the commit, its stored files. Follow-ups
/// survive, unlinked. The audit event holds the ticket number and counts only.
/// </summary>
public sealed class DeleteTicketRequestHandler(
    ITicketRepository tickets,
    IAttachmentStore attachments,
    IEmailOutboxStore outbox,
    IAdminEventRepository adminEvents,
    IAgentRepository agents,
    ICurrentAgentClaims currentAgent,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<DeleteTicketRequestHandler> logger) : IDeleteTicketRequestHandler
{
    public async Task<Result> HandleAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result.Failure(actor.Errors[0]);
        }

        var ticket = await tickets.GetByIdAsync(ticketId, cancellationToken);
        if (ticket is null)
        {
            return Result.Failure(TicketErrors.NotFound());
        }

        // Collect the keys before the commit (internal-note files too); the files go only after it succeeded.
        var files = await tickets.GetAttachmentsAsync(ticketId, publicOnly: false, cancellationToken);
        var storageKeys = files.Select(file => file.StorageKey).ToList();
        var messageCount = (await tickets.GetMessagesAsync(ticketId, publicOnly: false, cancellationToken)).Count;

        tickets.Remove(ticket);
        await outbox.DeleteForTicketAsync(ticketId, cancellationToken);
        AdminAudit.Record(adminEvents, AdminEventType.TicketDeleted, actor.Value, AdminSubjectType.Ticket, ticket.Id,
            new { number = ticket.Number.ToString(), messageCount, attachmentCount = storageKeys.Count }, clock);

        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            return committed;
        }

        await StoredFileCleanup.DeleteAllAsync(attachments, storageKeys, logger);
        return Result.Success();
    }
}
