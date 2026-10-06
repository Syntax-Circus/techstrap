using SyntaxCircus.Common;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Portal.Clients;

/// <summary>A reply from the ticket page: the text the customer typed and the files they attached.</summary>
public sealed record CustomerReply(string Body, IReadOnlyList<AttachmentUpload> Attachments);

/// <summary>
/// The calls of a ticket's customer (P09-T02, T08 to T10). The ticket calls take the <see cref="TicketToken"/> from the link, which becomes the <c>X-Ticket-Token</c> header of that one request and appears
/// nowhere else (Review Focus 1). Every failure to see a ticket is the same not-found error, whatever the API's reason (an unknown, expired or revoked token).
/// </summary>
public interface ICustomerTicketClient
{
    /// <summary>The ticket and its public messages, through the retrying read client.</summary>
    Task<Result<CustomerTicketDto>> GetAsync(TicketToken token, CancellationToken cancellationToken);

    /// <summary>
    /// A reply as a multipart POST through the write client (never retried). On a Closed ticket the API starts a follow-up and answers with its link (<see cref="CustomerReplyResponse.FollowUpViewUrl"/>). A 409 is
    /// <see cref="ApiErrorCodes.ReplyConflict"/>.
    /// </summary>
    Task<Result<CustomerReplyResponse>> ReplyAsync(TicketToken token, CustomerReply reply, CancellationToken cancellationToken);

    /// <summary>
    /// Asks for a new link to be emailed (the lost-link page). No token and no product: the API route takes neither. The API answers 202 for any well-formed address; a malformed one is a field error with the code
    /// <c>email-invalid</c> and a 429 is rate limited. The call is never retried.
    /// </summary>
    Task<Result> RequestAccessLinkAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Opens one attachment of the ticket for streaming (the pass-through, P09-T11): the token is the header of the request and the id goes in the API path, through the retrying read client, returning as soon as the
    /// response headers have arrived. The caller owns the <see cref="ApiDownload"/> and disposes it. An unknown id, another ticket's id and a bad token are all the API's uniform not-found.
    /// </summary>
    Task<Result<ApiDownload>> OpenAttachmentAsync(TicketToken token, Guid attachmentId, CancellationToken cancellationToken);
}
