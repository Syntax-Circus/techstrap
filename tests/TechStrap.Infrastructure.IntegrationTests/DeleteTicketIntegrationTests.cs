using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using SyntaxCircus.Common;
using SyntaxCircus.Storage;
using TechStrap.Application.Agents;
using TechStrap.Application.Attachments;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tickets;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Knowledge;
using TechStrap.Domain.Outbox;
using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.Attachments;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>The real DeleteTicketRequestHandler against Postgres with real stored files (D-006, D-022, D-039).</summary>
public sealed class DeleteTicketIntegrationTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres), IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "techstrap-delete-ticket-" + Guid.NewGuid().ToString("N"));

    private sealed class StubClaims(AgentClaims claims) : ICurrentAgentClaims
    {
        public AgentClaims? Current { get; } = claims;
    }

    private sealed record Arranged(
        PersistenceTestHost Host, Ticket Parent, Ticket FollowUp, IReadOnlyList<string> Keys, Guid UnrelatedTicketId);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private AttachmentStore Store() =>
        new(new LocalFileStorageProvider(Options.Create(new LocalStorageOptions { RootPath = _root })));

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

    private async Task<StoredAttachment> StoreFileAsync(Guid throwawayTicketId, string name) =>
        (await Store().SaveAsync(throwawayTicketId, new IncomingAttachment(name, "image/png", Png.Length, new MemoryStream(Png)), Ct)).Value;

    private static EmailOutboxItem Mail(PersistenceTestHost host, Guid ticketId) =>
        EmailOutboxItem.Enqueue("TicketConfirmation", "ann@example.com", "{}", null, ticketId, host.Clock).Value;

    private static void Attach(Message message, StoredAttachment file, PersistenceTestHost host) =>
        message.AddAttachment(file.FileName, file.ContentType, file.Size, file.StorageKey, host.Clock).IsSuccess.ShouldBeTrue();

    private async Task<Arranged> ArrangeAsync()
    {
        var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var admin = Agent.Create("oidc|admin", "Ada", "ada@example.com", AgentRole.Admin, host.Clock).Value;
        var tag = Tag.Create("bug", "Bug", "#DC2626", host.Clock).Value;
        var article = KbArticle.Create(scenario.Acme.Id, null, "faq", "FAQ", "summary", "# body", scenario.Agent.Id, host.Clock).Value;
        (await host.CommitAsync(sp =>
        {
            sp.GetRequiredService<IAgentRepository>().Add(admin);
            sp.GetRequiredService<ITagRepository>().Add(tag);
            sp.GetRequiredService<IKbRepository>().AddArticle(article);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();

        var throwaway = Guid.CreateVersion7();
        var files = new[]
        {
            await StoreFileAsync(throwaway, "customer.png"),
            await StoreFileAsync(throwaway, "reply.png"),
            await StoreFileAsync(throwaway, "note.png"),
        };

        var replyId = Guid.Empty;
        var parent = await scenario.CreateTicketAsync("Secret subject", change: ticket =>
        {
            Attach(ticket.PendingMessages[0], files[0], host);
            var reply = ticket.AddAgentReply(scenario.Agent.Id, "<p>Agent reply</p>", host.Clock).Value;
            replyId = reply.Id;
            Attach(reply, files[1], host);
            Attach(ticket.AddInternalNote(scenario.Agent.Id, "internal", host.Clock).Value, files[2], host);
            ticket.AddTag(tag.Id, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
            ticket.ChangeStatus(TicketStatus.Solved, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
            ticket.ChangeStatus(TicketStatus.Closed, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
        });

        Ticket? followUp = null;
        (await host.CommitAsync(async sp =>
        {
            var tickets = sp.GetRequiredService<ITicketRepository>();
            var loaded = (await tickets.GetByIdAsync(parent.Id, Ct))!;
            var number = (await sp.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(scenario.Acme.Id, Ct)).Value;
            followUp = loaded.CreateFollowUp(number, host.Clock).Value;
            followUp.AddCustomerReply(scenario.Requester.Id, "<p>Still broken</p>", host.Clock).IsSuccess.ShouldBeTrue();
            tickets.Update(loaded);
            tickets.Add(followUp);
            tickets.AddAccessToken(TicketAccessToken.Issue(parent.Id, scenario.Requester.Id, "sha256:" + new string('a', 64), host.Clock).Value);
            sp.GetRequiredService<IKbRepository>().AddTicketArticle(new TicketArticle(parent.Id, replyId, article.Id));
        })).IsSuccess.ShouldBeTrue();

        var unrelated = Guid.CreateVersion7();
        var pending = Mail(host, parent.Id);
        var sent = Mail(host, parent.Id);
        var dead = Mail(host, parent.Id);
        (await host.CommitAsync(sp =>
        {
            var outbox = sp.GetRequiredService<IEmailOutbox>();
            outbox.Enqueue(pending);
            outbox.Enqueue(sent);
            outbox.Enqueue(dead);
            outbox.Enqueue(Mail(host, unrelated));
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        await ExecuteAsync($"UPDATE email_outbox SET status = 'Sent' WHERE id = '{sent.Id}'");
        await ExecuteAsync($"UPDATE email_outbox SET status = 'DeadLettered' WHERE id = '{dead.Id}'");

        return new Arranged(host, parent, followUp!, files.Select(file => file.StorageKey).ToList(), unrelated);
    }

    private async Task<Result> DeleteAsync(PersistenceTestHost host, Guid ticketId)
    {
        await using var scope = host.CreateScope();
        var sp = scope.ServiceProvider;
        var handler = new DeleteTicketRequestHandler(
            sp.GetRequiredService<ITicketRepository>(),
            Store(),
            sp.GetRequiredService<IEmailOutboxStore>(),
            sp.GetRequiredService<IAdminEventRepository>(),
            sp.GetRequiredService<IAgentRepository>(),
            new StubClaims(new AgentClaims("oidc|admin", "Ada", "ada@example.com", AgentRole.Admin)),
            sp.GetRequiredService<IUnitOfWork>(),
            host.Clock,
            NullLogger<DeleteTicketRequestHandler>.Instance);
        return await handler.HandleAsync(ticketId, Ct);
    }

    private static readonly string[] ChildTables = ["messages", "attachments", "ticket_events", "ticket_access_tokens", "ticket_tags", "ticket_articles"];

    [Fact]
    public async Task A_ticket_with_everything_attached_is_gone_and_its_follow_up_survives()
    {
        var arranged = await ArrangeAsync();
        await using var host = arranged.Host;
        var (parent, followUp, keys, unrelated) = (arranged.Parent, arranged.FollowUp, arranged.Keys, arranged.UnrelatedTicketId);
        keys.ShouldAllBe(key => File.Exists(Path.Combine(_root, key)));
        foreach (var table in ChildTables)
        {
            (await CountAsync($"SELECT count(*) FROM {table} WHERE ticket_id = '{parent.Id}'")).ShouldBeGreaterThan(0, table);
        }

        (await CountAsync($"SELECT count(*) FROM email_outbox WHERE ticket_id = '{parent.Id}'")).ShouldBe(3);

        var result = await DeleteAsync(host, parent.Id);

        result.IsSuccess.ShouldBeTrue();
        foreach (var table in ChildTables)
        {
            (await CountAsync($"SELECT count(*) FROM {table} WHERE ticket_id = '{parent.Id}'")).ShouldBe(0, table);
        }

        (await CountAsync($"SELECT count(*) FROM tickets WHERE id = '{parent.Id}'")).ShouldBe(0);
        keys.ShouldAllBe(key => !File.Exists(Path.Combine(_root, key)));
        (await CountAsync($"SELECT count(*) FROM email_outbox WHERE ticket_id = '{parent.Id}'")).ShouldBe(0);
        (await CountAsync($"SELECT count(*) FROM email_outbox WHERE ticket_id = '{unrelated}'")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM tickets WHERE id = '{followUp.Id}' AND parent_ticket_id IS NULL")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM ticket_events WHERE ticket_id = '{followUp.Id}' AND type = 'Created' AND payload->>'parentTicketId' = '{parent.Id}'")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM messages WHERE ticket_id = '{followUp.Id}'")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM admin_events WHERE type = 'TicketDeleted' AND subject_id = '{parent.Id}' AND (payload->>'messageCount')::int = 3 AND (payload->>'attachmentCount')::int = 3")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM admin_events WHERE payload::text ILIKE '%{parent.Subject}%'")).ShouldBe(0);
    }

    [Fact]
    public async Task Deleting_an_unknown_ticket_is_not_found_and_changes_nothing()
    {
        var arranged = await ArrangeAsync();
        await using var host = arranged.Host;
        var tickets = await CountAsync("SELECT count(*) FROM tickets");
        var outbox = await CountAsync("SELECT count(*) FROM email_outbox");

        var result = await DeleteAsync(host, Guid.CreateVersion7());

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("ticket-not-found");
        (await CountAsync("SELECT count(*) FROM tickets")).ShouldBe(tickets);
        (await CountAsync("SELECT count(*) FROM email_outbox")).ShouldBe(outbox);
        (await CountAsync("SELECT count(*) FROM admin_events WHERE type = 'TicketDeleted'")).ShouldBe(0);
        arranged.Keys.ShouldAllBe(key => File.Exists(Path.Combine(_root, key)));
    }

    [Fact]
    public async Task Deleting_a_ticket_with_an_attachment_whose_file_is_already_missing_still_succeeds()
    {
        var arranged = await ArrangeAsync();
        await using var host = arranged.Host;
        File.Delete(Path.Combine(_root, arranged.Keys[0]));

        var result = await DeleteAsync(host, arranged.Parent.Id);

        result.IsSuccess.ShouldBeTrue();
        (await CountAsync($"SELECT count(*) FROM tickets WHERE id = '{arranged.Parent.Id}'")).ShouldBe(0);
        arranged.Keys.ShouldAllBe(key => !File.Exists(Path.Combine(_root, key)));
    }
}
