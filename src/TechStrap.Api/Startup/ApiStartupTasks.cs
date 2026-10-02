using SyntaxCircus.EntityFrameworkCore.Postgres;
using TechStrap.Application.Seeding;
using TechStrap.Infrastructure.Persistence;

namespace TechStrap.Api.Startup;

/// <summary>
/// Host startup steps that run once, before the API accepts traffic: migrate the database under a
/// Postgres advisory lock, then (Development only) seed sample data. These are exempt from the
/// handler rule (02-ARCHITECTURE.md section 7.6). Only the API runs them; the Worker, Admin and
/// Portal never migrate.
/// </summary>
public static class ApiStartupTasks
{
    /// <summary>Set to false to skip migration (tests, or a database managed elsewhere).</summary>
    public const string MigrateOnStartupKey = "Database:MigrateOnStartup";

    /// <summary>Set to true, in Development only, to run <see cref="IDevelopmentDataSeeder"/>.</summary>
    public const string SeedDevelopmentDataKey = "TECHSTRAP_SEED_DEV_DATA";

    public static bool ShouldSeedDevelopmentData(IHostEnvironment environment, IConfiguration configuration) =>
        environment.IsDevelopment() && configuration.GetValue<bool>(SeedDevelopmentDataKey);

    public static async Task RunAsync(
        IServiceProvider services,
        IHostEnvironment environment,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("TechStrap.Api.Startup");
        await using var scope = services.CreateAsyncScope();

        if (configuration.GetValue(MigrateOnStartupKey, true))
        {
            logger.LogInformation("Applying database migrations.");
            var database = scope.ServiceProvider.GetRequiredService<TechStrapDbContext>();
            await database.MigrateWithAdvisoryLockAsync(TechStrapDatabase.MigrationLockKey, cancellationToken);
        }
        else
        {
            logger.LogWarning("Database migration on startup is disabled ({Key}=false).", MigrateOnStartupKey);
        }

        if (ShouldSeedDevelopmentData(environment, configuration))
        {
            logger.LogInformation("Seeding development data.");
            var seeder = scope.ServiceProvider.GetRequiredService<IDevelopmentDataSeeder>();
            await seeder.SeedAsync(cancellationToken);
        }
    }
}
