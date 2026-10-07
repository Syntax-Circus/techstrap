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
}
