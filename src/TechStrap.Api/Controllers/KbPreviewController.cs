using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Api.Startup;
using TechStrap.Application.Knowledge;
using TechStrap.Contracts.Kb;

namespace TechStrap.Api.Controllers;

/// <summary>The Admin editor preview (D-021, D-044): Markdown in, sanitized HTML out, nothing stored. Agents only, and the source is limited so it cannot serve as a free renderer.</summary>
[ApiController]
[Route("api/kb/preview")]
[Authorize(Policy = AuthorizationPolicies.Agent)]
public sealed class KbPreviewController : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(KbRequestLimits.JsonBodyBytes)]
    public async Task<IActionResult> Render([FromBody] KbPreviewRequest request, [FromServices] IRenderKbPreviewRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToActionResult(this, Ok);
}
