using Npgsql;
using TechStrap.Infrastructure.Persistence;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class AdvisoryLockTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const long Key = 6_387_541_299;

    private string PooledConnectionString() =>
        new NpgsqlConnectionStringBuilder(Database.ConnectionString) { Pooling = true, MaxPoolSize = 10 }.ConnectionString;

    private async Task<bool> TryLockAsync()
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@key)";
        command.Parameters.AddWithValue("key", Key);
        var acquired = (bool)(await command.ExecuteScalarAsync(Ct))!;
        if (acquired)
        {
            command.CommandText = "SELECT pg_advisory_unlock(@key)";
            await command.ExecuteNonQueryAsync(Ct);
        }

        return acquired;
    }

    [Fact]
    public async Task The_lock_is_held_until_disposed_and_free_afterwards()
    {
        var held = await AdvisoryLock.AcquireAsync(PooledConnectionString(), Key, Ct);
        (await TryLockAsync()).ShouldBeFalse();

        await held.DisposeAsync();

        (await TryLockAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task Disposing_after_the_session_was_killed_does_not_throw_and_the_lock_is_free()
    {
        var held = await AdvisoryLock.AcquireAsync(PooledConnectionString(), Key, Ct);
        await using (var admin = new NpgsqlConnection(Database.ConnectionString))
        {
            await admin.OpenAsync(Ct);
            await using var kill = admin.CreateCommand();
            kill.CommandText = """
                SELECT pg_terminate_backend(pid) FROM pg_locks
                WHERE locktype = 'advisory' AND granted AND objid = @low AND classid = @high AND pid <> pg_backend_pid()
                """;
            kill.Parameters.AddWithValue("low", (long)(uint)(Key & 0xFFFFFFFF));
            kill.Parameters.AddWithValue("high", (long)(uint)(Key >> 32));
            await kill.ExecuteNonQueryAsync(Ct);
        }

        // The unlock cannot run on a dead session; that must not surface as an error that masks the caller's own.
        await Should.NotThrowAsync(async () => await held.DisposeAsync());

        (await TryLockAsync()).ShouldBeTrue();
    }
}
