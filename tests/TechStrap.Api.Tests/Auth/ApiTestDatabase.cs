using Npgsql;
using TechStrap.Api.Startup;

namespace TechStrap.Api.Tests.Auth;

/// <summary>A fresh database that the Api migrates on start, plus small SQL helpers for arranging and asserting rows.</summary>
public sealed class ApiTestDatabase
{
    private ApiTestDatabase(string connectionString) => ConnectionString = connectionString;

    public string ConnectionString { get; }

    public IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["ConnectionStrings:TechStrap"] = ConnectionString,
        [ApiStartupTasks.MigrateOnStartupKey] = "true",
    };

    public static async Task<ApiTestDatabase> CreateAsync(TestPostgres postgres) => new(await postgres.CreateDatabaseAsync());

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}
