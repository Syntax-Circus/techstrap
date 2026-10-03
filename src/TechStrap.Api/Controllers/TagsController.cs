using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Application.Tags;
using TechStrap.Contracts.Tags;

namespace TechStrap.Api.Controllers;

/// <summary>Global tags. Agents read them for filters; admins manage them (D-022, D-030).</summary>
[ApiController]
[Route("api/tags")]
[Authorize(Policy = AuthorizationPolicies.Agent)]
public sealed class TagsController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromServices] IListTagsRequestHandler listTags, CancellationToken cancellationToken) =>
        (await listTags.HandleAsync(cancellationToken)).ToActionResult(this, Ok);

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> Create(CreateTagRequest request, [FromServices] ICreateTagRequestHandler createTag, CancellationToken cancellationToken) =>
        (await createTag.HandleAsync(request, cancellationToken)).ToActionResult(this, tag => StatusCode(StatusCodes.Status201Created, tag));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> Update(Guid id, UpdateTagRequest request, [FromServices] IUpdateTagRequestHandler updateTag, CancellationToken cancellationToken) =>
        (await updateTag.HandleAsync(id, request, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Deletes a tag. A tag in use is a 409 unless <paramref name="force"/> is true (D-030).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> Delete(Guid id, [FromServices] IDeleteTagRequestHandler deleteTag, CancellationToken cancellationToken, [FromQuery] bool force = false) =>
        (await deleteTag.HandleAsync(id, force, cancellationToken)).ToActionResult(this, NoContent);
}
