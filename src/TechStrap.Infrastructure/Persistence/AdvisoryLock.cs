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
        var connection = new NpgsqlConnection(connectionString);
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
            // Pooled connections keep their session, so the lock must be released explicitly.
            await using var command = _connection.CreateCommand();
            command.CommandText = "SELECT pg_advisory_unlock(@key)";
            command.Parameters.AddWithValue("key", _key);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
        finally
        {
            await _connection.DisposeAsync();
        }
    }
}
