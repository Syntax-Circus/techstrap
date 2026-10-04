using Microsoft.Extensions.Logging;
using SyntaxCircus.Common;
using TechStrap.Application.Attachments;
using TechStrap.Application.Persistence;
using TechStrap.Application.Security;

namespace TechStrap.Application.Tickets.Customer;

public interface IGetCustomerAttachmentRequestHandler
{
    Task<Result<AttachmentContent>> HandleAsync(string? token, Guid attachmentId, CancellationToken cancellationToken);
}

/// <summary>
/// GET /api/customer/attachments/{id}. A read: no unit of work and no token slide. Only attachments on public messages of the
/// token's own ticket resolve; another ticket's attachment, an internal note's and a missing file are all the uniform NotFound (D-038).
/// The caller disposes the returned stream.
/// </summary>
public sealed class GetCustomerAttachmentRequestHandler(
    IAccessTokenService accessTokens,
    ITicketRepository tickets,
    IRequesterRepository requesters,
    IAttachmentStore store,
    TimeProvider clock,
    ILogger<GetCustomerAttachmentRequestHandler> logger) : IGetCustomerAttachmentRequestHandler
{
    public async Task<Result<AttachmentContent>> HandleAsync(string? token, Guid attachmentId, CancellationToken cancellationToken)
    {
        var access = await CustomerAccess.ResolveAsync(token, accessTokens, tickets, requesters, clock, cancellationToken);
        if (access.IsFailure)
        {
            return NotFound();
        }

        var attachment = await tickets.GetAttachmentAsync(access.Value.Ticket.Id, attachmentId, publicOnly: true, cancellationToken);
        if (attachment is null)
        {
            return NotFound();
        }

        var stream = await store.OpenReadAsync(attachment.StorageKey, cancellationToken);
        if (stream is null)
        {
            logger.LogWarning("Attachment {AttachmentId} has no stored file.", attachment.Id);
            return NotFound();
        }

        return Result<AttachmentContent>.Success(new AttachmentContent(stream, attachment.FileName, attachment.ContentType, attachment.Size));
    }

    private static Result<AttachmentContent> NotFound() => Result<AttachmentContent>.Failure(CustomerErrors.NotFound());
}
