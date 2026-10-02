using Microsoft.Extensions.Logging;
using TechStrap.Application.Seeding;

namespace TechStrap.Infrastructure.Seeding;

/// <summary>No-op in PHASE-01. Later phases add sample products, agents and tickets here.</summary>
public sealed class DevelopmentDataSeeder(ILogger<DevelopmentDataSeeder> logger) : IDevelopmentDataSeeder
{
    public Task SeedAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Development data seeding requested; no development data is defined yet.");
        return Task.CompletedTask;
    }
}
