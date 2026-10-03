using TechStrap.Contracts.Agents;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Agents;

internal static class AgentMapping
{
    public static string RoleName(AgentRole role) => role == AgentRole.Admin ? AgentRoles.Admin : AgentRoles.Agent;

    public static AgentDto ToDto(Agent agent) =>
        new(agent.Id, agent.Name, agent.Email, RoleName(agent.Role), agent.IsActive, agent.PublicDisplayName, agent.LastSeenAt);

    /// <summary>Agents get only what assignment needs; Admins also get email, role, status and last seen (D-022).</summary>
    public static AgentListItemDto ToListItem(Agent agent, bool includeAdminFields) =>
        includeAdminFields
            ? new(agent.Id, agent.Name, agent.Name ?? agent.Email, agent.Email, RoleName(agent.Role), agent.IsActive, agent.LastSeenAt)
            : new(agent.Id, agent.Name, agent.Name ?? agent.Email, null, null, null, null);
}
