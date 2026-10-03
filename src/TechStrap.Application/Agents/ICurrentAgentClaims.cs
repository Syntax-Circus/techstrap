using TechStrap.Domain.Agents;

namespace TechStrap.Application.Agents;

/// <summary>
/// The signed-in agent as the identity provider describes them. This is the project-specific identity abstraction (instead
/// of SyntaxCircus.Common.ICurrentUserService, which carries no groups). Application code never reads claims directly.
/// </summary>
public interface ICurrentAgentClaims
{
    /// <summary>Null when the caller is not authenticated, has no subject, or is in neither the agent nor the admin group.</summary>
    AgentClaims? Current { get; }
}

/// <summary>Identity claims plus the role derived from IdP groups alone (D-029): Admin for the admin group, otherwise Agent.</summary>
public sealed record AgentClaims(string Subject, string? Name, string? Email, AgentRole Role);
