using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Api.Startup;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;

namespace TechStrap.Api.Controllers;

/// <summary>The knowledge-base articles (PHASE-08, D-044). Every agent may list, read, write, publish and archive; deleting is not offered.</summary>
[ApiController]
[Route("api/kb/articles")]
[Authorize(Policy = AuthorizationPolicies.Agent)]
public sealed class KbArticlesController : ControllerBase
{
    /// <summary>Articles of any status, newest update first; <c>text</c> searches title, summary and body, best match first. Shared articles are included with a product unless <c>includeShared</c> is false.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? productId,
        [FromQuery] string? status,
        [FromQuery] Guid? categoryId,
        [FromQuery] string? text,
        [FromServices] IListKbArticlesRequestHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] bool sharedOnly = false,
        [FromQuery] bool includeShared = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize) =>
        (await handler.HandleAsync(new ListKbArticlesRequest(productId, sharedOnly, includeShared, status, categoryId, text, page, pageSize), cancellationToken))
            .ToActionResult(this, Ok);

    /// <summary>One article with its Markdown source and the version to send back on an update.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, [FromServices] IGetKbArticleRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>A new Draft. The product (null for shared) and the slug are permanent; the slug must be free in every scope.</summary>
    [HttpPost]
    [RequestSizeLimit(KbRequestLimits.JsonBodyBytes)]
    public async Task<IActionResult> Create([FromBody] CreateKbArticleRequest request, [FromServices] ICreateKbArticleRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToActionResult(this, article => StatusCode(StatusCodes.Status201Created, article));

    /// <summary>Replaces the category, title, summary and body. The version is required; a stale one is 409. An Archived article returns to Draft.</summary>
    [HttpPut("{id:guid}")]
    [RequestSizeLimit(KbRequestLimits.JsonBodyBytes)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateKbArticleRequest request, [FromServices] IUpdateKbArticleRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Publishes. Needs a title, a slug, a body and a category (400 <c>kb-publish-incomplete</c>). The version is optional; a stale one is 409.</summary>
    [HttpPost("{id:guid}/publish")]
    public async Task<IActionResult> Publish(
        Guid id, [FromServices] IPublishKbArticleRequestHandler handler, CancellationToken cancellationToken, [FromQuery] uint? version = null) =>
        (await handler.HandleAsync(id, version, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Archives: out of public search, the article page, the sitemap and the category counts. The version is optional; a stale one is 409.</summary>
    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(
        Guid id, [FromServices] IArchiveKbArticleRequestHandler handler, CancellationToken cancellationToken, [FromQuery] uint? version = null) =>
        (await handler.HandleAsync(id, version, cancellationToken)).ToActionResult(this, Ok);
}
