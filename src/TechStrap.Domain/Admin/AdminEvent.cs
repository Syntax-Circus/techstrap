using System.Text.Json;
using TechStrap.Domain.Rules;

namespace TechStrap.Domain.Admin;

public enum AdminEventType
{
    ProductCreated,
    ProductUpdated,
    ApiKeyCreated,
    ApiKeyRevoked,
    AgentUpdated,
    TagCreated,
    TagUpdated,
    TagDeleted,
    RequesterErased,
    TicketDeleted,
    DeadLetterRetried,
    DeadLetterDiscarded,
    SiteSettingsUpdated,
}

public enum AdminSubjectType
{
    Product,
    ApiKey,
    Agent,
    Tag,
    Requester,
    Ticket,
    EmailOutbox,
    SiteSettings,
}

/// <summary>
/// An audit entry for administrative actions (D-006, D-022). Separate from <c>TicketEvent</c>: different audience and retention.
/// The payload may hold ids and enum names but never erased values or secrets, so property names that suggest personal data or
/// secrets are refused.
/// </summary>
public sealed class AdminEvent
{
    /// <summary>Case-insensitive substrings that mark a property name as personal data or a secret.</summary>
    private static readonly string[] ForbiddenNameParts =
    [
        "token", "secret", "password", "passwd", "pwd", "hash", "plaintext", "authorization", "credential", "bearer", "apikey", "privatekey", "email", "ipaddress",
    ];

    /// <summary>Normalised names that end with one of these are always allowed: identifiers and algorithm names, and the documented key shapes.</summary>
    private static readonly string[] AllowedNameSuffixes = ["id", "algorithm", "productkey", "keyprefix"];

    /// <summary>Exact (case-insensitive) property names that are never allowed, whole words the substring list would be too broad for.</summary>
    private static readonly HashSet<string> ForbiddenExactNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "email", "name", "body", "subject", "token", "secret", "key", "apikey", "hash", "password", "address",
    };

    private AdminEvent(Guid id, AdminEventType type, Guid actorId, AdminSubjectType subjectType, Guid subjectId, string payloadJson, DateTimeOffset occurredAt)
    {
        Id = id;
        Type = type;
        ActorId = actorId;
        SubjectType = subjectType;
        SubjectId = subjectId;
        PayloadJson = payloadJson;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; }

    public AdminEventType Type { get; }

    public Guid ActorId { get; }

    public AdminSubjectType SubjectType { get; }

    public Guid SubjectId { get; }

    public string PayloadJson { get; }

    public DateTimeOffset OccurredAt { get; }

    public static DomainResult<AdminEvent> Record(AdminEventType type, Guid actorId, AdminSubjectType subjectType, Guid subjectId, string? payloadJson, TimeProvider clock)
    {
        if (actorId == Guid.Empty)
        {
            return DomainErrors.Validation("actor-id-required", "An admin event needs the actor that performed it.", "actorId");
        }

        if (subjectId == Guid.Empty)
        {
            return DomainErrors.Validation("subject-id-required", "An admin event needs the subject it concerns.", "subjectId");
        }

        var payload = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson;
        if (!IsSafePayload(payload))
        {
            return DomainErrors.Validation("admin-event-payload-invalid", "An admin event payload is a JSON object of ids and enum names with no personal data or secrets.", "payload");
        }

        return DomainResult<AdminEvent>.Ok(new AdminEvent(EntityId.New(clock), type, actorId, subjectType, subjectId, payload, DomainTime.Now(clock)));
    }

    public static AdminEvent Restore(Guid id, AdminEventType type, Guid actorId, AdminSubjectType subjectType, Guid subjectId, string payloadJson, DateTimeOffset occurredAt) =>
        new(id, type, actorId, subjectType, subjectId, payloadJson, occurredAt);

    private static bool IsSafePayload(string payload)
    {
        if (payload.Length > DomainLimits.MetadataMaxLength)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object && !HasForbiddenName(document.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Names are normalised (lower-case, no underscores or hyphens) so <c>api_key</c> and <c>access-key</c> match <c>apikey</c>.</summary>
    private static bool IsForbiddenName(string rawName)
    {
        var name = Normalise(rawName);
        if (AllowedNameSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal)))
        {
            return false;
        }

        return ForbiddenExactNames.Contains(name)
            || name.EndsWith("key", StringComparison.Ordinal)
            || ForbiddenNameParts.Any(part => name.Contains(part, StringComparison.Ordinal));
    }

    private static string Normalise(string name) =>
        name.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    private static bool HasForbiddenName(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().Any(p => IsForbiddenName(p.Name) || HasForbiddenName(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().Any(HasForbiddenName),
        _ => false,
    };
}
