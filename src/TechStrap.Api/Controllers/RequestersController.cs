using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Application.Requesters;

namespace TechStrap.Api.Controllers;

/// <summary>Erasing a requester (Admin only, D-006, D-022, D-039).</summary>
[ApiController]
[Route("api/requesters")]
[Authorize(Policy = AuthorizationPolicies.Admin)]
public sealed class RequestersController : ControllerBase
{
    /// <summary>Anonymizes the requester and their data; ticket numbers and events remain. Safe to repeat; re-run if the requester was active during the erase.</summary>
    [HttpPost("{id:guid}/erase")]
    public async Task<IActionResult> Erase(Guid id, [FromServices] IEraseRequesterRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToActionResult(this, NoContent);
}
