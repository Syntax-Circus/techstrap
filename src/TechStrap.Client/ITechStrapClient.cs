using SyntaxCircus.Common;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client;

/// <summary>
/// Submits support tickets to a TechStrap API. Every failure the API or the network can cause is a <see cref="Result"/> error with a stable code from <see cref="TechStrapClientErrorCodes"/>; the
/// only exceptions are caller mistakes (a null request, a bad idempotency key), invalid options (on first use) and a cancellation requested by the caller.
/// </summary>
public interface ITechStrapClient
{
    /// <summary>
    /// Submits a ticket with a key the SDK generates for this call, and retries a transient failure (timeouts, 408, 502, 503, 504, a dropped connection), making up to <c>MaxAttempts</c> attempts in total and sending the same
    /// <c>Idempotency-Key</c> on every attempt so the API creates the ticket once. The generated key is not returned: a caller that may retry a failed submit itself must use the overload that takes its own stable key,
    /// because a second call to this overload uses a new key and can create a duplicate ticket.
    /// </summary>
    /// <param name="request">The ticket.</param>
    /// <param name="cancellationToken">Cancels the call; the cancellation propagates as <see cref="OperationCanceledException"/>.</param>
    Task<Result<SubmitTicketResponse>> SubmitTicketAsync(SubmitTicketRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits a ticket under the caller's key, with the same retries as the overload without a key. Keep the key stable for one logical submission and reuse it when you retry after a failure: the same key never creates a
    /// second ticket. An <see cref="TechStrapClientErrorCodes.ApiUnavailable"/> result means the ticket may or may not have been created, so retry with the same key. A <see cref="TechStrapClientErrorCodes.RateLimited"/>
    /// result is also retried later with the same key.
    /// </summary>
    /// <param name="request">The ticket.</param>
    /// <param name="idempotencyKey">A key that identifies this submission. Visible ASCII, at most <see cref="IntakeLimits.MaxIdempotencyKeyLength"/> characters, not blank; anything else throws <see cref="ArgumentException"/>.</param>
    /// <param name="cancellationToken">Cancels the call; the cancellation propagates as <see cref="OperationCanceledException"/>.</param>
    Task<Result<SubmitTicketResponse>> SubmitTicketAsync(SubmitTicketRequest request, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Submits a ticket exactly once: no idempotency key, no retry. A failure after the request was sent may or may not have created the ticket.</summary>
    Task<Result<SubmitTicketResponse>> SubmitTicketOnceAsync(SubmitTicketRequest request, CancellationToken cancellationToken = default);
}
