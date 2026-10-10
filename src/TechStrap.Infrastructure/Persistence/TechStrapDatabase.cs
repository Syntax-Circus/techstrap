using Microsoft.EntityFrameworkCore;
using SyntaxCircus.EntityFrameworkCore.Postgres;

namespace TechStrap.Infrastructure.Persistence;

/// <summary>Database-wide constants and the single place that configures EF options for TechStrap.</summary>
public static class TechStrapDatabase
{
    /// <summary>Name under ConnectionStrings: ConnectionStrings__TechStrap.</summary>
    public const string ConnectionStringName = "TechStrap";

    /// <summary>Postgres advisory lock key that serializes migrate-on-startup across API instances.</summary>
    public const long MigrationLockKey = 6_387_541_208;

    /// <summary>Postgres advisory lock key that serializes development seeding across API instances.</summary>
    public const long SeedLockKey = 6_387_541_209;

    /// <summary>Health check tag that /health/ready selects.</summary>
    public const string ReadyHealthTag = "ready";

    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder, string? connectionString) =>
        builder
            .UseNpgsql(connectionString ?? string.Empty)
            .UseSyntaxCircusSnakeCaseNamingConvention()
            .AddInterceptors(new AppendOnlyEventInterceptor());
}
