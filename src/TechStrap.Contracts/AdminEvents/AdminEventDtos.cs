namespace TechStrap.Contracts.AdminEvents;

/// <summary>
/// One admin audit entry. <paramref name="Payload"/> is the raw JSON object recorded with the change; it never holds secrets
/// or personal data (D-006). <paramref name="ActorLabel"/> is the acting agent's name, or email when the name is unknown,
/// or null when the agent no longer exists.
/// </summary>
public sealed record AdminEventDto(
    Guid Id,
    string Type,
    Guid ActorId,
    string? ActorLabel,
    string SubjectType,
    Guid SubjectId,
    string Payload,
    DateTimeOffset OccurredAt);
