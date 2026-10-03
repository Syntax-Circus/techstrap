using TechStrap.Infrastructure.Persistence;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// Base class for tests that need a real database. Every test gets its own migrated database
/// (a copy of the template), dropped when the test ends, so tests never see each other's rows.
/// </summary>
public abstract class PostgresIntegrationTestBase(PostgresFixture postgres) : IAsyncLifetime
{
    protected PostgresFixture Postgres { get; } = postgres;

    protected TestDatabase? DatabaseOrNull { get; private set; }

    /// <summary>The per-test database. Only valid after <see cref="InitializeAsync"/> succeeded.</summary>
    protected TestDatabase Database => DatabaseOrNull ?? throw new InvalidOperationException("The test database has not been created.");

    public async ValueTask InitializeAsync() => DatabaseOrNull = await Postgres.CreateDatabaseAsync();

    /// <summary>Safe when <see cref="InitializeAsync"/> threw: there is nothing to drop then (PHASE-01 carry-forward).</summary>
    public async ValueTask DisposeAsync()
    {
        if (DatabaseOrNull is not null)
        {
            await DatabaseOrNull.DisposeAsync();
        }
    }

    protected TechStrapDbContext CreateDbContext() => Database.CreateDbContext();
}
