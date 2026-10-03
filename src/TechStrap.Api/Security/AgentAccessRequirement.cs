using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using TechStrap.Api.Options;
using TechStrap.Domain.Agents;

namespace TechStrap.Api.Security;

/// <summary>The caller must be in the agent or admin group; <see cref="AdminOnly"/> requires the admin group (D-029).</summary>
public sealed class AgentAccessRequirement(bool adminOnly) : IAuthorizationRequirement
{
    public bool AdminOnly { get; } = adminOnly;
}

/// <summary>Host-level policy check. Task 5 adds the refusal of deactivated agents.</summary>
public sealed class AgentAccessAuthorizationHandler(IOptions<AgentAccessOptions> options) : AuthorizationHandler<AgentAccessRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AgentAccessRequirement requirement)
    {
        var claims = ClaimsCurrentAgentClaims.FromPrincipal(context.User, options.Value);
        if (claims is not null && (!requirement.AdminOnly || claims.Role == AgentRole.Admin))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
