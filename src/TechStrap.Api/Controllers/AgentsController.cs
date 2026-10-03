using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Agents;

namespace TechStrap.Api.Controllers;

/// <summary>Agent profile, list and access management (PHASE-04). Every action delegates to one named handler.</summary>
[ApiController]
[Route("api/agents")]
[Authorize(Policy = AuthorizationPolicies.Agent)]
public sealed class AgentsController : ControllerBase
{
    /// <summary>The signed-in agent; provisions them on first call.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMe([FromServices] IGetCurrentAgentRequestHandler getCurrentAgent, CancellationToken cancellationToken) =>
        (await getCurrentAgent.HandleAsync(cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Agents: active agents for assignment. Admins: everyone, with role and status.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromServices] IListAgentsRequestHandler listAgents,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize) =>
        (await listAgents.HandleAsync(page, pageSize, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Activates or deactivates an agent (Admin). Roles come from IdP groups (D-029).</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateAgentRequest request,
        [FromServices] IUpdateAgentRequestHandler updateAgent,
        CancellationToken cancellationToken) =>
        (await updateAgent.HandleAsync(id, request, cancellationToken)).ToActionResult(this, Ok);

    [HttpGet("me/notification-preferences")]
    public async Task<IActionResult> GetMyNotificationPreferences(
        [FromServices] IGetMyNotificationPreferencesRequestHandler getPreferences,
        CancellationToken cancellationToken) =>
        (await getPreferences.HandleAsync(cancellationToken)).ToActionResult(this, Ok);

    [HttpPut("me/notification-preferences")]
    public async Task<IActionResult> UpdateMyNotificationPreferences(
        UpdateNotificationPreferencesRequest request,
        [FromServices] IUpdateNotificationPreferencesRequestHandler updatePreferences,
        CancellationToken cancellationToken) =>
        (await updatePreferences.HandleAsync(request, cancellationToken)).ToActionResult(this, NoContent);

    /// <summary>Sets or clears the caller's customer-facing display name (D-024).</summary>
    [HttpPut("me/profile")]
    public async Task<IActionResult> UpdateMyProfile(
        UpdateMyProfileRequest request,
        [FromServices] IUpdateMyProfileRequestHandler updateProfile,
        CancellationToken cancellationToken) =>
        (await updateProfile.HandleAsync(request, cancellationToken)).ToActionResult(this, NoContent);
}
