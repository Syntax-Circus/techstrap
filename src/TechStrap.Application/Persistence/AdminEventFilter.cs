using TechStrap.Domain.Admin;

namespace TechStrap.Application.Persistence;

/// <summary>Admin audit filters. <see cref="AsOf"/> hides events recorded later, so paging stays stable while new events arrive.</summary>
public sealed record AdminEventFilter(AdminSubjectType? SubjectType, Guid? ActorId, DateTimeOffset? AsOf);
