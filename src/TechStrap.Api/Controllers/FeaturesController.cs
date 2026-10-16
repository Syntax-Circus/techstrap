using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Application.Features;
using TechStrap.Contracts.Features;

namespace TechStrap.Api.Controllers;

/// <summary>Agent management of curated product features.</summary>
[ApiController]
[Route("api/features")]
[Authorize(Policy = AuthorizationPolicies.Agent)]
public sealed class FeaturesController : ControllerBase
{
    /// <summary>Lists features by newest update.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromServices] IListFeaturesRequestHandler handler, CancellationToken cancellationToken,
        [FromQuery] Guid? productId = null, [FromQuery] string? q = null,
        [FromQuery] string? status = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 25) =>
        (await handler.HandleAsync(productId, q, status, page, pageSize, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Reads an editable feature.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(
        Guid id, [FromServices] IGetFeatureRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Creates a private draft with permanent product and slug.</summary>
    [HttpPost]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Create(
        [FromBody] CreateFeatureRequest request, [FromServices] ICreateFeatureRequestHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToActionResult(this, f => StatusCode(StatusCodes.Status201Created, f));

    /// <summary>Changes curated content and independent progress using a required version.</summary>
    [HttpPut("{id:guid}")]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateFeatureRequest request, [FromServices] IUpdateFeatureRequestHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Publishes the curated content.</summary>
    [HttpPost("{id:guid}/publish")]
    public async Task<IActionResult> Publish(
        Guid id, [FromQuery] uint version, [FromServices] IPublishFeatureRequestHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, version, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Immediately removes the feature from anonymous views.</summary>
    [HttpPost("{id:guid}/unpublish")]
    public async Task<IActionResult> Unpublish(
        Guid id, [FromQuery] uint version, [FromServices] IUnpublishFeatureRequestHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, version, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Private requests linked to a feature; never returned anonymously.</summary>
    [HttpGet("{id:guid}/tickets")]
    public async Task<IActionResult> Tickets(
        Guid id, [FromServices] IListFeatureTicketsRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToActionResult(this, Ok);
}
