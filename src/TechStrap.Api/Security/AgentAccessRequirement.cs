using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using TechStrap.Api.Options;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Agents;

namespace TechStrap.Api.Security;

/// <summary>The caller must be in the agent or admin group; <see cref="AdminOnly"/> requires the admin group (D-029).</summary>
public sealed class AgentAccessRequirement(bool adminOnly) : IAuthorizationRequirement
{
    public bool AdminOnly { get; } = adminOnly;
}

/// <summary>
/// Host-level policy check: group membership from the token, and a stored agent row that is not deactivated. A deactivated agent
/// is refused on every agent endpoint even though their token is still valid (D-029). Agents without a row yet pass; only
/// GET /api/agents/me provisions, and handlers that need the row ask the agent to open TechStrap first.
/// </summary>
public sealed class AgentAccessAuthorizationHandler(IOptions<AgentAccessOptions> options, IAgentRepository agents) : AuthorizationHandler<AgentAccessRequirement>
{
    public const string AccessRequired = "agent-access-required";
    public const string AdminRequired = "admin-access-required";
    public const string Inactive = "agent-inactive";

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AgentAccessRequirement requirement)
    {
        var claims = ClaimsCurrentAgentClaims.FromPrincipal(context.User, options.Value);
        if (claims is null)
        {
            context.Fail(new AuthorizationFailureReason(this, AccessRequired));
            return;
        }

        if (requirement.AdminOnly && claims.Role != AgentRole.Admin)
        {
            context.Fail(new AuthorizationFailureReason(this, AdminRequired));
            return;
        }

        var cancellationToken = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        if (await agents.GetBySubjectAsync(claims.Subject, cancellationToken) is { IsActive: false })
        {
            context.Fail(new AuthorizationFailureReason(this, Inactive));
            return;
        }

        context.Succeed(requirement);
    }
}
