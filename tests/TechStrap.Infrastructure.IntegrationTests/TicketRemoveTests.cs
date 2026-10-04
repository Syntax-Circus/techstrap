using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.Persistence;
using TechStrap.Infrastructure.Persistence.Records;

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

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(Ct);
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
        var articleId = Guid.NewGuid();
        var apiKeyId = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO kb_articles (id, slug, title, body_markdown, status, author_id, created_at, updated_at)
            VALUES ('{articleId}', 'a-1', 'Article', 'body', 'Draft', '{scenario.Agent.Id}', now(), now());
            INSERT INTO ticket_articles (ticket_id, message_id, article_id)
            SELECT ticket_id, id, '{articleId}' FROM messages WHERE ticket_id = '{ticket.Id}';
            INSERT INTO product_api_keys (id, product_id, kind, key_hash, key_prefix, created_at)
            VALUES ('{apiKeyId}', '{scenario.Acme.Id}', 'Trusted', 'h', 'tsk_x', now());
            INSERT INTO intake_idempotency_keys (id, api_key_id, key_hash, ticket_id, response, created_at)
            VALUES (gen_random_uuid(), '{apiKeyId}', 'k', '{ticket.Id}', '[]', now());
            """);
        (await CountAsync($"SELECT count(*) FROM ticket_articles WHERE ticket_id = '{ticket.Id}'")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM intake_idempotency_keys WHERE ticket_id = '{ticket.Id}'")).ShouldBe(1);

        var result = await host.CommitAsync(async sp =>
        {
            var repository = sp.GetRequiredService<ITicketRepository>();
            repository.Remove((await repository.GetByIdAsync(ticket.Id, Ct))!);
        });

        result.IsSuccess.ShouldBeTrue();
        foreach (var table in new[] { "tickets", "messages", "attachments", "ticket_events", "ticket_tags", "ticket_access_tokens", "ticket_articles", "intake_idempotency_keys" })
        {
            var column = table == "tickets" ? "id" : "ticket_id";
            (await CountAsync($"SELECT count(*) FROM {table} WHERE {column} = '{ticket.Id}'")).ShouldBe(0, table);
        }

        (await CountAsync($"SELECT count(*) FROM kb_articles WHERE id = '{articleId}'")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM product_api_keys WHERE id = '{apiKeyId}'")).ShouldBe(1);
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

            // Flush the staged delete into the open transaction (everything a commit does short of committing it).
            var context = scope.ServiceProvider.GetRequiredService<TechStrapDbContext>();
            await context.SaveChangesAsync(Ct);
            (await context.Set<TicketRecord>().AsNoTracking().AnyAsync(t => t.Id == ticket.Id, Ct)).ShouldBeFalse();
        }

        (await CountAsync($"SELECT count(*) FROM tickets WHERE id = '{ticket.Id}'")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM messages WHERE ticket_id = '{ticket.Id}'")).ShouldBe(1);
    }
}
