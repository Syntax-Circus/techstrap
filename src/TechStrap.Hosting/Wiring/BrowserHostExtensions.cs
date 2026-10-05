using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SyntaxCircus.AspNetCore.Common;

namespace TechStrap.Hosting.Wiring;

/// <summary>
/// The wiring the two browser hosts (Admin and Portal) share. Each host calls <see cref="AddTechStrapWebHost"/> with its services, <see cref="UseTechStrapWebHost"/> first
/// in its pipeline and <see cref="UseTechStrapErrorPages"/> right after it, so a change to forwarded headers, correlation, security headers or the error pages is made once.
/// Admin-only behaviour (token forwarding, sign-in) stays in the Admin.
/// </summary>
public static class BrowserHostExtensions
{
    /// <summary>The route the 404 re-executes to. Both hosts have a page at it.</summary>
    public const string NotFoundPath = "/not-found";

    /// <summary>The route unhandled exceptions are sent to outside Development.</summary>
    public const string ErrorPath = "/error";

    /// <summary>
    /// Registers what every browser host needs: the correlation id, health checks, trusted-proxy forwarded headers (production fails to start without
    /// configuration), the data-protection key ring (antiforgery and circuit state need a stable key; containers mount a volume at
    /// <c>DataProtection:KeyRingPath</c>), the host-wide <c>HttpClient</c> logging default and the security headers.
    /// </summary>
    /// <param name="contentSecurityPolicy">The Content-Security-Policy to send. Null keeps the package default (which only restricts framing, forms and the base URI).</param>
    public static IServiceCollection AddTechStrapWebHost(this IServiceCollection services, IConfiguration configuration, string? contentSecurityPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddCorrelationId();
        services.AddHealthChecks();
        services.AddTrustedProxyForwardedHeaders(configuration);
        var keyRingPath = configuration["DataProtection:KeyRingPath"];
        if (!string.IsNullOrWhiteSpace(keyRingPath))
        {
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        }

        services.AddTechStrapHttpClientDefaults();
        services.AddTechStrapSecurityHeaders(configuration, contentSecurityPolicy);
        return services;
    }

    /// <summary>
    /// Forwarded headers, then the correlation id, then the security headers. The security headers come before the error pages, so the re-executed 404 page and the
    /// error page carry them too.
    /// </summary>
    /// <param name="downloadPathPrefixes">
    /// Paths that stream a user's file (the Admin's <c>/attachments</c>). Their responses get <c>sandbox</c> appended to the Content-Security-Policy, so a file that is
    /// opened rather than saved cannot run script in the app's origin. The shared security-headers middleware sets the policy when the response starts and would
    /// overwrite a value the endpoint set itself; this step is registered before it, and start callbacks run last-registered-first, so it runs after it and appends.
    /// </param>
    public static WebApplication UseTechStrapWebHost(this WebApplication app, params string[] downloadPathPrefixes)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseForwardedHeaders();
        app.UseCorrelationId();
        if (downloadPathPrefixes.Length > 0)
        {
            var prefixes = downloadPathPrefixes.Select(prefix => new PathString(prefix)).ToArray();
            app.Use(async (context, next) =>
            {
                if (prefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix)))
                {
                    context.Response.OnStarting(() =>
                    {
                        var headers = context.Response.Headers;
                        var existing = headers.ContentSecurityPolicy.ToString();
                        if (!existing.Contains("sandbox", StringComparison.Ordinal))
                        {
                            headers.ContentSecurityPolicy = string.IsNullOrEmpty(existing) ? "sandbox" : $"{existing}; sandbox";
                        }

                        return Task.CompletedTask;
                    });
                }

                await next();
            });
        }

        app.UseSecurityHeaders();
        return app;
    }

    /// <summary>
    /// The plain error page for an unhandled exception (outside Development; BRAND.md section 3), the branded not-found page for a 404 (re-executed, so the 404 status
    /// is kept), and nothing else: humour never covers an error that blocks work, so every other status code keeps its own response.
    /// </summary>
    public static WebApplication UseTechStrapErrorPages(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler(ErrorPath, createScopeForErrors: true);
        }

        app.UseStatusCodePagesWithReExecute(NotFoundPath, createScopeForStatusCodePages: true);
        app.Use(async (context, next) =>
        {
            await next();
            if (context.Response.StatusCode != StatusCodes.Status404NotFound)
            {
                context.Features.Get<IStatusCodePagesFeature>()?.Enabled = false;
            }
        });
        return app;
    }
}
