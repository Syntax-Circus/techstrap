using TechStrap.Domain.Admin;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Mapping;

internal static class AdminEventMappings
{
    public static AdminEvent ToDomain(this AdminEventRecord record) =>
        AdminEvent.Restore(record.Id, record.Type, record.ActorId, record.SubjectType, record.SubjectId, record.Payload, record.OccurredAt);

    public static AdminEventRecord ToRecord(this AdminEvent adminEvent) => new()
    {
        Id = adminEvent.Id,
        Type = adminEvent.Type,
        ActorId = adminEvent.ActorId,
        SubjectType = adminEvent.SubjectType,
        SubjectId = adminEvent.SubjectId,
        Payload = adminEvent.PayloadJson,
        OccurredAt = adminEvent.OccurredAt,
    };
}
