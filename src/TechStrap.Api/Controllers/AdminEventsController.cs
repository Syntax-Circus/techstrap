using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Application.AdminEvents;
using TechStrap.Application.Persistence;

namespace TechStrap.Api.Controllers;

/// <summary>The admin audit log (Admin only, D-022).</summary>
[ApiController]
[Route("api/admin-events")]
[Authorize(Policy = AuthorizationPolicies.Admin)]
public sealed class AdminEventsController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromServices] IListAdminEventsRequestHandler listAdminEvents,
        CancellationToken cancellationToken,
        [FromQuery] string? subjectType = null,
        [FromQuery] Guid? actorId = null,
        [FromQuery] DateTimeOffset? asOf = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize) =>
        (await listAdminEvents.HandleAsync(subjectType, actorId, asOf, page, pageSize, cancellationToken)).ToActionResult(this, Ok);
}
