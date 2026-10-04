using Microsoft.EntityFrameworkCore;
using TechStrap.Application.Requesters;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Repositories;

/// <summary>
/// The bulk half of erasing a requester (D-039). Every statement is a set-based update or delete on the context's connection, so it
/// joins the caller's active unit-of-work transaction and rolls back with it. It never commits.
/// </summary>
internal sealed class RequesterErasure(TechStrapDbContext context) : IRequesterErasure
{
    public async Task<RequesterErasureResult> EraseDataAsync(Guid requesterId, string email, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ticketIds = await context.Set<TicketRecord>().AsNoTracking()
            .Where(t => t.RequesterId == requesterId)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        var customerMessages = context.Set<MessageRecord>()
            .Where(m => m.AuthorType == AuthorType.Requester && m.AuthorId == requesterId);

        // Select once, then delete exactly those ids: the deleted set and the returned keys are identical even if an attachment
        // commits in between (READ COMMITTED), so no file is ever orphaned.
        var doomed = await context.Set<AttachmentRecord>().AsNoTracking()
            .Where(a => customerMessages.Select(m => m.Id).Contains(a.MessageId))
            .Select(a => new { a.Id, a.StorageKey })
            .ToListAsync(cancellationToken);
        var attachmentIds = doomed.ConvertAll(a => a.Id);
        var storageKeys = doomed.ConvertAll(a => a.StorageKey);
        var attachments = attachmentIds.Count == 0
            ? 0
            : await context.Set<AttachmentRecord>()
                .Where(a => attachmentIds.Contains(a.Id))
                .ExecuteDeleteAsync(cancellationToken);

        var messages = await customerMessages
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.Body, ErasureMarker.Text), cancellationToken);

        var tickets = await context.Set<TicketRecord>()
            .Where(t => ticketIds.Contains(t.Id))
            .ExecuteUpdateAsync(set => set
                .SetProperty(t => t.Subject, ErasureMarker.Text)
                .SetProperty(t => t.Metadata, (string?)null)
                .SetProperty(t => t.CustomFields, (string?)null), cancellationToken);

        var tokens = await context.Set<TicketAccessTokenRecord>()
            .Where(t => t.RequesterId == requesterId && t.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.RevokedAt, now), cancellationToken);

        // Outbox addresses are stored lower-cased (CountRecentAsync relies on the same).
        var address = email.Trim().ToLowerInvariant();
        var outboxRows = await context.Set<EmailOutboxRecord>()
            .Where(e => e.ToAddress == address || (e.TicketId != null && ticketIds.Contains(e.TicketId.Value)))
            .ExecuteDeleteAsync(cancellationToken);

        return new RequesterErasureResult(tickets, messages, attachments, tokens, outboxRows, storageKeys);
    }
}
