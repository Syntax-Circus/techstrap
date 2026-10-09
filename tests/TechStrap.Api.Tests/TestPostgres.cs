using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(TechStrap.Api.Tests.TestPostgres))]

namespace TechStrap.Api.Tests;

/// <summary>
/// One postgres:17 container for the Api.Tests assembly. Deliberately small: repository-level tests
/// use the richer PostgresFixture in TechStrap.Infrastructure.IntegrationTests. Requires Docker.
/// </summary>
public sealed class TestPostgres : IAsyncLifetime
{
    private const string MaintenanceDatabase = "postgres";

    /// <summary>
    /// postgres:17 from Docker Hub unless TECHSTRAP_TEST_POSTGRES_IMAGE names another reference. CI names the official image's mirror on a
    /// registry GitHub's runners can always reach (docs/development/RELEASING.md, "Docker Hub and the tests"); local runs keep the default.
    /// </summary>
    public static string PostgresImage { get; } =
        Environment.GetEnvironmentVariable("TECHSTRAP_TEST_POSTGRES_IMAGE") is { Length: > 0 } image ? image : "postgres:17";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(PostgresImage).WithCommand("-c", "max_connections=300").Build();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates a new empty database and returns a connection string for it.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"t_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(ConnectionStringFor(MaintenanceDatabase));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{name}\"";
        await command.ExecuteNonQueryAsync();
        return ConnectionStringFor(name);
    }

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
}
