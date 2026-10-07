using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;
using TechStrap.Infrastructure.Persistence;

namespace TechStrap.Infrastructure.Live;

/// <summary>
/// The Worker's <see cref="ITicketChangeBroadcaster"/> (D-018): sends a Postgres <c>NOTIFY</c> on <see cref="TicketChangeNotify.Channel"/> for the Api's listener to relay. It runs after the commit, from
/// the post-commit hook, on a pooled connection of its own, so it is separate from the transaction that wrote the change. The payload is the change's JSON (ids and names only, far under
/// the 8,000 bytes Postgres allows). Presence belongs to the Api process, so <see cref="PublishPresenceAsync"/> does nothing.
/// </summary>
internal sealed class PgNotifyTicketChangeBroadcaster(IOptions<DatabaseConnectionOptions> database) : ITicketChangeBroadcaster, IAsyncDisposable
{
    private readonly Lazy<NpgsqlDataSource> _dataSource = new(() =>
        NpgsqlDataSource.Create(database.Value.ConnectionString is { Length: > 0 } connectionString
            ? connectionString
            : throw new InvalidOperationException("No database connection string is configured, so a ticket change cannot be sent.")));

    public async Task PublishAsync(TicketChange change, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(change.ToDto(), JsonSerializerOptions.Web);
        if (Encoding.UTF8.GetByteCount(payload) > TicketLiveLimits.MaxChangePayloadBytes)
        {
            throw new InvalidOperationException("The ticket change is larger than the relay accepts.");
        }

        await using var command = _dataSource.Value.CreateCommand("SELECT pg_notify(@channel, @payload)");
        command.Parameters.AddWithValue("channel", TicketChangeNotify.Channel);
        command.Parameters.AddWithValue("payload", payload);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task PublishPresenceAsync(TicketPresence presence, CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => _dataSource.IsValueCreated ? _dataSource.Value.DisposeAsync() : ValueTask.CompletedTask;
}
