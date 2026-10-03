using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Application.Agents;

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
}
