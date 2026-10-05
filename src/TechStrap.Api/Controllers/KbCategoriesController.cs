using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Application.Knowledge;
using TechStrap.Contracts.Kb;

namespace TechStrap.Api.Controllers;

/// <summary>The knowledge-base categories (PHASE-08, D-044). Agents list, create and update; only an Admin deletes (D-022).</summary>
[ApiController]
[Route("api/kb/categories")]
[Authorize(Policy = AuthorizationPolicies.Agent)]
public sealed class KbCategoriesController : ControllerBase
{
    /// <summary>Sort order, then name. Without <c>productId</c> every category is listed; with one, its own plus the shared ones unless <c>includeShared</c> is false.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? productId, [FromServices] IListKbCategoriesRequestHandler handler, CancellationToken cancellationToken, [FromQuery] bool includeShared = true) =>
        (await handler.HandleAsync(productId, includeShared, cancellationToken)).ToActionResult(this, Ok);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateKbCategoryRequest request, [FromServices] ICreateKbCategoryRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToActionResult(this, category => StatusCode(StatusCodes.Status201Created, category));

    /// <summary>Changes the name, description and sort order. The version is required; a stale one is 409.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateKbCategoryRequest request, [FromServices] IUpdateKbCategoryRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Deletes a category that holds no article of any status (409 <c>kb-category-in-use</c> otherwise). Admin only.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> Delete(Guid id, [FromServices] IDeleteKbCategoryRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToActionResult(this, NoContent);
}
