using Microsoft.EntityFrameworkCore;
using Npgsql;
using SyntaxCircus.EntityFrameworkCore.Postgres;
using TechStrap.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

// xunit.v3 assembly fixture: one Postgres 17 container is started for the whole test assembly and
// injected into any test class constructor that asks for PostgresFixture.
[assembly: AssemblyFixture(typeof(TechStrap.Infrastructure.IntegrationTests.PostgresFixture))]

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// Starts one postgres:17 container, migrates a template database once, and hands out a cheap
/// per-test copy of it (CREATE DATABASE ... TEMPLATE). Requires Docker.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string PostgresImage = "postgres:17";
    private const string TemplateDatabase = "techstrap_template";
    private const string MaintenanceDatabase = "postgres";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(PostgresImage).WithCommand("-c", "max_connections=300").Build();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        await ExecuteAdminAsync($"CREATE DATABASE \"{TemplateDatabase}\"");

        var options = new DbContextOptionsBuilder<TechStrapDbContext>();
        TechStrapDatabase.Configure(options, ConnectionStringFor(TemplateDatabase));
        await using var context = new TechStrapDbContext(options.Options);
        await context.MigrateWithAdvisoryLockAsync(TechStrapDatabase.MigrationLockKey);

        // CREATE DATABASE ... TEMPLATE needs the template to have no open connections, and pooled ones stay open.
        NpgsqlConnection.ClearAllPools();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = database,
            // Pooling keeps sockets alive instead of leaving thousands in TIME_WAIT on Windows.
            Pooling = true,
            MaxPoolSize = 10,
            ConnectionIdleLifetime = 5,
            ConnectionPruningInterval = 1,
        }.ConnectionString;

    /// <summary>Creates an isolated database. <paramref name="migrated"/> copies the migrated template; otherwise it is empty.</summary>
    public async Task<TestDatabase> CreateDatabaseAsync(bool migrated = true)
    {
        var name = $"t_{Guid.NewGuid():N}";
        var template = migrated ? $" TEMPLATE \"{TemplateDatabase}\"" : string.Empty;
        await ExecuteAdminAsync($"CREATE DATABASE \"{name}\"{template}");
        return new TestDatabase(name, ConnectionStringFor(name), DropDatabaseAsync);
    }

    private Task DropDatabaseAsync(string name) => ExecuteAdminAsync($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionStringFor(MaintenanceDatabase));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>One isolated database for one test. Disposing drops it.</summary>
public sealed class TestDatabase(string name, string connectionString, Func<string, Task> drop) : IAsyncDisposable
{
    public string Name { get; } = name;

    public string ConnectionString { get; } = connectionString;

    public TechStrapDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TechStrapDbContext>();
        TechStrapDatabase.Configure(options, ConnectionString);
        return new TechStrapDbContext(options.Options);
    }

    public async ValueTask DisposeAsync() => await drop(Name);
}
