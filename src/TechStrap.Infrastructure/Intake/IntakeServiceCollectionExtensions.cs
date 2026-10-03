using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Intake;
using TechStrap.Infrastructure.Attachments;
using TechStrap.Infrastructure.Content;
using TechStrap.Infrastructure.Security;

namespace TechStrap.Infrastructure.Intake;

public static class IntakeServiceCollectionExtensions
{
    /// <summary>
    /// Everything the submit-ticket handler needs besides persistence: the portal link base URL (validated on start), access tokens,
    /// the HTML sanitizer and attachment storage. The handler itself is registered by the host that exposes it.
    /// </summary>
    public static IServiceCollection AddTechStrapIntake(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PortalLinkOptions>()
            .Configure(options => options.PublicUrl = configuration[PortalLinkOptions.PublicUrlKey]?.Trim() ?? string.Empty)
            .Validate(
                options => Uri.TryCreate(options.PublicUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp),
                $"{PortalLinkOptions.PublicUrlKey} must be an absolute http or https URL.")
            .ValidateOnStart();
        services.AddTechStrapSecurity();
        services.AddTechStrapContent();
        services.AddTechStrapAttachments(configuration);
        return services;
    }
}
