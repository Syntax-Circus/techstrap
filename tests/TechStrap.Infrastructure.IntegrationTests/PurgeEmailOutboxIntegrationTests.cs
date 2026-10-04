using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TechStrap.Application.Email;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Outbox;
using TechStrap.Infrastructure.Email;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class PurgeEmailOutboxIntegrationTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private PersistenceTestHost NewHost(int batchSize = 500) => new(Database, configure: services =>
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OutboxRetention:Days"] = "90", ["OutboxRetention:BatchSize"] = batchSize.ToString() })
            .Build();
        services.AddTechStrapOutboxRetention(configuration);
    });

    private async Task<Guid> InsertAsync(string status, DateTimeOffset createdAt, Guid? ticketId = null)
    {
        var id = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(_ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO email_outbox (id, kind, to_address, payload, ticket_id, status, attempts, next_attempt_at, created_at)
            VALUES (@id, 'ticket-confirmation', 'ann@example.com', '{}', @ticket, @status, 5, @created, @created)
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("ticket", (object?)ticketId ?? DBNull.Value);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("created", createdAt);
        await command.ExecuteNonQueryAsync(_ct);
        return id;
    }

    private async Task<HashSet<Guid>> RemainingAsync()
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(_ct);
        await using var command = new NpgsqlCommand("SELECT id FROM email_outbox", connection);
        await using var reader = await command.ExecuteReaderAsync(_ct);
        var ids = new HashSet<Guid>();
        while (await reader.ReadAsync(_ct))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    private async Task<string> StatusAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(_ct);
        await using var command = new NpgsqlCommand("SELECT status FROM email_outbox WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        return (string)(await command.ExecuteScalarAsync(_ct))!;
    }

    private static async Task<PurgeEmailOutboxResult> RunAsync(PersistenceTestHost host)
    {
        await using var scope = host.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IPurgeEmailOutboxHandler>().HandleAsync(_ct)).Value;
    }

    [Fact]
    public async Task Only_finished_rows_older_than_the_window_are_deleted()
    {
        await using var host = NewHost();
        var now = host.Clock.GetUtcNow();
        var sentOld = await InsertAsync("Sent", now.AddDays(-91));
        var discardedOld = await InsertAsync("Discarded", now.AddDays(-91));
        var sentAtCutoff = await InsertAsync("Sent", now.AddDays(-90)); // exactly N days: kept
        var sentRecent = await InsertAsync("Sent", now.AddDays(-89));
        var deadOld = await InsertAsync("DeadLettered", now.AddDays(-400));
        var pendingOld = await InsertAsync("Pending", now.AddDays(-400));
        var sendingOld = await InsertAsync("Sending", now.AddDays(-400));

        var result = await RunAsync(host);

        result.Deleted.ShouldBe(2);
        (await RemainingAsync()).ShouldBe([sentAtCutoff, sentRecent, deadOld, pendingOld, sendingOld], ignoreOrder: true);
        (await RemainingAsync()).ShouldNotContain(sentOld);
        (await RemainingAsync()).ShouldNotContain(discardedOld);
    }

    [Fact]
    public async Task A_row_one_tick_inside_the_window_is_kept_and_one_tick_outside_is_deleted()
    {
        await using var host = NewHost();
        var cutoff = host.Clock.GetUtcNow() - TimeSpan.FromDays(90);
        var inside = await InsertAsync("Sent", cutoff.AddTicks(10)); // 1 microsecond newer than the cutoff
        var outside = await InsertAsync("Sent", cutoff.AddTicks(-10));

        (await RunAsync(host)).Deleted.ShouldBe(1);
        (await RemainingAsync()).ShouldBe([inside]);
        (await RemainingAsync()).ShouldNotContain(outside);
    }

    [Fact]
    public async Task A_large_backlog_is_deleted_one_batch_per_run_and_a_second_run_is_a_no_op_when_empty()
    {
        await using var host = NewHost(batchSize: 2);
        for (var i = 0; i < 5; i++)
        {
            await InsertAsync("Sent", host.Clock.GetUtcNow().AddDays(-200 - i));
        }

        (await RunAsync(host)).Deleted.ShouldBe(2);
        (await RunAsync(host)).Deleted.ShouldBe(2);
        (await RunAsync(host)).Deleted.ShouldBe(1);
        (await RunAsync(host)).Deleted.ShouldBe(0);
    }

    [Fact]
    public async Task A_retried_dead_letter_is_pending_again_and_is_still_not_purged()
    {
        await using var host = NewHost();
        var id = await InsertAsync("DeadLettered", host.Clock.GetUtcNow().AddDays(-400));

        var committed = await host.CommitAsync(async provider =>
        {
            var store = provider.GetRequiredService<IEmailOutboxStore>();
            var item = (await store.GetAsync(id, _ct))!;
            item.Retry(host.Clock).IsSuccess.ShouldBeTrue();
            store.Update(item);
        });
        committed.IsSuccess.ShouldBeTrue();

        (await RunAsync(host)).Deleted.ShouldBe(0);
        (await StatusAsync(id)).ShouldBe(nameof(OutboxStatus.Pending));
    }
}
