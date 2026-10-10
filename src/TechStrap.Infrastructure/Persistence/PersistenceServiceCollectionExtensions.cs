using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TechStrap.Application.Live;
using TechStrap.Application.Persistence;
using TechStrap.Application.Requesters;
using TechStrap.Infrastructure.Live;
using TechStrap.Infrastructure.Persistence.Repositories;

namespace TechStrap.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TechStrapDbContext"/>, the "database" readiness check, the unit of work and the repositories
    /// (all scoped, so one request or one worker iteration shares one context and one transaction). The connection string is
    /// resolved when the context is first created, so test overrides apply; outside Development a blank one fails the start (<see cref="DatabaseConnectionOptions"/>).
    /// It also wires the post-commit hook (D-018): two interceptors that publish committed ticket changes to the host's <see cref="ITicketChangeBroadcaster"/>, which is
    /// <see cref="NullTicketChangeBroadcaster"/> until the Api or the Worker replaces it (register theirs after this call, with <c>Replace</c>).
    /// Used by the Api and the Worker only; Admin and Portal never touch the database.
    /// </summary>
    public static IServiceCollection AddTechStrapPersistence(this IServiceCollection services)
    {
        services.TryAddSingleton<ITicketChangeBroadcaster, NullTicketChangeBroadcaster>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<PendingTicketChanges>();
        services.AddSingleton<TicketChangePublisher>();
        services.AddSingleton<TicketChangeCaptureInterceptor>();
        services.AddSingleton<TicketChangePublishingInterceptor>();

        // Configure stays static and DI-free for the tools and tests that call it directly; the hook's interceptors need the container, so they are added here.
        services.AddDbContext<TechStrapDbContext>((provider, options) =>
            TechStrapDatabase.Configure(
                options,
                provider.GetRequiredService<IConfiguration>().GetConnectionString(TechStrapDatabase.ConnectionStringName))
                .AddInterceptors(
                    provider.GetRequiredService<TicketChangeCaptureInterceptor>(),
                    provider.GetRequiredService<TicketChangePublishingInterceptor>()));

        services.AddOptions<DatabaseConnectionOptions>()
            .Configure<IConfiguration>((options, configuration) =>
                options.ConnectionString = configuration.GetConnectionString(TechStrapDatabase.ConnectionStringName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DatabaseConnectionOptions>, DatabaseConnectionOptionsValidator>());

        services.AddHealthChecks()
            .AddCheck<DatabaseReadinessHealthCheck>("database", tags: [TechStrapDatabase.ReadyHealthTag]);

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ISiteSettingsRepository, SiteSettingsRepository>();
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
