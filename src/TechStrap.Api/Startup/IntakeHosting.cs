namespace TechStrap.Api.Startup;

/// <summary>Request size limits for the intake endpoints.</summary>
public static class IntakeRequestLimits
{
    public const long JsonBodyBytes = 256 * 1024; // text fields and metadata only (D-034: no API attachments in v1)
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
    private static bool IsTooLarge(Exception? ex)
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

public static class IntakeHosting
{
    public static IApplicationBuilder UseRequestTooLargeProblemDetails(this IApplicationBuilder app) =>
        app.UseMiddleware<RequestTooLargeMiddleware>();
}
