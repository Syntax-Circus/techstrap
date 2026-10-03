using System.Text.Json;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Auditing;

/// <summary>
/// Stages an AdminEvent in the caller's unit of work (D-006, D-022). Payloads are small anonymous objects serialised as camelCase JSON.
/// AdminEvent.Record rejects secrets and personal data; a rejected payload is a bug in the calling handler, so it throws.
/// </summary>
internal static class AdminAudit
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public static void Record(
        IAdminEventRepository events,
        AdminEventType type,
        Agent actor,
        AdminSubjectType subjectType,
        Guid subjectId,
        object payload,
        TimeProvider clock)
    {
        var recorded = AdminEvent.Record(type, actor.Id, subjectType, subjectId, JsonSerializer.Serialize(payload, _json), clock);
        if (recorded.IsFailure)
        {
            throw new InvalidOperationException($"The {type} audit payload was rejected ({recorded.Error!.Code}); fix the handler's payload.");
        }

        events.Add(recorded.Value);
    }
}
