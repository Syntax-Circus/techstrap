using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Options;
using TechStrap.Api.Security;
using TechStrap.Application.Features;

namespace TechStrap.Api.Controllers;

/// <summary>Anonymous published feature content only.</summary>
[ApiController]
[Route("api/public/products/{key}/features")]
[Authorize(Policy = AuthorizationPolicies.Public)]
[EnableRateLimiting(PublicRateLimitOptions.PolicyName)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PublicFeaturesController : ControllerBase
{
    /// <summary>Lists a product's published features with search and progress filters.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        string key, [FromServices] IListPublicFeaturesRequestHandler handler, CancellationToken cancellationToken,
        [FromQuery] string? q = null, [FromQuery] string? status = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25) =>
        (await handler.HandleAsync(key, q, status, page, pageSize, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Reads one published feature; missing, inactive and private content share a neutral 404.</summary>
    [HttpGet("{slug}")]
    public async Task<IActionResult> Get(
        string key, string slug, [FromServices] IGetPublicFeatureRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(key, slug, cancellationToken)).ToActionResult(this, Ok);
}
