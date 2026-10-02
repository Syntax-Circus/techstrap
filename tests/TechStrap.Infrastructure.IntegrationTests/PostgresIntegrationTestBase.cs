using TechStrap.Infrastructure.Persistence;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// Base class for tests that need a real database. Every test gets its own migrated database
/// (a copy of the template), dropped when the test ends, so tests never see each other's rows.
/// </summary>
public abstract class PostgresIntegrationTestBase(PostgresFixture postgres) : IAsyncLifetime
{
    protected PostgresFixture Postgres { get; } = postgres;

    protected TestDatabase Database { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Database = await Postgres.CreateDatabaseAsync();

    public async ValueTask DisposeAsync() => await Database.DisposeAsync();

    protected TechStrapDbContext CreateDbContext() => Database.CreateDbContext();
}
