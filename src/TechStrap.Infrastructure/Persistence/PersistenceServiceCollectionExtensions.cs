using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TechStrap.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TechStrapDbContext"/> and the "database" readiness check. The connection
    /// string is resolved when the context is first created, so test overrides apply.
    /// Used by the Api and the Worker only; Admin and Portal never touch the database.
    /// </summary>
    public static IServiceCollection AddTechStrapPersistence(this IServiceCollection services)
    {
        services.AddDbContext<TechStrapDbContext>((provider, options) =>
            TechStrapDatabase.Configure(
                options,
                provider.GetRequiredService<IConfiguration>().GetConnectionString(TechStrapDatabase.ConnectionStringName)));

        services.AddHealthChecks()
            .AddCheck<DatabaseReadinessHealthCheck>("database", tags: [TechStrapDatabase.ReadyHealthTag]);

        return services;
    }
}
