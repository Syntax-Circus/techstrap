using SyntaxCircus.Common;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client.Maui;

/// <summary>
/// Submits a support ticket from a MAUI app with the device and app context attached as metadata.
/// Public API keys are extractable from an app package: metadata sent with a Public key is stored but flagged untrusted on the server.
/// A caller who retries must supply a stable <see cref="MauiTicketDraft.IdempotencyKey"/>, or a retry can create a second ticket.
/// </summary>
public interface IMauiTicketSubmitter
{
    /// <summary>Collects the device context, merges it with the draft's metadata and submits the ticket.</summary>
    /// <param name="draft">The ticket to submit.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The server's answer, or a validation failure (code <see cref="TechStrapMauiErrorCodes.MetadataInvalid"/>) when the metadata is over the limits, in which case nothing is sent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="draft"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The draft's <see cref="MauiTicketDraft.IdempotencyKey"/> is blank, contains non-ASCII characters or is over 200 characters.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<Result<SubmitTicketResponse>> SubmitAsync(MauiTicketDraft draft, CancellationToken cancellationToken = default);
}
