using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SyntaxCircus.AspNetCore.Common;
using SyntaxCircus.Common;
using TechStrap.Api.Options;
using TechStrap.Api.Security;
using TechStrap.Api.Startup;
using TechStrap.Application.Intake;
using TechStrap.Application.Attachments;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Intake;

namespace TechStrap.Api.Controllers;

/// <summary>Ticket intake for product backends and the SDK, authenticated by a product API key (D-034).</summary>
[ApiController]
[Route(IntakeRoutes.Tickets)]
[EnableRateLimiting(IntakeRateLimitOptions.KeyPolicyName)]
[Authorize(Policy = AuthorizationPolicies.ApiKey)]
public sealed class IntakeController : ControllerBase
{
    [HttpPost("with-attachments")]
    [ReadFormBeforeBinding]
    [RequestSizeLimit(IntakeRequestLimits.FormBodyBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = IntakeRequestLimits.FormBodyBytes * 2)]
    public async Task<IActionResult> SubmitWithAttachments(
        [FromForm] ApiSubmitTicketForm form,
        [FromHeader(Name = HeaderNames.IdempotencyKey)] string? idempotencyKey,
        [FromServices] ISubmitTicketRequestHandler handler,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Dictionary<string, string>? metadata = null;
        if (form.Metadata is not null)
        {
            try
            {
                metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(form.Metadata)
                    ?? throw new JsonException();
            }
            catch (JsonException)
            {
                return Result<SubmitTicketResponse>.Failure(new ResultError(
                    "metadata-invalid", "Metadata must be a JSON object with string values.", ResultErrorKind.Validation, "metadata"))
                    .ToActionResult(this, response => StatusCode(StatusCodes.Status201Created, response));
            }
        }

        var streams = new List<Stream>();
        try
        {
            var files = new List<IncomingAttachment>();
            foreach (var file in form.Attachments ?? [])
            {
                var stream = file.OpenReadStream();
                streams.Add(stream);
                files.Add(new IncomingAttachment(file.FileName, file.ContentType, file.Length, stream));
            }
            var (keyId, productId, trusted) = ApiKeyPrincipal.Read(User);
            var request = new SubmitTicketRequest(form.Email, form.Name, form.Subject, form.Body, form.ExternalUserRef, metadata, form.Type);
            var context = new SubmitTicketContext(IntakeChannel.Api, null, productId, keyId, trusted, false, files, idempotencyKey);
            return (await handler.HandleAsync(request, context, cancellationToken))
                .ToActionResult(this, response => StatusCode(StatusCodes.Status201Created, response));
        }
        finally
        {
            foreach (var stream in streams) await stream.DisposeAsync();
        }
    }

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
