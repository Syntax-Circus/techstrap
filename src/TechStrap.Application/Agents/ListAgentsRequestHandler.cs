using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Paging;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Agents;

public interface IListAgentsRequestHandler
{
    Task<Result<PagedResponse<AgentListItemDto>>> HandleAsync(int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>GET /api/agents. Agents get active agents for assignment; Admins get everyone with role and status (D-022).</summary>
public sealed class ListAgentsRequestHandler(ICurrentAgentClaims currentAgent, IAgentRepository agents) : IListAgentsRequestHandler
{
    public async Task<Result<PagedResponse<AgentListItemDto>>> HandleAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        if (currentAgent.Current is not { } claims)
        {
            return Result<PagedResponse<AgentListItemDto>>.Failure(AgentErrors.AccessRequired());
        }

        var isAdmin = claims.Role == AgentRole.Admin;
        var found = await agents.ListAsync(activeOnly: !isAdmin, page, pageSize, cancellationToken);
        return Result<PagedResponse<AgentListItemDto>>.Success(new PagedResponse<AgentListItemDto>(
            [.. found.Items.Select(agent => AgentMapping.ToListItem(agent, includeAdminFields: isAdmin))],
            found.Page,
            found.PageSize,
            found.TotalCount));
    }
}
