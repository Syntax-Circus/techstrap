using TechStrap.Contracts.AdminEvents;

namespace TechStrap.Admin.Features.Settings.Audit;

internal sealed record AdminEventRowViewModel(Guid Id, DateTimeOffset At, string Actor, string Subject, string Summary)
{
    public static AdminEventRowViewModel From(AdminEventDto admin) => new(
        admin.Id,
        admin.OccurredAt,
        string.IsNullOrWhiteSpace(admin.ActorLabel) ? AuditCopy.UnknownActor : admin.ActorLabel,
        AdminEventSummaryFactory.SubjectLabel(admin.SubjectType),
        AdminEventSummaryFactory.Summarize(admin.Type, admin.Payload));
}
