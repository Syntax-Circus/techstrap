using Microsoft.EntityFrameworkCore;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// Two tests share one container (the assembly fixture) but each gets a fresh database. Both create
/// the same table: if databases leaked between tests, the second CREATE TABLE would fail.
/// </summary>
public sealed class PostgresFixtureSmokeTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    [Fact]
    public async Task Runs_against_postgres_17()
    {
        await using var context = CreateDbContext();

        var version = await context.Database.SqlQueryRaw<string>("SELECT version() AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken);

        version.ShouldStartWith("PostgreSQL 17");
    }

    [Fact]
    public async Task First_test_gets_an_isolated_database()
    {
        await using var context = CreateDbContext();

        await context.Database.ExecuteSqlRawAsync("CREATE TABLE isolation_marker (id int)", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Second_test_gets_an_isolated_database()
    {
        await using var context = CreateDbContext();

        await context.Database.ExecuteSqlRawAsync("CREATE TABLE isolation_marker (id int)", TestContext.Current.CancellationToken);
    }
}
