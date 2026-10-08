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
    /// Submits a ticket and retries a transient failure (timeouts, 408, 502, 503, 504, a dropped connection), making up to <c>MaxAttempts</c> attempts in total and sending the same <c>Idempotency-Key</c> on every attempt so the
    /// API creates the ticket once.
    /// </summary>
    /// <param name="request">The ticket.</param>
    /// <param name="idempotencyKey">A key that identifies this submission; the same key never creates a second ticket. Visible ASCII, at most <see cref="IntakeLimits.MaxIdempotencyKeyLength"/> characters. When null, a new key is generated for the call.</param>
    /// <param name="ct">Cancels the call; the cancellation propagates as <see cref="OperationCanceledException"/>.</param>
    Task<Result<SubmitTicketResponse>> SubmitTicketAsync(SubmitTicketRequest request, string? idempotencyKey = null, CancellationToken ct = default);

    /// <summary>Submits a ticket exactly once: no idempotency key, no retry. A failure after the request was sent may or may not have created the ticket.</summary>
    Task<Result<SubmitTicketResponse>> SubmitTicketOnceAsync(SubmitTicketRequest request, CancellationToken ct = default);
}
