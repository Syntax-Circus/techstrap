using System.Net;
using Npgsql;
using TechStrap.Api.Startup;

namespace TechStrap.Api.Tests;

public sealed class ApiMigrationOnStartupTests(TestPostgres postgres)
{
    [Fact]
    public async Task Starting_the_api_migrates_an_empty_database_to_the_Initial_migration()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new ApiFactory(settings: new Dictionary<string, string?>
        {
            ["ConnectionStrings:TechStrap"] = connectionString,
            [ApiStartupTasks.MigrateOnStartupKey] = "true",
        });

        using var client = factory.CreateClient();

        var ready = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        ready.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\"";
        var applied = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            applied.Add(reader.GetString(0));
        }

        applied.Count.ShouldBe(1);
        applied[0].ShouldEndWith("_Initial");
    }
}
