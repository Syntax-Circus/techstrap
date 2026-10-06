using SyntaxCircus.Common;
using TechStrap.Contracts.Intake;

namespace TechStrap.Portal.Clients;

/// <summary>
/// A new ticket from the contact form. The text is what the visitor typed (already validated by the page, and validated again by the API, which is the authority); <paramref name="Website"/> is the honeypot field,
/// sent on as it arrived so the API can answer a bot with the same 201 a person gets (D-045 addendum).
/// </summary>
public sealed record NewTicketRequest(string Email, string Name, string Subject, string Body, string? Website, IReadOnlyList<AttachmentUpload> Attachments);

/// <summary>The anonymous intake call of a product's contact form (P09-T02, T06).</summary>
public interface IPublicTicketClient
{
    /// <summary>
    /// Sends the ticket as a multipart POST through the write client (never retried, so a transient failure cannot create two tickets). A key that is not a slug is the uniform not-found error and no call is
    /// made. 400 keeps the API's field codes (<c>email-invalid</c>, <c>attachments-too-many</c> and so on), 413 and 415 are the attachment errors, 429 is rate limited.
    /// </summary>
    Task<Result<SubmitTicketResponse>> SubmitAsync(string productKey, NewTicketRequest request, CancellationToken cancellationToken);
}
