using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Application.DeadLetters;
using TechStrap.Application.Persistence;

namespace TechStrap.Api.Controllers;

/// <summary>Dead-lettered emails (Admin only, D-006, D-022). Recipients are masked and payloads are never returned (D-039).</summary>
[ApiController]
[Route("api/dead-letters")]
[Authorize(Policy = AuthorizationPolicies.Admin)]
public sealed class DeadLettersController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromServices] IListDeadLettersRequestHandler handler, CancellationToken cancellationToken,
        [FromQuery] int page = 1, [FromQuery] int pageSize = Paging.DefaultPageSize) =>
        (await handler.HandleAsync(page, pageSize, cancellationToken)).ToActionResult(this, Ok);

    [HttpPost("{id:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id, [FromServices] IRetryDeadLetterRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToActionResult(this, NoContent);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Discard(Guid id, [FromServices] IDiscardDeadLetterRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToActionResult(this, NoContent);
}
