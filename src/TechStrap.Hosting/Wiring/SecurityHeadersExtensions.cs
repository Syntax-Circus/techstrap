using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SyntaxCircus.AspNetCore.Common;

namespace TechStrap.Hosting.Wiring;

public static class SecurityHeadersExtensions
{
    /// <summary>
    /// Registers the package's security headers (<c>Referrer-Policy</c>, <c>X-Frame-Options</c>, <c>X-Content-Type-Options</c>, <c>Permissions-Policy</c>,
    /// <c>Strict-Transport-Security</c> and a <c>Content-Security-Policy</c>), bound from the <c>SecurityHeaders</c> section. When
    /// <paramref name="contentSecurityPolicy"/> is given it replaces the policy from configuration (a post-configure step over the bound options). The middleware, and the removal of
    /// <c>Strict-Transport-Security</c> in Development, are added by <see cref="BrowserHostExtensions.UseTechStrapWebHost"/>.
    /// </summary>
    public static IServiceCollection AddTechStrapSecurityHeaders(this IServiceCollection services, IConfiguration configuration, string? contentSecurityPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSecurityHeaders(configuration);
        if (!string.IsNullOrWhiteSpace(contentSecurityPolicy))
        {
            services.PostConfigure<SecurityHeadersOptions>(options => options.ContentSecurityPolicy = contentSecurityPolicy);
        }

        return services;
    }
}
