using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Options;
using TechStrap.Api.Security;
using TechStrap.Application.Tickets.Customer;
using TechStrap.Contracts.Http;

namespace TechStrap.Api.Controllers;

/// <summary>The customer's ticket by link. Authorised by the X-Ticket-Token header; every failure is the same 404 (D-038).</summary>
[ApiController]
[Route("api/customer")]
[Authorize(Policy = AuthorizationPolicies.Public)]
[EnableRateLimiting(CustomerRateLimitOptions.TokenAccessPolicyName)]
public sealed class CustomerTicketsController : ControllerBase
{
    [HttpGet("ticket")]
    public async Task<IActionResult> Get(
        [FromHeader(Name = HeaderNames.TicketToken)] string? token,
        [FromServices] IGetCustomerTicketRequestHandler handler,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await handler.HandleAsync(token, cancellationToken)).ToActionResult(this, Ok);
    }
}
