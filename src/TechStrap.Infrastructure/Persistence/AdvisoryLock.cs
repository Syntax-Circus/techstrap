using System.Data.Common;
using Npgsql;

namespace TechStrap.Infrastructure.Persistence;

/// <summary>
/// A Postgres session-level advisory lock held on its own connection until disposed. Callers on any instance that ask for the same
/// key wait their turn, so work such as development seeding runs one instance at a time.
/// </summary>
internal sealed class AdvisoryLock : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly long _key;

    private AdvisoryLock(NpgsqlConnection connection, long key)
    {
        _connection = connection;
        _key = key;
    }

    public static async Task<AdvisoryLock> AcquireAsync(string connectionString, long key, CancellationToken cancellationToken)
    {
        // Never pooled: closing an unpooled connection ends the session, which frees the lock even when the explicit unlock cannot run.
        var unpooled = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
        var connection = new NpgsqlConnection(unpooled);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_advisory_lock(@key)";
            command.Parameters.AddWithValue("key", key);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return new AdvisoryLock(connection, key);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            // An explicit unlock is the clean path; closing the unpooled connection below ends the session as the backstop.
            await using var command = _connection.CreateCommand();
            command.CommandText = "SELECT pg_advisory_unlock(@key)";
            command.Parameters.AddWithValue("key", _key);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException)
        {
            // The session is gone or broken, so the lock is already released with it. Swallowed on purpose: this runs from DisposeAsync,
            // and throwing here would mask the error that is already unwinding the caller.
        }
        finally
        {
            await _connection.DisposeAsync();
        }
    }
}
