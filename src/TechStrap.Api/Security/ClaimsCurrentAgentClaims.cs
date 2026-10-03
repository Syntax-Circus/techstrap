using System.Security.Claims;
using Microsoft.Extensions.Options;
using TechStrap.Api.Options;
using TechStrap.Application.Agents;
using TechStrap.Domain.Agents;

namespace TechStrap.Api.Security;

/// <summary>
/// ICurrentAgentClaims over the request principal. Claim names are the raw OIDC names (sub, email, name,
/// preferred_username) because AgentAuthenticationSetup turns off inbound claim mapping.
/// </summary>
public sealed class ClaimsCurrentAgentClaims(IHttpContextAccessor httpContextAccessor, IOptions<AgentAccessOptions> options) : ICurrentAgentClaims
{
    public const string SubjectClaim = "sub";
    public const string EmailClaim = "email";
    public const string NameClaim = "name";
    public const string PreferredUsernameClaim = "preferred_username";

    public AgentClaims? Current =>
        httpContextAccessor.HttpContext?.User is { } user ? FromPrincipal(user, options.Value) : null;

    public static AgentClaims? FromPrincipal(ClaimsPrincipal principal, AgentAccessOptions options)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var subject = principal.FindFirst(SubjectClaim)?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        AgentRole? role = AgentGroups.Has(principal, options.GroupClaimType, options.AdminGroup) ? AgentRole.Admin
            : AgentGroups.Has(principal, options.GroupClaimType, options.AgentGroup) ? AgentRole.Agent
            : null;

        return role is null
            ? null
            : new AgentClaims(
                subject,
                principal.FindFirst(NameClaim)?.Value ?? principal.FindFirst(PreferredUsernameClaim)?.Value,
                principal.FindFirst(EmailClaim)?.Value,
                role.Value);
    }
}
