namespace TechStrap.Api.Startup;

public static class AttachmentSandbox
{
    private static readonly string[] _prefixes = ["/api/attachments", "/api/customer/attachments"];

    /// <summary>
    /// Downloads (agent and customer) get <c>Content-Security-Policy: sandbox</c>. The shared security-headers middleware overwrites the CSP
    /// when the response starts, and start callbacks run last-registered-first, so this must be registered before it to have the final say.
    /// </summary>
    public static IApplicationBuilder UseAttachmentSandbox(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (_prefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.OnStarting(() =>
                {
                    if (context.Response.StatusCode == StatusCodes.Status200OK)
                    {
                        var headers = context.Response.Headers;
                        var existing = headers.ContentSecurityPolicy.ToString();
                        headers.ContentSecurityPolicy = string.IsNullOrEmpty(existing) ? "sandbox" : $"{existing}; sandbox";
                    }

                    return Task.CompletedTask;
                });
            }

            await next();
        });
}
