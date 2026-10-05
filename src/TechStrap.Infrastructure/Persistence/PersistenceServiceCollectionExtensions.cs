using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TechStrap.Application.Persistence;
using TechStrap.Application.Requesters;
using TechStrap.Infrastructure.Persistence.Repositories;

namespace TechStrap.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TechStrapDbContext"/>, the "database" readiness check, the unit of work and the repositories
    /// (all scoped, so one request or one worker iteration shares one context and one transaction). The connection string is
    /// resolved when the context is first created, so test overrides apply; outside Development a blank one fails the start (<see cref="DatabaseConnectionOptions"/>).
    /// Used by the Api and the Worker only; Admin and Portal never touch the database.
    /// </summary>
    public static IServiceCollection AddTechStrapPersistence(this IServiceCollection services)
    {
        services.AddDbContext<TechStrapDbContext>((provider, options) =>
            TechStrapDatabase.Configure(
                options,
                provider.GetRequiredService<IConfiguration>().GetConnectionString(TechStrapDatabase.ConnectionStringName)));

        services.AddOptions<DatabaseConnectionOptions>()
            .Configure<IConfiguration>((options, configuration) =>
                options.ConnectionString = configuration.GetConnectionString(TechStrapDatabase.ConnectionStringName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DatabaseConnectionOptions>, DatabaseConnectionOptionsValidator>());

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
        services.AddScoped<IIntakeIdempotencyStore, IntakeIdempotencyStore>();
        services.AddScoped<IRequesterErasure, RequesterErasure>();

        return services;
    }
}
