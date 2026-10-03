using Microsoft.EntityFrameworkCore;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Admin;
using TechStrap.Infrastructure.Persistence.Mapping;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Repositories;

internal sealed class AdminEventRepository(TechStrapDbContext context) : IAdminEventRepository
{
    public void Add(AdminEvent adminEvent) => context.Set<AdminEventRecord>().Add(adminEvent.ToRecord());

    public async Task<PagedResult<AdminEvent>> ListAsync(AdminSubjectType? subjectType, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Paging.NormalizePage(page);
        pageSize = Paging.NormalizePageSize(pageSize);

        var query = context.Set<AdminEventRecord>().AsNoTracking();
        if (subjectType is { } type)
        {
            query = query.Where(e => e.SubjectType == type);
        }

        var total = await query.CountAsync(cancellationToken);
        var records = await query.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
            .Skip(Paging.Offset(page, pageSize)).Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<AdminEvent>([.. records.Select(e => e.ToDomain())], page, pageSize, total);
    }
}
