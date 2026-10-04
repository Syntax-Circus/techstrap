using Microsoft.Extensions.Logging;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Attachments;
using TechStrap.Application.Persistence;

namespace TechStrap.Application.Tickets;

public interface IGetAttachmentRequestHandler
{
    Task<Result<AttachmentContent>> HandleAsync(Guid attachmentId, CancellationToken cancellationToken);
}

/// <summary>
/// GET /api/attachments/{id}. Agents may read any ticket's attachment, internal notes included (the customer path is 06b, D-036).
/// The caller disposes the returned stream.
/// </summary>
public sealed class GetAttachmentRequestHandler(
    ICurrentAgentClaims currentAgent,
    ITicketRepository tickets,
    IAttachmentStore store,
    ILogger<GetAttachmentRequestHandler> logger) : IGetAttachmentRequestHandler
{
    public async Task<Result<AttachmentContent>> HandleAsync(Guid attachmentId, CancellationToken cancellationToken)
    {
        if (currentAgent.Current is null)
        {
            return Result<AttachmentContent>.Failure(AgentErrors.AccessRequired());
        }

        var attachment = await tickets.GetAttachmentByIdAsync(attachmentId, cancellationToken);
        if (attachment is null)
        {
            return Result<AttachmentContent>.Failure(TicketErrors.AttachmentNotFound());
        }

        var stream = await store.OpenReadAsync(attachment.StorageKey, cancellationToken);
        if (stream is null)
        {
            logger.LogWarning("Attachment {AttachmentId} has no stored file.", attachment.Id);
            return Result<AttachmentContent>.Failure(TicketErrors.AttachmentNotFound());
        }

        return Result<AttachmentContent>.Success(new AttachmentContent(stream, attachment.FileName, attachment.ContentType, attachment.Size));
    }
}
