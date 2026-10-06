using Microsoft.AspNetCore.Http.Features;
using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Uploads;

/// <summary>
/// Answers a post whose declared <c>Content-Length</c> is over the limit that applies to its endpoint (<c>[RequestSizeLimit]</c> on the form page; endpoint routing has already put it on
/// <see cref="IHttpMaxRequestBodySizeFeature"/> before any later middleware runs) with a plain 413 and a sentence, before anything is read. Without it the same post is rejected by Kestrel without being
/// buffered, but the antiforgery check reads the form first and reports the failed read as a 400 with the framework's own text about a token. A chunked body (no declared length) over the limit still
/// gets that 400; it is just as unbuffered. Must run before <c>UseAntiforgery</c>. A server without the feature (the in-memory test server) is a no-op.
/// </summary>
public sealed class RequestTooLargeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize is { } limit
            && context.Request.ContentLength is { } declared
            && declared > limit)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsync(ProblemCopy.PayloadTooLarge, context.RequestAborted);
            return;
        }

        await next(context);
    }
}
