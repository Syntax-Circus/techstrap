using SyntaxCircus.Common;
using TechStrap.Domain.Admin;

namespace TechStrap.Application.Persistence;

/// <summary>Append and read the admin audit log. There is no update or delete.</summary>
public interface IAdminEventRepository
{
    void Add(AdminEvent adminEvent);

    /// <summary>
    /// Newest first, optionally limited to one subject type. Implementations normalize <paramref name="page"/> and
    /// <paramref name="pageSize"/> through <see cref="Paging"/> before querying.
    /// </summary>
    Task<PagedResult<AdminEvent>> ListAsync(AdminSubjectType? subjectType, int page, int pageSize, CancellationToken cancellationToken);
}
