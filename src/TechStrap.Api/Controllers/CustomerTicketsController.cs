using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Options;
using TechStrap.Api.Security;
using TechStrap.Api.Startup;
using TechStrap.Application.Attachments;
using TechStrap.Application.Tickets.Customer;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Api.Controllers;

/// <summary>The customer's ticket by link. Authorized by the X-Ticket-Token header; every failure is the same 404 (D-038).</summary>
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

    [HttpPost("ticket/replies")]
    [ReadFormBeforeBinding]
    [RequestSizeLimit(IntakeRequestLimits.FormBodyBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = IntakeRequestLimits.FormBodyBytes * 2)] // Kestrel's limit must trip first, as a 413
    public async Task<IActionResult> Reply(
        [FromHeader(Name = HeaderNames.TicketToken)] string? token,
        [FromForm] CustomerReplyForm form,
        [FromServices] IAddCustomerReplyRequestHandler handler,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
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

            return (await handler.HandleAsync(token, new AddCustomerReplyRequest(form.Body), attachments, cancellationToken))
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

    [HttpGet("attachments/{id:guid}")]
    public async Task<IActionResult> GetAttachment(
        [FromHeader(Name = HeaderNames.TicketToken)] string? token,
        Guid id,
        [FromServices] IGetCustomerAttachmentRequestHandler handler,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store"; // 404s; AttachmentDownloadResult sets private, no-store on success
        return (await handler.HandleAsync(token, id, cancellationToken)).ToActionResult(this, content =>
        {
            Response.RegisterForDisposeAsync(content.Content);
            return new AttachmentDownloadResult(content);
        });
    }
}
