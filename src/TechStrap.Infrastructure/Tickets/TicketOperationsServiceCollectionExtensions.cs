using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TechStrap.Application.Tickets.Notifications;

namespace TechStrap.Infrastructure.Tickets;

public static class TicketOperationsServiceCollectionExtensions
{
    /// <summary>Agent ticket operations: the optional Admin link base URL (validated on start) and the notification planner.</summary>
    public static IServiceCollection AddTechStrapTicketOperations(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AdminLinkOptions>()
            .Configure(options => options.PublicUrl = configuration[AdminLinkOptions.PublicUrlKey]?.Trim())
            .Validate(
                options => string.IsNullOrWhiteSpace(options.PublicUrl)
                    || (Uri.TryCreate(options.PublicUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)),
                $"{AdminLinkOptions.PublicUrlKey} must be an absolute http or https URL when set.")
            .ValidateOnStart();
        services.TryAddScoped<ITicketNotificationPlanner, TicketNotificationPlanner>();
        return services;
    }
}
