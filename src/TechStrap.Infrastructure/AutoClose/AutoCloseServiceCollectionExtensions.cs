using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TechStrap.Application.Tickets.AutoClose;
using TechStrap.Infrastructure.Tickets;

namespace TechStrap.Infrastructure.AutoClose;

public static class AutoCloseServiceCollectionExtensions
{
    /// <summary>Everything the Worker needs to auto-close Solved tickets: the shared, validated options and the handler (D-008, D-037).</summary>
    public static IServiceCollection AddTechStrapAutoClose(this IServiceCollection services, IConfiguration configuration)
    {
        TicketOperationsServiceCollectionExtensions.AddAutoCloseOptions(services, configuration);
        services.TryAddScoped<IAutoCloseSolvedTicketsHandler, AutoCloseSolvedTicketsHandler>();
        return services;
    }
}
