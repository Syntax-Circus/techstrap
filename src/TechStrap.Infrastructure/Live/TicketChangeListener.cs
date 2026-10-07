using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;
using TechStrap.Infrastructure.Persistence;

namespace TechStrap.Infrastructure.Live;

/// <summary>
/// The Api's end of the NOTIFY relay (D-007, D-018): one dedicated, unpooled connection that does <c>LISTEN</c> on <see cref="TicketChangeNotify.Channel"/> and hands each payload to
/// <see cref="IRelayTicketChangeHandler"/>, which validates it. A payload that is bad is dropped by the handler and the loop goes on. When the connection is lost it reconnects with an
/// exponential backoff (<see cref="BackoffFor"/>) and, once it is listening again, relays a <c>Resync</c>, because notifications sent in the gap are gone for good. It only ever listens: it
/// never notifies, so a change cannot echo back to itself. Stopping the host cancels the wait and closes the connection. Needs a direct Postgres connection (not a transaction-pooling proxy).
/// Nothing the loop logs carries a payload, a connection string or an exception message.
/// </summary>
internal sealed class TicketChangeListener(
    IOptions<DatabaseConnectionOptions> database,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<TicketChangeListener> logger) : BackgroundService
{
    /// <summary>A test seam: runs on the listener's connection right after LISTEN (and the Resync), before the first wait, so a test can have a notification arrive in that window.</summary>
    internal Func<NpgsqlConnection, CancellationToken, Task>? AfterListening { get; set; }

    /// <summary>The longest one relay (the handler and the hub behind it) may take, so a stuck hub cannot hold the loop for good.</summary>
    private static readonly TimeSpan RelayTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The delay before reconnect number <paramref name="failuresInARow"/> (0 is the first): the initial delay doubled each time, never over the maximum.</summary>
    internal static TimeSpan BackoffFor(int failuresInARow)
    {
        var doublings = Math.Min(Math.Max(failuresInARow, 0), 16);
        var delay = TimeSpan.FromTicks(TicketChangeNotify.ReconnectInitialDelay.Ticks << doublings);
        return delay > TicketChangeNotify.ReconnectMaxDelay ? TicketChangeNotify.ReconnectMaxDelay : delay;
    }

    /// <summary>The configured connection with its own settings for a long-lived listener: no pool, a name and a keepalive.</summary>
    internal static string ListenerConnectionString(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
            ApplicationName = TicketChangeNotify.ListenerApplicationName,
            KeepAlive = TicketChangeNotify.KeepAliveSeconds,
            TcpKeepAlive = true,
        }.ConnectionString;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(database.Value.ConnectionString))
        {
            logger.LogInformation("The ticket change listener is off: no database connection string is configured.");
            return;
        }

        var connectionString = ListenerConnectionString(database.Value.ConnectionString);
        var failuresInARow = 0;
        var listenedBefore = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            string? lost = null;
            var listening = false;
            try
            {
                await ListenAsync(
                    connectionString,
                    resync: listenedBefore || failuresInARow > 0,
                    onListening: () =>
                    {
                        failuresInARow = 0;
                        listenedBefore = true;
                        listening = true;
                    },
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                lost = exception.GetType().Name;
            }

            // Worked out after the attempt, so a connection that was good for a while and is then lost starts again from the first delay.
            var delay = BackoffFor(failuresInARow++);
            if (lost is not null)
            {
                logger.LogWarning(
                    listening
                        ? "The ticket change listener lost its connection ({ExceptionType}); it will reconnect in {DelaySeconds}s."
                        : "The ticket change listener could not connect ({ExceptionType}); it will try again in {DelaySeconds}s.",
                    lost,
                    delay.TotalSeconds);
            }

            try
            {
                await Task.Delay(delay, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ListenAsync(string connectionString, bool resync, Action onListening, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        // Npgsql raises Notification on whichever thread reads the connection: a keepalive timer, or the LISTEN command itself (Postgres 15 and later can deliver one there).
        var received = new ConcurrentQueue<string>();
        connection.Notification += (_, notification) => received.Enqueue(notification.Payload);
        await using (var listen = new NpgsqlCommand($"LISTEN {TicketChangeNotify.Channel}", connection))
        {
            await listen.ExecuteNonQueryAsync(cancellationToken);
        }

        onListening();
        if (resync)
        {
            // Whatever was sent while the connection was down is lost, so tell the clients to reload everything.
            await RelayAsync(JsonSerializer.Serialize(TicketChange.Resync(Guid.CreateVersion7(clock.GetUtcNow()), clock.GetUtcNow()).ToDto(), JsonSerializerOptions.Web), cancellationToken);
        }

        if (AfterListening is { } afterListening)
        {
            await afterListening(connection, cancellationToken);
        }

        while (true)
        {
            // Drain before waiting, and as a while: a notification can already be queued (it arrived during LISTEN or on a keepalive) and WaitAsync would not return for it until
            // another one came, and several can be queued at once.
            while (received.TryDequeue(out var payload))
            {
                await RelayAsync(payload, cancellationToken);
            }

            await connection.WaitAsync(cancellationToken);
        }
    }

    private async Task RelayAsync(string payload, CancellationToken cancellationToken)
    {
        try
        {
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bounded.CancelAfter(RelayTimeout);
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IRelayTicketChangeHandler>().HandleAsync(new RelayTicketChangeRequest(payload), bounded.Token);
            if (result.IsFailure)
            {
                // The code only: the payload came off a channel anything can write to and is never logged.
                logger.LogWarning("A ticket change from the database was dropped ({ErrorCode}).", result.Errors[0].Code);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Relaying a ticket change from the database failed ({ExceptionType}).", exception.GetType().Name);
        }
    }
}
