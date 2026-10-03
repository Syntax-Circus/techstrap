using Microsoft.EntityFrameworkCore;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Agents;
using TechStrap.Infrastructure.Persistence.Mapping;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Repositories;

internal sealed class AgentRepository(TechStrapDbContext context) : IAgentRepository
{
    public async Task<Agent?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        (await context.Set<AgentRecord>().FirstOrDefaultAsync(a => a.Id == id, cancellationToken))?.ToDomain();

    public async Task<Agent?> GetBySubjectAsync(string oidcSubject, CancellationToken cancellationToken) =>
        (await context.Set<AgentRecord>().FirstOrDefaultAsync(a => a.OidcSubject == oidcSubject, cancellationToken))?.ToDomain();

    public async Task<PagedResult<Agent>> ListAsync(bool activeOnly, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Paging.NormalizePage(page);
        pageSize = Paging.NormalizePageSize(pageSize);

        var query = context.Set<AgentRecord>().AsNoTracking();
        if (activeOnly)
        {
            query = query.Where(a => a.IsActive);
        }

        var total = await query.CountAsync(cancellationToken);
        var records = await query.OrderBy(a => a.Name).ThenBy(a => a.Email).ThenBy(a => a.Id)
            .Skip(Paging.Offset(page, pageSize)).Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<Agent>([.. records.Select(a => a.ToDomain())], page, pageSize, total);
    }

    public Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken) =>
        context.Set<AgentRecord>().CountAsync(a => a.IsActive && a.Role == AgentRole.Admin, cancellationToken);

    public void Add(Agent agent) => context.Set<AgentRecord>().Add(agent.ToRecord());

    public void Update(Agent agent) => agent.CopyTo(context.FindLoaded<AgentRecord>(agent.Id));

    public async Task<IReadOnlyList<AgentNotificationPreference>> ListNotificationPreferencesAsync(Guid agentId, CancellationToken cancellationToken)
    {
        var records = await context.Set<AgentNotificationPreferenceRecord>().AsNoTracking()
            .Where(p => p.AgentId == agentId)
            .OrderBy(p => p.ProductId)
            .ToListAsync(cancellationToken);
        return [.. records.Select(p => p.ToDomain())];
    }

    public async Task SetNotificationPreferenceAsync(AgentNotificationPreference preference, CancellationToken cancellationToken)
    {
        var existing = await context.Set<AgentNotificationPreferenceRecord>()
            .FirstOrDefaultAsync(p => p.AgentId == preference.AgentId && p.ProductId == preference.ProductId, cancellationToken);
        if (existing is null)
        {
            context.Set<AgentNotificationPreferenceRecord>().Add(new AgentNotificationPreferenceRecord
            {
                AgentId = preference.AgentId,
                ProductId = preference.ProductId,
                NotifyNewTicket = preference.NotifyNewTicket,
            });
        }
        else
        {
            existing.NotifyNewTicket = preference.NotifyNewTicket;
        }
    }

    public async Task<IReadOnlyList<Agent>> ListAgentsToAlertForProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        var records = await context.Set<AgentNotificationPreferenceRecord>().AsNoTracking()
            .Where(p => p.ProductId == productId && p.NotifyNewTicket)
            .Join(context.Set<AgentRecord>().Where(a => a.IsActive), p => p.AgentId, a => a.Id, (_, agent) => agent)
            .OrderBy(a => a.Email)
            .ToListAsync(cancellationToken);
        return [.. records.Select(a => a.ToDomain())];
    }
}
