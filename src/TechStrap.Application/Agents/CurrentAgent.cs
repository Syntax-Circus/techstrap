using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Agents;

/// <summary>
/// Resolves the acting agent for handlers that act as them (audit actor, own profile). The Agent/Admin policies already refuse
/// deactivated agents at the host; this repeats the check because Application must not rely on the transport.
/// </summary>
internal static class CurrentAgent
{
    public static async Task<Result<Agent>> RequireActiveAsync(ICurrentAgentClaims currentAgent, IAgentRepository agents, CancellationToken cancellationToken)
    {
        if (currentAgent.Current is not { } claims)
        {
            return Result<Agent>.Failure(AgentErrors.AccessRequired());
        }

        var agent = await agents.GetBySubjectAsync(claims.Subject, cancellationToken);
        if (agent is null)
        {
            return Result<Agent>.Failure(AgentErrors.NotProvisioned());
        }

        return agent.IsActive ? Result<Agent>.Success(agent) : Result<Agent>.Failure(AgentErrors.Inactive());
    }
}
