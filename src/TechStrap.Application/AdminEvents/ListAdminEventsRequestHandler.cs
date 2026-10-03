using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.AdminEvents;
using TechStrap.Contracts.Paging;
using TechStrap.Domain.Admin;

namespace TechStrap.Application.AdminEvents;

public interface IListAdminEventsRequestHandler
{
    Task<Result<PagedResponse<AdminEventDto>>> HandleAsync(string? subjectType, Guid? actorId, DateTimeOffset? asOf, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>GET /api/admin-events (Admin, D-006, D-022): newest first, filterable by subject type and actor, stable with asOf.</summary>
public sealed class ListAdminEventsRequestHandler(IAdminEventRepository adminEvents, IAgentRepository agents) : IListAdminEventsRequestHandler
{
    public async Task<Result<PagedResponse<AdminEventDto>>> HandleAsync(
        string? subjectType,
        Guid? actorId,
        DateTimeOffset? asOf,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        AdminSubjectType? subject = null;
        if (!string.IsNullOrWhiteSpace(subjectType))
        {
            if (!Enum.TryParse<AdminSubjectType>(subjectType.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return Result<PagedResponse<AdminEventDto>>.Failure(new ResultError(
                    "admin-event-subject-type-invalid",
                    $"Use one of: {string.Join(", ", Enum.GetNames<AdminSubjectType>())}.",
                    ResultErrorKind.Validation,
                    "subjectType"));
            }

            subject = parsed;
        }

        var found = await adminEvents.ListAsync(new AdminEventFilter(subject, actorId, asOf?.ToUniversalTime()), page, pageSize, cancellationToken);
        var actorIds = found.Items.Select(e => e.ActorId).Distinct().ToList();
        var labels = (await agents.GetByIdsAsync(actorIds, cancellationToken)).ToDictionary(a => a.Id, a => a.Name ?? a.Email);

        return Result<PagedResponse<AdminEventDto>>.Success(new PagedResponse<AdminEventDto>(
            [.. found.Items.Select(e => new AdminEventDto(
                e.Id, e.Type.ToString(), e.ActorId, labels.GetValueOrDefault(e.ActorId), e.SubjectType.ToString(), e.SubjectId, e.PayloadJson, e.OccurredAt))],
            found.Page,
            found.PageSize,
            found.TotalCount));
    }
}
