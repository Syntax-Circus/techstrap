using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Net.Http.Headers;
using TechStrap.Application.Attachments;

namespace TechStrap.Api.Controllers;

/// <summary>
/// Streams an attachment as a download. Always <c>Content-Disposition: attachment</c> (filename* plus an ASCII fallback),
/// <c>nosniff</c>, <c>no-store</c> and (via the Startup/AttachmentSandbox.cs middleware, because the shared security-headers middleware overwrites it) <c>Content-Security-Policy: sandbox</c>. The stored content type, including text/html and other text/*, is kept as is:
/// attachment and nosniff already stop the browser rendering it inline.
/// </summary>
internal sealed class AttachmentDownloadResult(AttachmentContent content) : IActionResult, IStatusCodeActionResult
{
    public int? StatusCode => StatusCodes.Status200OK;

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        // Also registered for disposal in the controller, so a filter that replaces this result cannot leak the stream.
        await using var stream = content.Content;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = content.ContentType;
        response.ContentLength = content.Size;
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.CacheControl = "private, no-store";
        response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileNameStar = content.FileName,
            FileName = SafeAsciiName(content.FileName),
        }.ToString();
        await stream.CopyToAsync(response.Body, context.HttpContext.RequestAborted);
    }

    // ASCII fallback for old clients: anything outside printable ASCII, plus quote and backslash, becomes '_'.
    private static string SafeAsciiName(string name) =>
        string.Concat(name.Select(c => c is >= ' ' and <= '~' and not '"' and not '\\' ? c : '_'));
}
