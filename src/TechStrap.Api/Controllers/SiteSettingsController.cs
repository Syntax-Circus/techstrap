using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Application.Settings;
using TechStrap.Contracts.Settings;

namespace TechStrap.Api.Controllers;

/// <summary>Deployment-wide settings: today the default Portal theme pack (D-053). Admin only.</summary>
[ApiController]
[Route("api/settings/site")]
[Authorize(Policy = AuthorizationPolicies.Admin)]
public sealed class SiteSettingsController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromServices] IGetSiteSettingsRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(cancellationToken)).ToActionResult(this, Ok);

    [HttpPut]
    public async Task<IActionResult> Put(UpdateSiteSettingsRequest request, [FromServices] IUpdateSiteSettingsRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToActionResult(this, Ok);
}
