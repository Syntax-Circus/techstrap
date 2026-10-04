using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Outbox;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>The outbox deletions used by the admin ticket delete and by the retention sweep (D-039).</summary>
public sealed class OutboxDeletionTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task<Guid> EnqueueAsync(PersistenceTestHost host, string kind, string to, Guid? ticketId)
    {
        var item = EmailOutboxItem.Enqueue(kind, to, "{}", null, ticketId, host.Clock).Value;
        (await host.CommitAsync(sp =>
        {
            sp.GetRequiredService<IEmailOutbox>().Enqueue(item);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        return item.Id;
    }

    private async Task SetAsync(Guid id, OutboxStatus status, DateTimeOffset createdAt)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE email_outbox SET status = @status, created_at = @createdAt WHERE id = @id";
        command.Parameters.AddWithValue("status", status.ToString());
        command.Parameters.AddWithValue("createdAt", createdAt);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private async Task<long> CountAsync(string sql = "SELECT count(*) FROM email_outbox")
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<(Guid A, Guid B)> ArrangeTicketRowsAsync(PersistenceTestHost host)
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var now = host.Clock.GetUtcNow();
        await EnqueueAsync(host, "k", "x@example.com", a);
        await SetAsync(await EnqueueAsync(host, "k", "x@example.com", a), OutboxStatus.Sent, now);
        await SetAsync(await EnqueueAsync(host, "k", "x@example.com", a), OutboxStatus.DeadLettered, now);
        await EnqueueAsync(host, "k", "x@example.com", b);
        await EnqueueAsync(host, "k", "x@example.com", null);
        return (a, b);
    }

    [Fact]
    public async Task DeleteForTicket_removes_rows_of_every_status_for_that_ticket_only_inside_the_callers_transaction()
    {
        await using var host = new PersistenceTestHost(Database);
        var (a, b) = await ArrangeTicketRowsAsync(host);

        var deleted = 0;
        (await host.CommitAsync(async sp => deleted = await sp.GetRequiredService<IEmailOutboxStore>().DeleteForTicketAsync(a, Ct))).IsSuccess.ShouldBeTrue();

        deleted.ShouldBe(3);
        (await CountAsync()).ShouldBe(2);
        (await CountAsync($"SELECT count(*) FROM email_outbox WHERE ticket_id = '{b}'")).ShouldBe(1);
        (await CountAsync("SELECT count(*) FROM email_outbox WHERE ticket_id IS NULL")).ShouldBe(1);
    }

    [Fact]
    public async Task DeleteForTicket_rolls_back_with_the_unit_of_work()
    {
        await using var host = new PersistenceTestHost(Database);
        var (a, _) = await ArrangeTicketRowsAsync(host);

        await using (var scope = host.CreateScope())
        {
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
            (await scope.ServiceProvider.GetRequiredService<IEmailOutboxStore>().DeleteForTicketAsync(a, Ct)).ShouldBe(3);
        }

        (await CountAsync($"SELECT count(*) FROM email_outbox WHERE ticket_id = '{a}'")).ShouldBe(3);
        (await CountAsync()).ShouldBe(5);
    }

    [Fact]
    public async Task DeleteFinishedBefore_deletes_only_sent_and_discarded_rows_older_than_the_cutoff()
    {
        await using var host = new PersistenceTestHost(Database);
        var cutoff = host.Clock.GetUtcNow();
        await SetAsync(await EnqueueAsync(host, "k", "a@example.com", null), OutboxStatus.Sent, cutoff.AddSeconds(-1));
        await SetAsync(await EnqueueAsync(host, "k", "b@example.com", null), OutboxStatus.Sent, cutoff);
        await SetAsync(await EnqueueAsync(host, "k", "c@example.com", null), OutboxStatus.Discarded, cutoff.AddDays(-1));
        foreach (var status in new[] { OutboxStatus.DeadLettered, OutboxStatus.Pending, OutboxStatus.Sending })
        {
            await SetAsync(await EnqueueAsync(host, "k", "d@example.com", null), status, cutoff.AddDays(-100));
        }

        var deleted = await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().DeleteFinishedBeforeAsync(cutoff, 100, Ct));

        deleted.ShouldBe(2);
        (await CountAsync()).ShouldBe(4);
        (await CountAsync("SELECT count(*) FROM email_outbox WHERE status IN ('DeadLettered', 'Pending', 'Sending')")).ShouldBe(3);
        (await CountAsync("SELECT count(*) FROM email_outbox WHERE status = 'Sent'")).ShouldBe(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task DeleteFinishedBefore_normalises_a_non_positive_or_oversized_batch_size(int batchSize)
    {
        await using var host = new PersistenceTestHost(Database);
        var cutoff = host.Clock.GetUtcNow();
        for (var i = 0; i < 3; i++)
        {
            await SetAsync(await EnqueueAsync(host, "k", "a@example.com", null), OutboxStatus.Sent, cutoff.AddDays(-1 - i));
        }

        var deleted = await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().DeleteFinishedBeforeAsync(cutoff, batchSize, Ct));

        deleted.ShouldBe(3);
        (await CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task DeleteFinishedBefore_honours_the_batch_size()
    {
        await using var host = new PersistenceTestHost(Database);
        var cutoff = host.Clock.GetUtcNow();
        for (var i = 0; i < 3; i++)
        {
            await SetAsync(await EnqueueAsync(host, "k", "a@example.com", null), OutboxStatus.Sent, cutoff.AddDays(-1 - i));
        }

        async Task<int> SweepAsync() => await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().DeleteFinishedBeforeAsync(cutoff, 2, Ct));

        (await SweepAsync()).ShouldBe(2);
        (await SweepAsync()).ShouldBe(1);
        (await SweepAsync()).ShouldBe(0);
    }
}
