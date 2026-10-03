using TechStrap.Application.Attachments;

namespace TechStrap.Application.Intake;

/// <summary>
/// Everything the transport knows about a submission that is not in the request body (D-016, D-034). Built by the controllers:
/// the web form sets ProductKey, the honeypot flag and attachments; the API sets ProductId, ApiKeyId, Trusted and IdempotencyKey
/// from the authenticated key principal and headers.
/// </summary>
public sealed record SubmitTicketContext(
    IntakeChannel Channel,
    string? ProductKey,
    Guid? ProductId,
    Guid? ApiKeyId,
    bool Trusted,
    bool HoneypotTripped,
    IReadOnlyList<IncomingAttachment> Attachments,
    string? IdempotencyKey);
