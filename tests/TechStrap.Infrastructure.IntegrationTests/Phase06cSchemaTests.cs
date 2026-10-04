using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Outbox;
using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class Phase06cSchemaTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public void A_deleted_parent_ticket_unlinks_its_follow_ups()
    {
        var foreignKey = ModelInspector.Table("tickets").GetForeignKeys()
            .Single(fk => fk.Properties.Single().GetColumnName() == "parent_ticket_id");

        foreignKey.DeleteBehavior.ShouldBe(DeleteBehavior.SetNull);
    }

    [Fact]
    public void The_outbox_has_a_kind_address_created_at_index()
    {
        var index = ModelInspector.Table("email_outbox").GetIndexes()
            .Single(i => i.GetDatabaseName() == "ix_email_outbox_kind_to_address_created_at");

        index.Properties.Select(p => p.GetColumnName()).ShouldBe(["kind", "to_address", "created_at"]);
        index.IsUnique.ShouldBeFalse();
        index.GetFilter().ShouldBeNull();
    }

    [Fact]
    public void The_outbox_has_a_partial_created_at_index_for_the_finished_retention_sweep()
    {
        var index = ModelInspector.Table("email_outbox").GetIndexes()
            .Single(i => i.GetDatabaseName() == "ix_email_outbox_created_at_when_finished");

        index.Properties.Select(p => p.GetColumnName()).ShouldBe(["created_at"]);
        index.IsUnique.ShouldBeFalse();
        index.GetFilter().ShouldBe("status IN ('Sent','Discarded')");
    }

    [Fact]
    public async Task The_finished_retention_query_can_use_the_partial_index()
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await using (var off = new NpgsqlCommand("SET LOCAL enable_seqscan = off", connection, transaction)) { await off.ExecuteNonQueryAsync(Ct); }
        await using var explain = new NpgsqlCommand(
            "EXPLAIN SELECT id FROM email_outbox WHERE status IN ('Sent', 'Discarded') AND created_at < now() - interval '30 days' ORDER BY created_at LIMIT 100",
            connection, transaction);
        var plan = new List<string>();
        await using var reader = await explain.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct)) { plan.Add(reader.GetString(0)); }
        string.Join('\n', plan).ShouldContain("ix_email_outbox_created_at_when_finished");
    }

    [Fact]
    public async Task Deleting_a_parent_leaves_the_follow_up_with_a_null_parent_and_its_history()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var parent = await scenario.CreateTicketAsync();
        await scenario.UpdateAsync(parent.Id, t =>
        {
            t.ChangeStatus(TicketStatus.Solved, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
            t.ChangeStatus(TicketStatus.Closed, Actor.System, host.Clock).IsSuccess.ShouldBeTrue();
        });
        Guid followUpId = default;
        (await host.CommitAsync(async sp =>
        {
            var repository = sp.GetRequiredService<ITicketRepository>();
            var loaded = (await repository.GetByIdAsync(parent.Id, Ct))!;
            var number = (await sp.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(loaded.ProductId, Ct)).Value;
            var followUp = loaded.CreateFollowUp(number, host.Clock).Value;
            followUp.AddCustomerReply(scenario.Requester.Id, "<p>again</p>", host.Clock).IsSuccess.ShouldBeTrue();
            repository.Update(loaded);
            repository.Add(followUp);
            followUpId = followUp.Id;
        })).IsSuccess.ShouldBeTrue();

        await ExecuteAsync($"DELETE FROM tickets WHERE id = '{parent.Id}'");

        (await ScalarAsync($"SELECT count(*) FROM tickets WHERE id = '{parent.Id}'")).ShouldBe(0);
        (await ScalarAsync($"SELECT count(*) FROM tickets WHERE id = '{followUpId}' AND parent_ticket_id IS NULL")).ShouldBe(1);
        (await ScalarAsync($"SELECT count(*) FROM ticket_events WHERE ticket_id = '{followUpId}' AND type = 'Created' AND payload->>'parentTicketId' = '{parent.Id}'")).ShouldBe(1);
    }

    [Fact]
    public async Task The_per_address_count_is_still_correct_and_can_use_the_new_index()
    {
        await using var host = new PersistenceTestHost(Database);
        (await host.CommitAsync(sp =>
        {
            var outbox = sp.GetRequiredService<IEmailOutbox>();
            outbox.Enqueue(EmailOutboxItem.Enqueue("access-links", "Ann@Example.com", "{}", null, null, host.Clock).Value);
            outbox.Enqueue(EmailOutboxItem.Enqueue("access-links", "bob@example.com", "{}", null, null, host.Clock).Value);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        var since = host.Clock.GetUtcNow().AddHours(-1);

        (await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().CountRecentAsync("access-links", "ann@example.com", since, Ct))).ShouldBe(1);

        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await using (var off = new NpgsqlCommand("SET LOCAL enable_seqscan = off", connection, transaction)) { await off.ExecuteNonQueryAsync(Ct); }
        await using var explain = new NpgsqlCommand(
            "EXPLAIN SELECT count(*) FROM email_outbox WHERE kind = 'access-links' AND to_address = 'ann@example.com' AND created_at >= now() - interval '1 hour'",
            connection, transaction);
        var plan = new List<string>();
        await using var reader = await explain.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct)) { plan.Add(reader.GetString(0)); }
        string.Join('\n', plan).ShouldContain("ix_email_outbox_kind_to_address_created_at");
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(Ct);
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }
}
