using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TechStrap.Application.Persistence;
using TechStrap.Application.Requesters;
using TechStrap.Domain.Outbox;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>The bulk half of erasing a requester (D-039): exactly the requester's data changes, and it all rides on the caller's unit of work.</summary>
public sealed class RequesterErasureTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed record Arranged(PersistenceTestHost Host, Guid AnnId, Guid BobId, Guid TicketA, Guid TicketB, Guid TicketBob);

    private async Task<long> ScalarAsync(string sql)
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

    private static EmailOutboxItem Mail(PersistenceTestHost host, string kind, string to, Guid? ticketId, string payload = "{}") =>
        EmailOutboxItem.Enqueue(kind, to, payload, null, ticketId, host.Clock).Value;

    private async Task<Arranged> ArrangeAsync()
    {
        var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);

        var bob = Requester.Create("bob@example.com", "Bob", null, host.Clock).Value;
        (await host.CommitAsync(sp =>
        {
            sp.GetRequiredService<IRequesterRepository>().Add(bob);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        Ticket? bobTicket = null;
        (await host.CommitAsync(async sp =>
        {
            var number = (await sp.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(scenario.Acme.Id, Ct)).Value;
            bobTicket = Ticket.Create(number, scenario.Acme.Id, bob.Id, "Bob question", TicketChannel.Web, null, false, host.Clock).Value;
            bobTicket.AddCustomerReply(bob.Id, "<p>Bob here</p>", host.Clock).IsSuccess.ShouldBeTrue();
            sp.GetRequiredService<ITicketRepository>().Add(bobTicket);
        })).IsSuccess.ShouldBeTrue();

        var a = await scenario.CreateTicketAsync("Ann A");
        var b = await scenario.CreateTicketAsync("Ann B");
        (await scenario.UpdateAsync(a.Id, t =>
        {
            var customer = t.AddCustomerReply(scenario.Requester.Id, "<p>More from Ann</p>", host.Clock).Value;
            customer.AddAttachment("c.png", "image/png", 3, "attachments/a/customer", host.Clock).IsSuccess.ShouldBeTrue();
            var reply = t.AddAgentReply(scenario.Agent.Id, "<p>Agent reply</p>", host.Clock).Value;
            reply.AddAttachment("r.png", "image/png", 3, "attachments/a/agent", host.Clock).IsSuccess.ShouldBeTrue();
            t.AddInternalNote(scenario.Agent.Id, "<p>Internal</p>", host.Clock).IsSuccess.ShouldBeTrue();
        })).IsSuccess.ShouldBeTrue();

        (await host.CommitAsync(sp =>
        {
            var repository = sp.GetRequiredService<ITicketRepository>();
            repository.AddAccessToken(TicketAccessToken.Issue(a.Id, scenario.Requester.Id, "hash-active", host.Clock).Value);
            var revoked = TicketAccessToken.Issue(a.Id, scenario.Requester.Id, "hash-revoked", host.Clock).Value;
            revoked.Revoke(host.Clock);
            repository.AddAccessToken(revoked);
            repository.AddAccessToken(TicketAccessToken.Issue(bobTicket!.Id, bob.Id, "hash-bob", host.Clock).Value);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();

        await ExecuteAsync($"UPDATE tickets SET metadata = '{{\"note\":\"ann@example.com\"}}', custom_fields = '{{\"plan\":\"pro\"}}' WHERE id IN ('{a.Id}', '{b.Id}')");

        var sentToAnn = Mail(host, "ticket-confirmation", "Ann@Example.com", null);
        (await host.CommitAsync(sp =>
        {
            var outbox = sp.GetRequiredService<IEmailOutbox>();
            outbox.Enqueue(Mail(host, "ticket-confirmation", "ann@example.com", null));
            outbox.Enqueue(sentToAnn);
            outbox.Enqueue(Mail(host, "new-ticket-alert", "sam@example.com", a.Id, "{\"requesterLabel\":\"ann@example.com\"}"));
            outbox.Enqueue(Mail(host, "ticket-confirmation", "bob@example.com", null));
            outbox.Enqueue(Mail(host, "new-ticket-alert", "sam@example.com", bobTicket!.Id));
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        await ExecuteAsync($"UPDATE email_outbox SET status = 'Sent' WHERE id = '{sentToAnn.Id}'");

        return new Arranged(host, scenario.Requester.Id, bob.Id, a.Id, b.Id, bobTicket!.Id);
    }

    [Fact]
    public async Task The_port_erases_exactly_the_requesters_data_and_reports_what_it_did()
    {
        var arranged = await ArrangeAsync();
        await using var _ = arranged.Host;
        RequesterErasureResult? result = null;

        (await arranged.Host.CommitAsync(async sp =>
            result = await sp.GetRequiredService<IRequesterErasure>().EraseDataAsync(arranged.AnnId, "ann@example.com", arranged.Host.Clock.GetUtcNow(), Ct)))
            .IsSuccess.ShouldBeTrue();

        result!.Tickets.ShouldBe(2);
        result.Messages.ShouldBe(3);
        result.Attachments.ShouldBe(1);
        result.StorageKeys.ShouldBe(["attachments/a/customer"]);
        result.Tokens.ShouldBe(1);
        result.OutboxRows.ShouldBe(3);
        (await ScalarAsync($"SELECT count(*) FROM messages WHERE author_type = 'Requester' AND author_id = '{arranged.AnnId}' AND body <> '{ErasureMarker.Text}'")).ShouldBe(0);
        (await ScalarAsync($"SELECT count(*) FROM messages WHERE author_type = 'Agent' AND body = '{ErasureMarker.Text}'")).ShouldBe(0);
        (await ScalarAsync($"SELECT count(*) FROM messages WHERE author_type = 'Requester' AND author_id = '{arranged.BobId}' AND body = '{ErasureMarker.Text}'")).ShouldBe(0);
        (await ScalarAsync($"SELECT count(*) FROM tickets WHERE requester_id = '{arranged.AnnId}' AND subject = '{ErasureMarker.Text}' AND metadata IS NULL AND custom_fields IS NULL")).ShouldBe(2);
        (await ScalarAsync("SELECT count(*) FROM attachments WHERE storage_key = 'attachments/a/agent'")).ShouldBe(1);
        (await ScalarAsync($"SELECT count(*) FROM ticket_access_tokens WHERE requester_id = '{arranged.AnnId}' AND revoked_at IS NULL")).ShouldBe(0);
        (await ScalarAsync("SELECT count(*) FROM email_outbox")).ShouldBe(2);
        (await ScalarAsync($"SELECT count(*) FROM ticket_access_tokens WHERE requester_id = '{arranged.BobId}' AND revoked_at IS NULL")).ShouldBe(1);
        (await ScalarAsync($"SELECT count(*) FROM tickets WHERE requester_id = '{arranged.BobId}' AND subject <> '{ErasureMarker.Text}'")).ShouldBe(1);
        (await ScalarAsync($"SELECT count(*) FROM ticket_events WHERE ticket_id IN ('{arranged.TicketA}', '{arranged.TicketB}')")).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_second_run_deletes_and_revokes_nothing_more()
    {
        var arranged = await ArrangeAsync();
        await using var _ = arranged.Host;
        var results = new List<RequesterErasureResult>();

        for (var run = 0; run < 2; run++)
        {
            (await arranged.Host.CommitAsync(async sp =>
                results.Add(await sp.GetRequiredService<IRequesterErasure>().EraseDataAsync(arranged.AnnId, "ann@example.com", arranged.Host.Clock.GetUtcNow(), Ct))))
                .IsSuccess.ShouldBeTrue();
        }

        var second = results[1];
        second.Attachments.ShouldBe(0);
        second.Tokens.ShouldBe(0);
        second.OutboxRows.ShouldBe(0);
        second.StorageKeys.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_erasure_rolls_back_with_the_unit_of_work()
    {
        var arranged = await ArrangeAsync();
        await using var _ = arranged.Host;

        await using (var scope = arranged.Host.CreateScope())
        {
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
            var result = await scope.ServiceProvider.GetRequiredService<IRequesterErasure>().EraseDataAsync(arranged.AnnId, "ann@example.com", arranged.Host.Clock.GetUtcNow(), Ct);
            result.Tickets.ShouldBe(2);
            result.Attachments.ShouldBe(1);
            result.Tokens.ShouldBe(1);
            result.OutboxRows.ShouldBe(3);
        }

        (await ScalarAsync($"SELECT count(*) FROM messages WHERE body = '{ErasureMarker.Text}'")).ShouldBe(0);
        (await ScalarAsync($"SELECT count(*) FROM tickets WHERE subject = '{ErasureMarker.Text}'")).ShouldBe(0);
        (await ScalarAsync($"SELECT count(*) FROM tickets WHERE requester_id = '{arranged.AnnId}' AND metadata IS NOT NULL AND custom_fields IS NOT NULL")).ShouldBe(2);
        (await ScalarAsync("SELECT count(*) FROM attachments WHERE storage_key = 'attachments/a/customer'")).ShouldBe(1);
        (await ScalarAsync($"SELECT count(*) FROM ticket_access_tokens WHERE requester_id = '{arranged.AnnId}' AND revoked_at IS NULL")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM email_outbox")).ShouldBe(5);
    }

    [Fact]
    public async Task A_requester_with_no_tickets_is_a_no_op()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var lonely = Requester.Create("lonely@example.com", "Lonely", null, host.Clock).Value;
        (await host.CommitAsync(sp =>
        {
            sp.GetRequiredService<IRequesterRepository>().Add(lonely);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        var ticket = await scenario.CreateTicketAsync();
        RequesterErasureResult? result = null;

        (await host.CommitAsync(async sp =>
            result = await sp.GetRequiredService<IRequesterErasure>().EraseDataAsync(lonely.Id, "lonely@example.com", host.Clock.GetUtcNow(), Ct)))
            .IsSuccess.ShouldBeTrue();

        result!.Tickets.ShouldBe(0);
        result.Messages.ShouldBe(0);
        result.Attachments.ShouldBe(0);
        result.Tokens.ShouldBe(0);
        result.OutboxRows.ShouldBe(0);
        result.StorageKeys.ShouldBeEmpty();
        (await ScalarAsync($"SELECT count(*) FROM tickets WHERE id = '{ticket.Id}' AND subject <> '{ErasureMarker.Text}'")).ShouldBe(1);
        (await ScalarAsync($"SELECT count(*) FROM messages WHERE body = '{ErasureMarker.Text}'")).ShouldBe(0);
    }
}
