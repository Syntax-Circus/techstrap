using SyntaxCircus.Common;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Clients;

/// <summary>The signed-in agent and the agent list (assignee picker). Components never see HTTP: every failure is a <see cref="Result"/> error.</summary>
public interface IAgentsClient
{
    /// <summary>
    /// <c>GET /api/agents/me</c>. The first call after sign-in also creates the agent row, so no ticket call works before it. 403 types:
    /// agent-access-required, agent-inactive, agent-email-required, agent-identity-invalid.
    /// </summary>
    Task<Result<AgentDto>> GetMeAsync(CancellationToken cancellationToken);

    /// <summary>Every agent the caller may see (active agents for an Agent, everyone for an Admin), fetched page by page at the API maximum of 100.</summary>
    Task<Result<IReadOnlyList<AgentListItemDto>>> ListAllAsync(CancellationToken cancellationToken);
}
