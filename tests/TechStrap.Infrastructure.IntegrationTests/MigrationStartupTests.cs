using Microsoft.EntityFrameworkCore;
using Npgsql;
using SyntaxCircus.EntityFrameworkCore.Postgres;
using TechStrap.Infrastructure.Persistence;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// Exercises the same call the API makes at startup: MigrateWithAdvisoryLockAsync with the
/// TechStrap lock key, against an empty Postgres 17 database.
/// </summary>
public sealed class MigrationStartupTests(PostgresFixture postgres)
{
    private static readonly TimeSpan LockObservationDelay = TimeSpan.FromSeconds(1.5);

    [Fact]
    public async Task An_empty_database_migrates_to_exactly_the_Initial_migration()
    {
        await using var database = await postgres.CreateDatabaseAsync(migrated: false);
        await using var context = database.CreateDbContext();

        await context.MigrateWithAdvisoryLockAsync(TechStrapDatabase.MigrationLockKey, TestContext.Current.CancellationToken);

        var applied = await ReadHistoryAsync(database);
        applied.Count.ShouldBe(1);
        applied[0].ShouldEndWith("_Initial");
    }

    [Fact]
    public async Task Running_the_migrator_a_second_time_is_a_no_op()
    {
        await using var database = await postgres.CreateDatabaseAsync(migrated: false);
        await using (var first = database.CreateDbContext())
        {
            await first.MigrateWithAdvisoryLockAsync(TechStrapDatabase.MigrationLockKey, TestContext.Current.CancellationToken);
        }

        await using var second = database.CreateDbContext();
        await second.MigrateWithAdvisoryLockAsync(TechStrapDatabase.MigrationLockKey, TestContext.Current.CancellationToken);

        (await ReadHistoryAsync(database)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Two_concurrent_startups_both_succeed_and_apply_the_migration_once()
    {
        await using var database = await postgres.CreateDatabaseAsync(migrated: false);
        await using var first = database.CreateDbContext();
        await using var second = database.CreateDbContext();

        await Task.WhenAll(
            first.MigrateWithAdvisoryLockAsync(TechStrapDatabase.MigrationLockKey, TestContext.Current.CancellationToken),
            second.MigrateWithAdvisoryLockAsync(TechStrapDatabase.MigrationLockKey, TestContext.Current.CancellationToken));

        (await ReadHistoryAsync(database)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_startup_waits_while_another_instance_holds_the_advisory_lock()
    {
        await using var database = await postgres.CreateDatabaseAsync(migrated: false);
        await using var holder = new NpgsqlConnection(database.ConnectionString);
        await holder.OpenAsync(TestContext.Current.CancellationToken);
        await ExecuteAsync(holder, $"SELECT pg_advisory_lock({TechStrapDatabase.MigrationLockKey})");

        await using var context = database.CreateDbContext();
        var migration = context.MigrateWithAdvisoryLockAsync(TechStrapDatabase.MigrationLockKey, TestContext.Current.CancellationToken);

        await Task.Delay(LockObservationDelay, TestContext.Current.CancellationToken);
        migration.IsCompleted.ShouldBeFalse("the migrator must block on the advisory lock");

        await ExecuteAsync(holder, $"SELECT pg_advisory_unlock({TechStrapDatabase.MigrationLockKey})");
        await migration.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        (await ReadHistoryAsync(database)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_model_has_no_changes_that_are_missing_from_the_migrations()
    {
        await using var database = await postgres.CreateDatabaseAsync(migrated: false);
        await using var context = database.CreateDbContext();

        context.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    private static async Task<List<string>> ReadHistoryAsync(TestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"";

        var ids = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
