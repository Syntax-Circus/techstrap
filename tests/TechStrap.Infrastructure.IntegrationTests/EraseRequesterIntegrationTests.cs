using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using SyntaxCircus.Common;
using SyntaxCircus.Storage;
using TechStrap.Application.Agents;
using TechStrap.Application.Attachments;
using TechStrap.Application.Persistence;
using TechStrap.Application.Requesters;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Outbox;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.Attachments;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>The real EraseRequesterRequestHandler against Postgres with real stored files (D-006, D-022, D-039).</summary>
public sealed class EraseRequesterIntegrationTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres), IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
    private static readonly string[] Needles = ["pat@example.com", "Pat Rivera", "canary-7f3a", "subject-canary-91c2", "pat-passport", "ext-canary-pat"];

    private static readonly string[] ExpectedLocations =
    [
        "attachments.file_name", "email_outbox.payload", "email_outbox.to_address", "messages.body", "messages.search_vector",
        "requesters.email", "requesters.external_user_ref", "requesters.name", "tickets.custom_fields", "tickets.metadata",
        "tickets.search_vector", "tickets.subject",
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "techstrap-erase-requester-" + Guid.NewGuid().ToString("N"));

    private sealed class StubClaims(AgentClaims claims) : ICurrentAgentClaims
    {
        public AgentClaims? Current { get; } = claims;
    }

    private sealed record Arranged(
        PersistenceTestHost Host, Guid PatId, Guid TicketAId, Guid TicketBId, Guid BobId, Guid BobTicketId,
        IReadOnlyList<string> StoredKeysOfPat, string AgentFileKey, string AgentReplyBody, string InternalNoteBody);

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
        return Convert.ToInt64(await command.ExecuteScalarAsync(Ct));
    }

    private async Task<string> TextAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(Ct)) ?? string.Empty;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(Ct);
    }

    private async Task<StoredAttachment> StoreFileAsync(string name) =>
        (await Store().SaveAsync(Guid.CreateVersion7(), new IncomingAttachment(name, "image/png", Png.Length, new MemoryStream(Png)), Ct)).Value;

    private static void Attach(Message message, StoredAttachment file, PersistenceTestHost host) =>
        message.AddAttachment(file.FileName, file.ContentType, file.Size, file.StorageKey, host.Clock).IsSuccess.ShouldBeTrue();

    private static EmailOutboxItem Mail(PersistenceTestHost host, string kind, string to, string payload, Guid? ticketId) =>
        EmailOutboxItem.Enqueue(kind, to, payload, null, ticketId, host.Clock).Value;

    private async Task<Arranged> ArrangeAsync()
    {
        var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var admin = Agent.Create("oidc|admin", "Ada", "ada@example.com", AgentRole.Admin, host.Clock).Value;
        var pat = Requester.Create("pat@example.com", "Pat Rivera", "ext-canary-pat", host.Clock).Value;
        var bob = Requester.Create("bob@example.com", "Bob Stone", null, host.Clock).Value;
        (await host.CommitAsync(sp =>
        {
            sp.GetRequiredService<IAgentRepository>().Add(admin);
            sp.GetRequiredService<IRequesterRepository>().Add(pat);
            sp.GetRequiredService<IRequesterRepository>().Add(bob);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();

        var passport = await StoreFileAsync("pat-passport.png");
        var agentFile = await StoreFileAsync("agent.png");
        const string agentReplyBody = "<p>We are looking into it</p>";
        const string noteBody = "internal note about the case";

        async Task<Ticket> NewTicketAsync(Requester requester, string subject, string firstBody, Action<Ticket>? change = null)
        {
            Ticket? created = null;
            (await host.CommitAsync(async sp =>
            {
                var number = (await sp.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(scenario.Acme.Id, Ct)).Value;
                var ticket = Ticket.Create(number, scenario.Acme.Id, requester.Id, subject, TicketChannel.Web, null, false, host.Clock).Value;
                ticket.AddCustomerReply(requester.Id, firstBody, host.Clock).IsSuccess.ShouldBeTrue();
                change?.Invoke(ticket);
                sp.GetRequiredService<ITicketRepository>().Add(ticket);
                created = ticket;
            })).IsSuccess.ShouldBeTrue();
            return created!;
        }

        var ticketA = await NewTicketAsync(pat, "subject-canary-91c2 cannot sign in", "<p>canary-7f3a pat@example.com</p>", ticket =>
        {
            Attach(ticket.AddCustomerReply(pat.Id, "<p>my passport is attached</p>", host.Clock).Value, passport, host);
            Attach(ticket.AddAgentReply(scenario.Agent.Id, agentReplyBody, host.Clock).Value, agentFile, host);
            ticket.AddInternalNote(scenario.Agent.Id, noteBody, host.Clock).IsSuccess.ShouldBeTrue();
        });
        var ticketB = await NewTicketAsync(pat, "second request", "<p>another question</p>");
        var bobTicket = await NewTicketAsync(bob, "Bob question", "<p>bob body</p>");
        await ExecuteAsync($"UPDATE tickets SET metadata = '{{\"who\":\"pat@example.com\"}}', custom_fields = '{{\"name\":\"Pat Rivera\"}}' WHERE requester_id = '{pat.Id}'");

        var alert = Mail(host, "new-ticket-alert", "sam@example.com", "{\"requesterLabel\":\"pat@example.com\"}", ticketA.Id);
        var sent = Mail(host, "ticket-confirmation", "pat@example.com", "{}", null);
        var pending = Mail(host, "ticket-confirmation", "pat@example.com", "{}", null);
        var bobMail = Mail(host, "ticket-confirmation", "bob@example.com", "{}", bobTicket.Id);
        var bobAlert = Mail(host, "new-ticket-alert", "sam@example.com", "{}", bobTicket.Id);
        (await host.CommitAsync(sp =>
        {
            var outbox = sp.GetRequiredService<IEmailOutbox>();
            outbox.Enqueue(alert);
            outbox.Enqueue(sent);
            outbox.Enqueue(pending);
            outbox.Enqueue(bobMail);
            outbox.Enqueue(bobAlert);
            var tickets = sp.GetRequiredService<ITicketRepository>();
            tickets.AddAccessToken(TicketAccessToken.Issue(ticketA.Id, pat.Id, "sha256:" + new string('a', 64), host.Clock).Value);
            tickets.AddAccessToken(TicketAccessToken.Issue(ticketB.Id, pat.Id, "sha256:" + new string('b', 64), host.Clock).Value);
            tickets.AddAccessToken(TicketAccessToken.Issue(bobTicket.Id, bob.Id, "sha256:" + new string('c', 64), host.Clock).Value);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        await ExecuteAsync($"UPDATE email_outbox SET status = 'Sent' WHERE id = '{sent.Id}'");
        await ExecuteAsync($"UPDATE email_outbox SET to_address = 'Pat@Example.com' WHERE id = '{pending.Id}'");

        return new Arranged(host, pat.Id, ticketA.Id, ticketB.Id, bob.Id, bobTicket.Id, [passport.StorageKey], agentFile.StorageKey, agentReplyBody, noteBody);
    }

    private async Task<Result> EraseAsync(PersistenceTestHost host, Guid requesterId)
    {
        await using var scope = host.CreateScope();
        var sp = scope.ServiceProvider;
        var handler = new EraseRequesterRequestHandler(
            sp.GetRequiredService<IRequesterRepository>(),
            sp.GetRequiredService<IRequesterErasure>(),
            Store(),
            sp.GetRequiredService<IAdminEventRepository>(),
            sp.GetRequiredService<IAgentRepository>(),
            new StubClaims(new AgentClaims("oidc|admin", "Ada", "ada@example.com", AgentRole.Admin)),
            sp.GetRequiredService<IUnitOfWork>(),
            host.Clock,
            NullLogger<EraseRequesterRequestHandler>.Instance);
        return await handler.HandleAsync(requesterId, Ct);
    }

    /// <summary>Every text, varchar, citext, json, jsonb, tsvector and array column that holds any needle, as "table.column holds 'needle'".</summary>
    private async Task<List<string>> ScanAsync()
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        var columns = new List<(string Table, string Column)>();
        await using (var catalogue = new NpgsqlCommand(
            "SELECT c.table_name, c.column_name FROM information_schema.columns c " +
            "JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name AND t.table_type = 'BASE TABLE' " +
            "WHERE c.table_schema = 'public' AND (c.data_type IN ('text', 'character varying', 'jsonb', 'json', 'ARRAY') OR c.udt_name IN ('citext', 'tsvector'))",
            connection))
        await using (var reader = await catalogue.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                columns.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        var found = new List<string>();
        foreach (var (table, column) in columns)
        {
            foreach (var needle in Needles)
            {
                await using var command = new NpgsqlCommand($"SELECT count(*) FROM \"{table}\" WHERE \"{column}\"::text ILIKE '%' || @needle || '%'", connection);
                command.Parameters.AddWithValue("needle", needle);
                if ((long)(await command.ExecuteScalarAsync(Ct))! > 0)
                {
                    found.Add($"{table}.{column} holds '{needle}'");
                }
            }
        }

        return found;
    }

    [Fact]
    public async Task Nothing_personal_remains_after_erase()
    {
        var arranged = await ArrangeAsync();
        await using var host = arranged.Host;

        // The arrange seeded every needle, so an empty scan afterwards cannot be a vacuous pass.
        var before = await ScanAsync();
        foreach (var needle in Needles)
        {
            before.ShouldContain(entry => entry.EndsWith($"holds '{needle}'", StringComparison.Ordinal), needle);
        }

        // Each seeded location is proven individually (tsvector coverage rests on the single-token needles).
        before.Select(entry => entry[..entry.IndexOf(" holds", StringComparison.Ordinal)]).Distinct().Order(StringComparer.Ordinal)
            .ShouldBe(ExpectedLocations);

        arranged.StoredKeysOfPat.ShouldAllBe(key => File.Exists(Path.Combine(_root, key)));

        var result = await EraseAsync(host, arranged.PatId);

        result.IsSuccess.ShouldBeTrue();
        var leaks = await ScanAsync();
        leaks.ShouldBeEmpty();
        arranged.StoredKeysOfPat.ShouldAllBe(key => !File.Exists(Path.Combine(_root, key)));
    }

    [Fact]
    public async Task Agent_replies_internal_notes_and_their_files_are_untouched()
    {
        var arranged = await ArrangeAsync();
        await using var host = arranged.Host;

        (await EraseAsync(host, arranged.PatId)).IsSuccess.ShouldBeTrue();

        (await CountAsync($"SELECT count(*) FROM messages WHERE ticket_id = '{arranged.TicketAId}' AND body = '{arranged.AgentReplyBody}'")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM messages WHERE ticket_id = '{arranged.TicketAId}' AND body = '{arranged.InternalNoteBody}'")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM attachments WHERE storage_key = '{arranged.AgentFileKey}'")).ShouldBe(1);
        File.Exists(Path.Combine(_root, arranged.AgentFileKey)).ShouldBeTrue();
    }

    [Fact]
    public async Task Tickets_numbers_events_and_the_requester_row_survive()
    {
        var arranged = await ArrangeAsync();
        await using var host = arranged.Host;
        var numbersSql = $"SELECT string_agg(number::text, ',' ORDER BY id) FROM tickets WHERE id IN ('{arranged.TicketAId}', '{arranged.TicketBId}', '{arranged.BobTicketId}')";
        var numbers = await TextAsync(numbersSql);
        var events = await CountAsync("SELECT count(*) FROM ticket_events");
        var bobMessage = await TextAsync($"SELECT body FROM messages WHERE ticket_id = '{arranged.BobTicketId}'");
        var bobSubject = await TextAsync($"SELECT subject FROM tickets WHERE id = '{arranged.BobTicketId}'");

        (await EraseAsync(host, arranged.PatId)).IsSuccess.ShouldBeTrue();

        (await TextAsync(numbersSql)).ShouldBe(numbers);
        (await CountAsync("SELECT count(*) FROM ticket_events")).ShouldBe(events);
        (await CountAsync($"SELECT count(*) FROM requesters WHERE id = '{arranged.PatId}' AND email = 'erased-{arranged.PatId}@invalid' AND name IS NULL AND erased_at IS NOT NULL")).ShouldBe(1);
        (await TextAsync($"SELECT body FROM messages WHERE ticket_id = '{arranged.BobTicketId}'")).ShouldBe(bobMessage);
        (await TextAsync($"SELECT subject FROM tickets WHERE id = '{arranged.BobTicketId}'")).ShouldBe(bobSubject);
        (await CountAsync($"SELECT count(*) FROM requesters WHERE id = '{arranged.BobId}' AND email = 'bob@example.com' AND erased_at IS NULL")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM ticket_access_tokens WHERE ticket_id = '{arranged.BobTicketId}' AND revoked_at IS NULL")).ShouldBe(1);
    }

    [Fact]
    public async Task Outbox_rows_by_address_and_by_ticket_are_gone_and_others_stay()
    {
        var arranged = await ArrangeAsync();
        await using var host = arranged.Host;
        (await CountAsync("SELECT count(*) FROM email_outbox WHERE lower(to_address) = 'pat@example.com'")).ShouldBe(2);
        (await CountAsync($"SELECT count(*) FROM email_outbox WHERE ticket_id IN ('{arranged.TicketAId}', '{arranged.TicketBId}')")).ShouldBe(1);

        (await EraseAsync(host, arranged.PatId)).IsSuccess.ShouldBeTrue();

        (await CountAsync("SELECT count(*) FROM email_outbox WHERE lower(to_address) = 'pat@example.com'")).ShouldBe(0);
        (await CountAsync($"SELECT count(*) FROM email_outbox WHERE ticket_id IN ('{arranged.TicketAId}', '{arranged.TicketBId}')")).ShouldBe(0);
        (await CountAsync($"SELECT count(*) FROM email_outbox WHERE ticket_id = '{arranged.BobTicketId}'")).ShouldBe(2);
    }

    [Fact]
    public async Task Tokens_are_revoked()
    {
        var arranged = await ArrangeAsync();
        await using var host = arranged.Host;

        (await EraseAsync(host, arranged.PatId)).IsSuccess.ShouldBeTrue();

        (await CountAsync($"SELECT count(*) FROM ticket_access_tokens WHERE requester_id = '{arranged.PatId}'")).ShouldBe(2);
        (await CountAsync($"SELECT count(*) FROM ticket_access_tokens WHERE requester_id = '{arranged.PatId}' AND revoked_at IS NULL")).ShouldBe(0);
        (await CountAsync($"SELECT count(*) FROM ticket_access_tokens WHERE requester_id = '{arranged.BobId}' AND revoked_at IS NULL")).ShouldBe(1);
    }

    [Fact]
    public async Task Erasing_twice_succeeds_and_each_run_writes_one_audit_row()
    {
        var arranged = await ArrangeAsync();
        await using var host = arranged.Host;

        (await EraseAsync(host, arranged.PatId)).IsSuccess.ShouldBeTrue();
        (await EraseAsync(host, arranged.PatId)).IsSuccess.ShouldBeTrue();

        (await CountAsync($"SELECT count(*) FROM admin_events WHERE type = 'RequesterErased' AND subject_id = '{arranged.PatId}'")).ShouldBe(2);
        for (var i = 0; i < 2; i++)
        {
            var payload = await TextAsync($"SELECT payload::text FROM admin_events WHERE type = 'RequesterErased' AND subject_id = '{arranged.PatId}' ORDER BY id OFFSET {i} LIMIT 1");
            JsonDocument.Parse(payload).RootElement.EnumerateObject().Select(p => p.Name)
                .ShouldBe(["tickets", "messages", "attachments", "links", "outboxRows"], ignoreOrder: true);
        }
    }

    [Fact]
    public async Task The_audit_row_has_counts_and_no_personal_data()
    {
        var arranged = await ArrangeAsync();
        await using var host = arranged.Host;

        (await EraseAsync(host, arranged.PatId)).IsSuccess.ShouldBeTrue();

        var payload = await TextAsync($"SELECT payload::text FROM admin_events WHERE type = 'RequesterErased' AND subject_id = '{arranged.PatId}'");
        JsonDocument.Parse(payload).RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["tickets", "messages", "attachments", "links", "outboxRows"], ignoreOrder: true);
        foreach (var needle in Needles)
        {
            payload.ShouldNotContain(needle, Case.Insensitive);
        }
    }
}
