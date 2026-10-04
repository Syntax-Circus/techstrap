using System.Globalization;
using SyntaxCircus.Common;
using TechStrap.Contracts.AdminEvents;
using TechStrap.Contracts.Paging;

namespace TechStrap.Admin.Clients;

/// <summary>
/// What the audit page filters by. <see cref="SubjectType"/> is one of <c>AdminSubjectTypes</c>; <see cref="AsOf"/> is the first page's timestamp, passed
/// back on every later page so new events do not shift the pages under the reader. A null member is not sent.
/// </summary>
public sealed record AdminEventFilter(string? SubjectType = null, Guid? ActorId = null, DateTimeOffset? AsOf = null);

/// <summary>The admin audit log (Admin only, newest first).</summary>
public interface IAdminEventsClient
{
    /// <summary>
    /// <c>GET /api/admin-events</c>. 400 admin-event-subject-type-invalid (field subjectType) for an unknown subject type. The API has no event-type filter and no date range.
    /// </summary>
    Task<Result<PagedResponse<AdminEventDto>>> ListAsync(AdminEventFilter filter, int page, int pageSize, CancellationToken cancellationToken);
}

internal sealed class AdminEventsClient(ApiConnection connection) : IAdminEventsClient
{
    public Task<Result<PagedResponse<AdminEventDto>>> ListAsync(AdminEventFilter filter, int page, int pageSize, CancellationToken cancellationToken) =>
        connection.GetAsync<PagedResponse<AdminEventDto>>(
            ApiUri.Build(
                "api/admin-events",
                ("subjectType", filter.SubjectType),
                ("actorId", filter.ActorId),
                ("asOf", filter.AsOf?.ToString("O", CultureInfo.InvariantCulture)),
                ("page", page),
                ("pageSize", pageSize)),
            cancellationToken);
}
