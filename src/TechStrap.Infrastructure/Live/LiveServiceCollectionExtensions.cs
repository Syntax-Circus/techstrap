using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TechStrap.Application.Live;

namespace TechStrap.Infrastructure.Live;

public static class LiveServiceCollectionExtensions
{
    /// <summary>
    /// The in-memory presence store, one for the process (D-007: a single Api instance). The Api registers it; the Worker never does, because presence is not
    /// the Worker's business. Needs a <see cref="TimeProvider"/>, which <c>AddTechStrapPersistence</c> registers.
    /// </summary>
    public static IServiceCollection AddTechStrapPresence(this IServiceCollection services)
    {
        services.TryAddSingleton<ITicketPresenceStore, InMemoryTicketPresenceStore>();
        return services;
    }

    /// <summary>
    /// The Api's listener on the NOTIFY channel (a hosted service). It needs <c>IRelayTicketChangeHandler</c> (registered with the other handlers) and a broadcaster that reaches the
    /// hub. The Worker never calls this, and nothing here ever notifies, so a change cannot echo.
    /// </summary>
    public static IServiceCollection AddTechStrapTicketChangeListener(this IServiceCollection services)
    {
        services.AddTechStrapMetrics();
        services.AddHostedService<TicketChangeListener>();
        return services;
    }

    /// <summary>
    /// <see cref="TechStrapMetrics"/> as a singleton. The Api and the Worker also pass <see cref="TechStrapMetrics.MeterName"/> to <c>AddSyntaxCircusObservability</c>; without that the instruments
    /// exist but no exporter ever reads them.
    /// </summary>
    public static IServiceCollection AddTechStrapMetrics(this IServiceCollection services)
    {
        services.TryAddSingleton<TechStrapMetrics>();
        return services;
    }

    /// <summary>The Worker's broadcaster: a Postgres NOTIFY after each commit, for the Api's listener. Call it after <c>AddTechStrapPersistence</c>, whose null broadcaster it replaces.</summary>
    public static IServiceCollection AddTechStrapNotifyBroadcaster(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<ITicketChangeBroadcaster, PgNotifyTicketChangeBroadcaster>());
        return services;
    }
}
