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
                options => AdminLinkOptions.IsValidBase(options.PublicUrl),
                $"{AdminLinkOptions.PublicUrlKey} must be an absolute http or https URL without a query or fragment when set.")
            .ValidateOnStart();
        services.TryAddScoped<ITicketNotificationPlanner, TicketNotificationPlanner>();
        return services;
    }
}
