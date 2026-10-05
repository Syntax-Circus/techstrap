using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SyntaxCircus.AspNetCore.Common;

namespace TechStrap.Hosting.Wiring;

public static class SecurityHeadersExtensions
{
    /// <summary>The configuration key the package reads its Content-Security-Policy from.</summary>
    internal const string ContentSecurityPolicyKey = "SecurityHeaders:ContentSecurityPolicy";

    /// <summary>
    /// Registers the package's security headers (<c>Referrer-Policy</c>, <c>X-Frame-Options</c>, <c>X-Content-Type-Options</c>, <c>Permissions-Policy</c>,
    /// <c>Strict-Transport-Security</c> and a <c>Content-Security-Policy</c>), bound from the <c>SecurityHeaders</c> section. When
    /// <paramref name="contentSecurityPolicy"/> is given it replaces the policy from configuration: it is layered over the configuration the package binds, because the
    /// options type is init-only. The middleware is added by <see cref="BrowserHostExtensions.UseTechStrapWebHost"/>.
    /// </summary>
    public static IServiceCollection AddTechStrapSecurityHeaders(this IServiceCollection services, IConfiguration configuration, string? contentSecurityPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var source = string.IsNullOrWhiteSpace(contentSecurityPolicy)
            ? configuration
            : new ConfigurationBuilder()
                .AddConfiguration(configuration)
                .AddInMemoryCollection(new Dictionary<string, string?> { [ContentSecurityPolicyKey] = contentSecurityPolicy })
                .Build();
        services.AddSecurityHeaders(source);
        return services;
    }
}
