using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Formatters;
using TechStrap.Contracts.Intake;

namespace TechStrap.Api.Startup;

/// <summary>Request size limits for the intake endpoints.</summary>
public static class IntakeRequestLimits
{
    public const long JsonBodyBytes = 256 * 1024; // text fields and metadata only (D-034: no API attachments in v1)

    // 25 MiB of files plus room for the text fields and multipart framing; the handler enforces the exact file limits.
    public const long FormBodyBytes = IntakeLimits.MaxMessageBytes + (1024 * 1024);
}

/// <summary>Maps an over-limit request body (a 413 BadHttpRequestException) to a problem+json 413 instead of a 500.</summary>
internal sealed class RequestTooLargeMiddleware(RequestDelegate next)
{
    public const string ErrorCode = "request-too-large";

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (IsTooLarge(ex) && !context.Response.HasStarted)
        {
            context.Response.Clear();
            await Results.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                type: ErrorCode,
                title: "Request too large",
                detail: "The request body is larger than this endpoint accepts.").ExecuteAsync(context);
        }
    }

    // MVC wraps form-read failures (BadHttpRequestException is an IOException) in ValueProviderException; walk the chain.
    internal static bool IsTooLarge(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge })
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Reads the form before model binding. MVC's form value provider swallows an over-limit read into a 400 model error; reading it here lets
/// the 413 BadHttpRequestException reach <see cref="RequestTooLargeMiddleware"/>. The parsed form is cached, so binding reuses it.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class ReadFormBeforeBindingAttribute : Attribute, IAsyncResourceFilter, IOrderedFilter, IApiRequestMetadataProvider
{
    public const string MalformedCode = "request-malformed";
    public const string UnsupportedMediaTypeCode = "unsupported-media-type";

    // RequestFormLimits (and RequestSizeLimit) filters default to Order 900 and lower Order runs first. This must run after them, or the
    // form is parsed under Kestrel's 30 MB / 128 MB defaults instead of the endpoint limits.
    public int Order => 1000;

    /// <summary>Documents the body as multipart/form-data in OpenAPI without [Consumes], which would hide the route's authorization policy behind a 401.</summary>
    public void SetContentTypes(MediaTypeCollection contentTypes)
    {
        contentTypes.Clear();
        contentTypes.Add("multipart/form-data");
    }

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (!request.HasFormContentType)
        {
            context.Result = Problem(StatusCodes.Status415UnsupportedMediaType, UnsupportedMediaTypeCode, "Unsupported media type", "This endpoint accepts multipart/form-data.");
            return;
        }

        try
        {
            await request.ReadFormAsync(context.HttpContext.RequestAborted);
        }
        catch (InvalidDataException)
        {
            // Malformed multipart: model binding reports it as a 400.
        }
        catch (IOException ex) when (!RequestTooLargeMiddleware.IsTooLarge(ex))
        {
            // A body cut short (BadHttpRequestException is an IOException) is the client's fault, not a 500. A 413 is left to RequestTooLargeMiddleware.
            context.Result = Problem(StatusCodes.Status400BadRequest, MalformedCode, "Malformed request", "The request body could not be read.");
            return;
        }

        await next();
    }

    private static ObjectResult Problem(int status, string code, string title, string detail) =>
        new(new ProblemDetails { Status = status, Type = code, Title = title, Detail = detail }) { StatusCode = status };
}

public static class IntakeHosting
{
    public static IApplicationBuilder UseRequestTooLargeProblemDetails(this IApplicationBuilder app) =>
        app.UseMiddleware<RequestTooLargeMiddleware>();
}
