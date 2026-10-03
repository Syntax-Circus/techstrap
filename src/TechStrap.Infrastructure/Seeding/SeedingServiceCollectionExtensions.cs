using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Seeding;
using TechStrap.Infrastructure.Security;

namespace TechStrap.Infrastructure.Seeding;

public static class SeedingServiceCollectionExtensions
{
    /// <summary>Registers the development data seeder. Called by the Api composition root only.</summary>
    public static IServiceCollection AddTechStrapDevelopmentSeeding(this IServiceCollection services)
    {
        services.AddTechStrapSecurity();
        return services.AddScoped<IDevelopmentDataSeeder, DevelopmentDataSeeder>();
    }
}
