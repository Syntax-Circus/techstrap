using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Api.Startup;
using TechStrap.Application.Knowledge;

namespace TechStrap.Api.Controllers;

/// <summary>Multipart form posted to upload one KB image: the file in the field named <c>file</c>.</summary>
public sealed class KbImageForm
{
    public IFormFile? File { get; set; }
}

/// <summary>Uploads of KB images (PHASE-08, D-044). The images are read back, publicly, from <c>GET /kb-images/{name}</c>.</summary>
[ApiController]
[Route("api/kb/images")]
[Authorize(Policy = AuthorizationPolicies.Agent)]
public sealed class KbImagesController : ControllerBase
{
    /// <summary>One png, jpeg, gif or webp up to 5 MB, by its leading bytes. A larger body than the request limit is a 413 before the handler runs.</summary>
    [HttpPost]
    [ReadFormBeforeBinding]
    [RequestSizeLimit(KbRequestLimits.ImageFormBytes)]
    // The form limit sits above the request-size limit so Kestrel's 413 trips first, not a form-binding error.
    [RequestFormLimits(MultipartBodyLengthLimit = KbRequestLimits.ImageFormBytes * 2)]
    public async Task<IActionResult> Upload([FromForm] KbImageForm form, [FromServices] IUploadKbImageRequestHandler handler, CancellationToken cancellationToken)
    {
        if (form.File is not { } file)
        {
            return (await handler.HandleAsync(null, cancellationToken)).ToActionResult(this, response => StatusCode(StatusCodes.Status201Created, response));
        }

        await using var stream = file.OpenReadStream();
        return (await handler.HandleAsync(new IncomingKbImage(file.Length, stream), cancellationToken))
            .ToActionResult(this, response => StatusCode(StatusCodes.Status201Created, response));
    }
}
