using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Api.Startup;
using TechStrap.Application.Attachments;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tickets;
using TechStrap.Contracts.Intake;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Api.Controllers;

/// <summary>The agent ticket queue and ticket operations (PHASE-06a). Every action delegates to one named handler.</summary>
[ApiController]
[Route("api/tickets")]
[Authorize(Policy = AuthorizationPolicies.Agent)]
public sealed class TicketsController : ControllerBase
{
    /// <summary>The queue: a view, optional filters and search, one page. A search matches internal notes too (agent-only).</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? view,
        [FromQuery] Guid? productId,
        [FromQuery] string? status,
        [FromQuery] string? priority,
        [FromQuery] Guid? assigneeId,
        [FromQuery] Guid? tagId,
        [FromQuery] Guid? requesterId,
        [FromQuery] string? search,
        [FromServices] IListTicketsRequestHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize) =>
        (await handler.HandleAsync(new ListTicketsRequest(view, productId, status, priority, assigneeId, tagId, requesterId, search, page, pageSize), cancellationToken))
            .ToActionResult(this, Ok);

    /// <summary>One ticket with its full timeline, by id or by number such as ORB-42 (agent-only: internal notes are included).</summary>
    [HttpGet("{reference}")]
    public async Task<IActionResult> Get(string reference, [FromServices] IGetTicketRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(reference, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Hard-deletes a ticket, its outbox rows and its files (Admin only, D-006, D-022, D-039). Follow-ups survive, unlinked.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> Delete(Guid id, [FromServices] IDeleteTicketRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToActionResult(this, NoContent);

    /// <summary>The number on each queue tab.</summary>
    [HttpGet("counts")]
    public async Task<IActionResult> Counts([FromServices] ICountTicketViewsRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(cancellationToken)).ToActionResult(this, Ok);

    /// <summary>An agent-only note (Markdown). Never emailed; the row version is optional.</summary>
    [HttpPost("{id:guid}/notes")]
    public async Task<IActionResult> AddNote(
        Guid id, [FromBody] AddInternalNoteRequest request, [FromServices] IAddInternalNoteRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToActionResult(this, response => StatusCode(StatusCodes.Status201Created, response));

    /// <summary>Changes the status. The row version is required; a stale one is 409.</summary>
    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> ChangeStatus(
        Guid id, [FromBody] ChangeTicketStatusRequest request, [FromServices] IChangeTicketStatusRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Assigns the ticket to an agent, or unassigns it with a null assignee. The row version is required.</summary>
    [HttpPut("{id:guid}/assignee")]
    public async Task<IActionResult> Assign(
        Guid id, [FromBody] AssignTicketRequest request, [FromServices] IAssignTicketRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Changes the priority. The row version is required.</summary>
    [HttpPut("{id:guid}/priority")]
    public async Task<IActionResult> ChangePriority(
        Guid id, [FromBody] ChangeTicketPriorityRequest request, [FromServices] IChangeTicketPriorityRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Moves the ticket to another product; its number never changes. The row version is required.</summary>
    [HttpPut("{id:guid}/product")]
    public async Task<IActionResult> MoveProduct(
        Guid id, [FromBody] MoveTicketProductRequest request, [FromServices] IMoveTicketProductRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Adds a tag. Adding one the ticket already has succeeds. The row version is required.</summary>
    [HttpPost("{id:guid}/tags")]
    public async Task<IActionResult> AddTag(
        Guid id, [FromBody] AddTicketTagRequest request, [FromServices] IAddTicketTagRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Removes a tag. Removing one the ticket does not have succeeds. The row version comes from the query string and is required.</summary>
    [HttpDelete("{id:guid}/tags/{tagId:guid}")]
    public async Task<IActionResult> RemoveTag(
        Guid id, Guid tagId, [FromQuery] uint? rowVersion, [FromServices] IRemoveTicketTagRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, tagId, rowVersion, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Marks or unmarks the ticket as spam; spam leaves every view except Spam. The row version is required.</summary>
    [HttpPut("{id:guid}/spam")]
    public async Task<IActionResult> MarkSpam(
        Guid id, [FromBody] MarkTicketSpamRequest request, [FromServices] IMarkTicketSpamRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>A public reply to the customer: Markdown body, optional files and linked articles, optionally solving the ticket.</summary>
    [HttpPost("{id:guid}/replies")]
    [ReadFormBeforeBinding]
    [RequestSizeLimit(IntakeRequestLimits.FormBodyBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = IntakeRequestLimits.FormBodyBytes * 2)] // Kestrel's limit must trip first, as a 413
    public async Task<IActionResult> Reply(
        Guid id,
        [FromForm] AgentReplyForm form,
        [FromServices] IAddAgentReplyRequestHandler handler,
        CancellationToken cancellationToken)
    {
        var files = form.Attachments ?? [];
        var streams = new List<Stream>(files.Count);
        try
        {
            var attachments = new List<IncomingAttachment>(files.Count);
            foreach (var file in files)
            {
                var stream = file.OpenReadStream();
                streams.Add(stream);
                attachments.Add(new IncomingAttachment(file.FileName, file.ContentType, file.Length, stream));
            }

            var request = new AddAgentReplyRequest(form.Body, form.LinkedArticleIds, form.StatusAfter, form.RowVersion);
            return (await handler.HandleAsync(id, request, attachments, cancellationToken))
                .ToActionResult(this, response => StatusCode(StatusCodes.Status201Created, response));
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }
}
