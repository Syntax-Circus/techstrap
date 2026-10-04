using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Application.Tickets;

namespace TechStrap.Api.Controllers;

/// <summary>Ticket attachment downloads for agents.</summary>
[ApiController]
[Route("api/attachments")]
[Authorize(Policy = AuthorizationPolicies.Agent)]
public sealed class AttachmentsController : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, [FromServices] IGetAttachmentRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToActionResult(this, content => new AttachmentDownloadResult(content));
}
