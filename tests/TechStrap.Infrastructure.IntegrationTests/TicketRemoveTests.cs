using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>ITicketRepository.Remove: a tracked ticket delete, with the database cascading everything attached (D-039).</summary>
public sealed class TicketRemoveTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private async Task<long> CountAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    [Fact]
    public async Task Removing_a_loaded_ticket_deletes_it_and_the_database_cascades_everything_attached()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var tag = Tag.Create("bug", "Bug", "#DC2626", host.Clock).Value;
        (await host.CommitAsync(sp =>
        {
            sp.GetRequiredService<ITagRepository>().Add(tag);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        var ticket = await scenario.CreateTicketAsync(change: t =>
        {
            t.AddTag(tag.Id, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
            t.PendingMessages[0].AddAttachment("a.png", "image/png", 5, "attachments/k/1", host.Clock).IsSuccess.ShouldBeTrue();
        });
        (await host.CommitAsync(sp =>
        {
            sp.GetRequiredService<ITicketRepository>().AddAccessToken(TicketAccessToken.Issue(ticket.Id, scenario.Requester.Id, "hash-1", host.Clock).Value);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();

        var result = await host.CommitAsync(async sp =>
        {
            var repository = sp.GetRequiredService<ITicketRepository>();
            repository.Remove((await repository.GetByIdAsync(ticket.Id, Ct))!);
        });

        result.IsSuccess.ShouldBeTrue();
        foreach (var table in new[] { "tickets", "messages", "attachments", "ticket_events", "ticket_tags", "ticket_access_tokens" })
        {
            var column = table == "tickets" ? "id" : "ticket_id";
            (await CountAsync($"SELECT count(*) FROM {table} WHERE {column} = '{ticket.Id}'")).ShouldBe(0, table);
        }

        (await CountAsync($"SELECT count(*) FROM tags WHERE id = '{tag.Id}'")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM requesters WHERE id = '{scenario.Requester.Id}'")).ShouldBe(1);
    }

    [Fact]
    public async Task Removing_a_ticket_that_this_scope_never_loaded_is_a_programming_error()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();

        await Should.ThrowAsync<InvalidOperationException>(() => host.CommitAsync(sp =>
        {
            sp.GetRequiredService<ITicketRepository>().Remove(ticket);
            return Task.CompletedTask;
        }));
    }

    [Fact]
    public async Task Rolling_back_the_scope_keeps_the_ticket()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();

        await using (var scope = host.CreateScope())
        {
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
            var repository = scope.ServiceProvider.GetRequiredService<ITicketRepository>();
            repository.Remove((await repository.GetByIdAsync(ticket.Id, Ct))!);
        }

        (await CountAsync($"SELECT count(*) FROM tickets WHERE id = '{ticket.Id}'")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM messages WHERE ticket_id = '{ticket.Id}'")).ShouldBe(1);
    }
}
