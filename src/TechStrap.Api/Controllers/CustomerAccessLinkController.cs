using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Options;
using TechStrap.Api.Security;
using TechStrap.Application.Tickets.Customer;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Api.Controllers;

/// <summary>The lost-link request: any well-formed address gets the same empty 202 (D-038); a malformed one is 400.</summary>
[ApiController]
[Route("api/customer/access-link")]
[Authorize(Policy = AuthorizationPolicies.Public)]
[EnableRateLimiting(CustomerRateLimitOptions.LostLinkPolicyName)]
public sealed class CustomerAccessLinkController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> RequestLink(
        [FromBody] RequestNewAccessLinkRequest request,
        [FromServices] IRequestNewAccessLinkRequestHandler handler,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await handler.HandleAsync(request, cancellationToken)).ToActionResult(this, Accepted);
    }
}
