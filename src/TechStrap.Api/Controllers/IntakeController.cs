using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Api.Startup;
using TechStrap.Application.Intake;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Intake;

namespace TechStrap.Api.Controllers;

/// <summary>Ticket intake for product backends and the SDK, authenticated by a product API key (D-034).</summary>
[ApiController]
[Route("api/intake/tickets")]
[Authorize(Policy = AuthorizationPolicies.ApiKey)]
public sealed class IntakeController : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(IntakeRequestLimits.JsonBodyBytes)]
    public async Task<IActionResult> Submit(
        [FromBody] SubmitTicketRequest request,
        [FromHeader(Name = HeaderNames.IdempotencyKey)] string? idempotencyKey,
        [FromServices] ISubmitTicketRequestHandler handler,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store"; // the response carries the customer ticket link
        var (keyId, productId, trusted) = ApiKeyPrincipal.Read(User);
        var context = new SubmitTicketContext(IntakeChannel.Api, null, productId, keyId, trusted, false, [], idempotencyKey);
        return (await handler.HandleAsync(request, context, cancellationToken))
            .ToActionResult(this, response => StatusCode(StatusCodes.Status201Created, response));
    }
}
