using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TechStrap.Api.Security;

namespace TechStrap.Api.Tests.Auth;

/// <summary>Test-only endpoints that carry the real policies, added to the host as an application part by AgentAuthTests.</summary>
[ApiController]
[Route("__test")]
public sealed class AuthProbeController : ControllerBase
{
    [HttpGet("agent")]
    [Authorize(Policy = AuthorizationPolicies.Agent)]
    public IActionResult AgentOnly() => Ok();

    [HttpGet("admin")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public IActionResult AdminOnly() => Ok();
}
