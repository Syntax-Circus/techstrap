using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Options;
using TechStrap.Api.Security;
using TechStrap.Application.Products;

namespace TechStrap.Api.Controllers;

[ApiController]
[Route("api/public/products")]
[Authorize(Policy = AuthorizationPolicies.Public)]
[EnableRateLimiting(PublicRateLimitOptions.PolicyName)]
public sealed class PublicProductsController : ControllerBase
{
    [HttpGet("{productKey}")]
    public async Task<IActionResult> Get(string productKey, [FromServices] IGetPublicProductRequestHandler handler, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await handler.HandleAsync(productKey, cancellationToken)).ToActionResult(this, product =>
        {
            Response.Headers.CacheControl = "public, max-age=300";
            return Ok(product);
        });
    }
}
