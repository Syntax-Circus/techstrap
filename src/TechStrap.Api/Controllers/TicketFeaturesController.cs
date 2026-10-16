using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Application.Features;
using TechStrap.Contracts.Features;

namespace TechStrap.Api.Controllers;

/// <summary>Agent-only linking of private feature request tickets.</summary>
[ApiController]
[Route("api/tickets/{id:guid}/feature")]
[Authorize(Policy = AuthorizationPolicies.Agent)]
public sealed class TicketFeaturesController : ControllerBase
{
    /// <summary>Links, relinks or unlinks a request using its required row version.</summary>
    [HttpPost]
    public async Task<IActionResult> Link(
        Guid id, [FromBody] LinkTicketFeatureRequest request, [FromServices] ILinkTicketFeatureRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToActionResult(this, Ok);
}
