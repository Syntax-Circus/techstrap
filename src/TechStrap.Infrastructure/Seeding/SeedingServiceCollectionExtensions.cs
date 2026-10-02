using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Seeding;

namespace TechStrap.Infrastructure.Seeding;

public static class SeedingServiceCollectionExtensions
{
    /// <summary>Registers the development data seeder. Called by the Api composition root only.</summary>
    public static IServiceCollection AddTechStrapDevelopmentSeeding(this IServiceCollection services) =>
        services.AddScoped<IDevelopmentDataSeeder, DevelopmentDataSeeder>();
}
