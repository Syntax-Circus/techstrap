using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Application.Webhooks;
using TechStrap.Contracts.Webhooks;

namespace TechStrap.Api.Controllers;

[ApiController]
[Route("api/settings/webhooks")]
[Route("api/products/{productId:guid}/webhooks")]
[Authorize(Policy = AuthorizationPolicies.Admin)]
public sealed class WebhooksController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid? productId, [FromServices] IGetWebhookSettingsRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(productId, cancellationToken)).ToActionResult(this, Ok);

    [HttpPut]
    public async Task<IActionResult> Put(Guid? productId, UpdateWebhookSettingsRequest request, [FromServices] IUpdateWebhookSettingsRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(productId, request, cancellationToken)).ToActionResult(this, Ok);

    [HttpGet("deliveries")]
    public async Task<IActionResult> Deliveries(Guid? productId, [FromQuery] int page, [FromServices] IListWebhookDeliveriesRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(productId, page, cancellationToken)).ToActionResult(this, Ok);

    [HttpPost("actions")]
    public async Task<IActionResult> Action(Guid? productId, WebhookActionRequest request, [FromServices] IWebhookActionRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(productId, request, cancellationToken)).ToActionResult(this, Ok);
}
