using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tickets.AutoClose;
using TechStrap.Application.Tickets.Notifications;

namespace TechStrap.Infrastructure.Tickets;

public static class TicketOperationsServiceCollectionExtensions
{
    /// <summary>Agent ticket operations: the optional Admin link base URL (validated on start), the auto-close options and the notification planner.</summary>
    public static IServiceCollection AddTechStrapTicketOperations(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AdminLinkOptions>()
            .Configure(options => options.PublicUrl = configuration[AdminLinkOptions.PublicUrlKey]?.Trim())
            .Validate(
                options => AdminLinkOptions.IsValidBase(options.PublicUrl),
                $"{AdminLinkOptions.PublicUrlKey} must be an absolute http or https URL without a query or fragment when set.")
            .ValidateOnStart();
        AddAutoCloseOptions(services, configuration);
        services.TryAddScoped<ITicketNotificationPlanner, TicketNotificationPlanner>();
        return services;
    }

    /// <summary>Binds and validates <see cref="AutoCloseOptions"/>; shared by the Api and the Worker.</summary>
    internal static void AddAutoCloseOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AutoCloseOptions>()
            .Configure(options =>
            {
                configuration.GetSection(AutoCloseOptions.SectionName).Bind(options);
                if (int.TryParse(configuration[AutoCloseOptions.DaysKey], out var days))
                {
                    options.Days = days;
                }
            })
            .Validate(o => string.IsNullOrWhiteSpace(configuration[AutoCloseOptions.DaysKey]) || int.TryParse(configuration[AutoCloseOptions.DaysKey], out int _), $"{AutoCloseOptions.DaysKey} must be a whole number.")
            .Validate(o => o.Days is >= 1 and <= 365, $"{AutoCloseOptions.DaysKey} must be between 1 and 365.")
            .Validate(o => o.IntervalMinutes is >= 1 and <= 1440, "AutoClose:IntervalMinutes must be between 1 and 1440.")
            .Validate(o => o.BatchSize is >= 1 and <= Paging.MaxBatchSize, $"AutoClose:BatchSize must be between 1 and {Paging.MaxBatchSize}.")
            .ValidateOnStart();
    }
}
