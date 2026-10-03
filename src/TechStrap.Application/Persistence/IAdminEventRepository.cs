using SyntaxCircus.Common;
using TechStrap.Domain.Admin;

namespace TechStrap.Application.Persistence;

/// <summary>Append and read the admin audit log. There is no update or delete.</summary>
public interface IAdminEventRepository
{
    void Add(AdminEvent adminEvent);

    /// <summary>
    /// Newest first (then id); filtered by subject type, actor and as-of time when given. Implementations normalize
    /// <paramref name="page"/> and <paramref name="pageSize"/> through <see cref="Paging"/> before querying.
    /// </summary>
    Task<PagedResult<AdminEvent>> ListAsync(AdminEventFilter filter, int page, int pageSize, CancellationToken cancellationToken);
}
