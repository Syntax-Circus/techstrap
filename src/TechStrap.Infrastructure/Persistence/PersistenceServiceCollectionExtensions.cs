using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TechStrap.Application.Persistence;
using TechStrap.Infrastructure.Persistence.Repositories;

namespace TechStrap.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TechStrapDbContext"/>, the "database" readiness check, the unit of work and the repositories
    /// (all scoped, so one request or one worker iteration shares one context and one transaction). The connection string is
    /// resolved when the context is first created, so test overrides apply.
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

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IAdminEventRepository, AdminEventRepository>();
        services.AddScoped<IAgentRepository, AgentRepository>();
        services.AddScoped<IRequesterRepository, RequesterRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<ITicketNumberAllocator, TicketNumberAllocator>();
        services.AddScoped<ITicketRepository, TicketRepository>();
        services.AddScoped<IKbRepository, KbRepository>();
        services.AddScoped<IEmailOutbox, EmailOutbox>();
        services.AddScoped<IEmailOutboxStore, EmailOutboxStore>();

        return services;
    }
}
