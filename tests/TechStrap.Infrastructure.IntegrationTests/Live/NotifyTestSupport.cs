using System.Threading.Channels;
using Npgsql;
using TechStrap.Infrastructure.Live;

namespace TechStrap.Infrastructure.IntegrationTests.Live;

/// <summary>Plain SQL helpers for the NOTIFY tests: send a notification, find and kill the listener's backend, wait for a condition without sleeping a fixed time.</summary>
internal static class NotifyTestSupport
{
    /// <summary>A ceiling for waits, never a measurement: a pass returns as soon as the condition holds.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    public static async Task NotifyAsync(string connectionString, string payload)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT pg_notify(@channel, @payload)", connection);
        command.Parameters.AddWithValue("channel", TicketChangeNotify.Channel);
        command.Parameters.AddWithValue("payload", payload);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Several notifications in one transaction: Postgres delivers them together, so the listener sees them in a single wake-up.</summary>
    public static async Task NotifyTogetherAsync(string connectionString, IReadOnlyList<string> payloads)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        foreach (var payload in payloads)
        {
            await using var command = new NpgsqlCommand("SELECT pg_notify(@channel, @payload)", connection, transaction);
            command.Parameters.AddWithValue("channel", TicketChangeNotify.Channel);
            command.Parameters.AddWithValue("payload", payload);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await transaction.CommitAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The listener's backends that are listening: found by the application name the listener gives its connection and by their last statement being the LISTEN (a connection that is open but has not listened yet would lose a notification).</summary>
    public static async Task<IReadOnlyList<int>> ListenerPidsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT pid FROM pg_stat_activity WHERE datname = current_database() AND application_name = '" + TicketChangeNotify.ListenerApplicationName + "' AND query LIKE 'LISTEN %'",
            connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var pids = new List<int>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            pids.Add(reader.GetInt32(0));
        }

        return pids;
    }

    public static async Task TerminateAsync(string connectionString, int pid)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT pg_terminate_backend(@pid)", connection);
        command.Parameters.AddWithValue("pid", pid);
        await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Polls until <paramref name="condition"/> holds. <paramref name="between"/> runs after each miss (for example, to move a fake clock on).</summary>
    public static async Task<T> UntilAsync<T>(Func<Task<T>> read, Func<T, bool> condition, Action? between = null)
    {
        using var timeout = new CancellationTokenSource(Patience);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current.CancellationToken);
        while (true)
        {
            var value = await read();
            if (condition(value))
            {
                return value;
            }

            between?.Invoke();
            await Task.Delay(50, linked.Token);
        }
    }

    public static Task<bool> UntilAsync(Func<bool> condition, Action? between = null) =>
        UntilAsync(() => Task.FromResult(condition()), held => held, between);

    /// <summary>The test's own listener: reads every payload sent on the channel, so a test can see exactly what was notified (and that nothing else was).</summary>
    public sealed class Probe : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;
        private readonly Channel<string> _received = Channel.CreateUnbounded<string>();
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;

        private Probe(NpgsqlConnection connection)
        {
            _connection = connection;
            _connection.Notification += (_, notification) => _received.Writer.TryWrite(notification.Payload);
            _loop = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        await _connection.WaitAsync(_stop.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                }
            });
        }

        public static async Task<Probe> StartAsync(string connectionString)
        {
            var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using (var listen = new NpgsqlCommand($"LISTEN {TicketChangeNotify.Channel}", connection))
            {
                await listen.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            return new Probe(connection);
        }

        public async Task<string> NextAsync()
        {
            using var timeout = new CancellationTokenSource(Patience);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current.CancellationToken);
            return await _received.Reader.ReadAsync(linked.Token);
        }

        public IReadOnlyList<string> Pending()
        {
            var items = new List<string>();
            while (_received.Reader.TryRead(out var item))
            {
                items.Add(item);
            }

            return items;
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            await _loop;
            await _connection.DisposeAsync();
            _stop.Dispose();
        }
    }
}
