using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Options;
using TechStrap.Api.Security;
using TechStrap.Application.Settings;

namespace TechStrap.Api.Controllers;

[ApiController]
[Route("api/public/site")]
[Authorize(Policy = AuthorizationPolicies.Public)]
[EnableRateLimiting(PublicRateLimitOptions.PolicyName)]
public sealed class PublicSiteController : ControllerBase
{
    /// <summary>The default theme pack key for the Portal (D-053): cacheable for 300 seconds, like the public product routes.</summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromServices] IGetPublicSiteRequestHandler handler, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await handler.HandleAsync(cancellationToken)).ToActionResult(this, site =>
        {
            Response.Headers.CacheControl = "public, max-age=300";
            return Ok(site);
        });
    }
}
