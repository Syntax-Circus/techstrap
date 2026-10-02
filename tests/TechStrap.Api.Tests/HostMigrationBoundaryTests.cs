using Npgsql;

namespace TechStrap.Api.Tests;

/// <summary>Only the API migrates the database. Worker, Admin and Portal never do.</summary>
public sealed class HostMigrationBoundaryTests(TestPostgres postgres)
{
    [Fact]
    public async Task Starting_the_worker_leaves_the_database_unmigrated()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new WorkerFactory(settings: new Dictionary<string, string?>
        {
            ["ConnectionStrings:TechStrap"] = connectionString,
            // Even with the API's switch forced on, the Worker must ignore it.
            ["Database:MigrateOnStartup"] = "true",
        });
        using var client = factory.CreateClient();
        await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT to_regclass('\"__EFMigrationsHistory\"')::text";
        var historyTable = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        historyTable.ShouldBeOfType<DBNull>("the Worker must not create the migrations history table");
    }

    [Theory]
    [InlineData(typeof(TechStrap.Admin.Program))]
    [InlineData(typeof(TechStrap.Portal.Program))]
    public void Admin_and_Portal_cannot_reach_the_database_layer(Type hostProgram)
    {
        var referenced = hostProgram.Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        referenced.ShouldNotContain("TechStrap.Infrastructure");
        referenced.ShouldNotContain("TechStrap.Application");
        referenced.ShouldNotContain("Npgsql");
    }
}
