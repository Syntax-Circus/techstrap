using System.Net;
using Npgsql;
using TechStrap.Api.Startup;

namespace TechStrap.Api.Tests;

/// <summary>
/// The compose path in miniature: the real API host, Development, migrate on startup and TECHSTRAP_SEED_DEV_DATA=true. It proves the
/// seeder is registered with everything it needs and that starting the API twice does not duplicate the demo data.
/// </summary>
public sealed class ApiSeedOnStartupTests(TestPostgres postgres)
{
    private static async Task<long> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static ApiFactory StartApi(string connectionString) => new(settings: new Dictionary<string, string?>
    {
        ["ConnectionStrings:TechStrap"] = connectionString,
        [ApiStartupTasks.MigrateOnStartupKey] = "true",
        [ApiStartupTasks.SeedDevelopmentDataKey] = "true",
    });

    [Fact]
    public async Task Starting_the_api_in_development_with_the_seed_flag_creates_demo_data_once()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using (var first = StartApi(connectionString))
        {
            using var client = first.CreateClient();
            (await client.GetAsync("/health/ready", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var tickets = await ScalarAsync(connectionString, "SELECT count(*) FROM tickets");
        (await ScalarAsync(connectionString, "SELECT count(DISTINCT status) FROM tickets")).ShouldBe(5);
        (await ScalarAsync(connectionString, "SELECT count(*) FROM tickets WHERE is_spam")).ShouldBe(1);
        (await ScalarAsync(connectionString, "SELECT count(*) FROM tickets WHERE parent_ticket_id IS NOT NULL")).ShouldBe(1);
        (await ScalarAsync(connectionString, "SELECT count(DISTINCT kind) FROM product_api_keys")).ShouldBe(2);
        (await ScalarAsync(connectionString, "SELECT count(*) FROM kb_articles WHERE status = 'Published'")).ShouldBeGreaterThan(0);

        await using (var second = StartApi(connectionString))
        {
            using var client = second.CreateClient();
            (await client.GetAsync("/health/ready", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await ScalarAsync(connectionString, "SELECT count(*) FROM tickets")).ShouldBe(tickets);
    }
}
