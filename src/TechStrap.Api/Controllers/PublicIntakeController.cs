using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Api.Startup;
using TechStrap.Application.Attachments;
using TechStrap.Application.Intake;
using TechStrap.Contracts.Intake;

namespace TechStrap.Api.Controllers;

/// <summary>Anonymous ticket intake for the portal contact form.</summary>
[ApiController]
[Route("api/public/products/{productKey}/tickets")]
[Authorize(Policy = AuthorizationPolicies.Public)]
public sealed class PublicIntakeController : ControllerBase
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ReadFormBeforeBinding]
    [RequestSizeLimit(IntakeRequestLimits.FormBodyBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = IntakeRequestLimits.FormBodyBytes * 2)] // Kestrel's limit must trip first, as a 413
    public async Task<IActionResult> Submit(
        string productKey,
        [FromForm] PublicSubmitTicketForm form,
        [FromServices] ISubmitTicketRequestHandler handler,
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

            var request = new SubmitTicketRequest(form.Email, form.Name, form.Subject, form.Body, null, null);
            var context = new SubmitTicketContext(
                IntakeChannel.Web, productKey, null, null, false, !string.IsNullOrEmpty(form.Website), attachments, null);
            return (await handler.HandleAsync(request, context, cancellationToken))
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
