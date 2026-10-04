using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Outbox;
using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class CustomerReadTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private async Task<Ticket> CreateFollowUpAsync(PersistenceTestHost host, TicketScenario scenario, Guid parentId, string body)
    {
        Ticket? followUp = null;
        var result = await host.CommitAsync(async sp =>
        {
            var repository = sp.GetRequiredService<ITicketRepository>();
            var parent = (await repository.GetByIdAsync(parentId, Ct))!;
            var number = (await sp.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(parent.ProductId, Ct)).Value;
            followUp = parent.CreateFollowUp(number, host.Clock).Value;
            followUp.AddCustomerReply(scenario.Requester.Id, body, host.Clock).IsSuccess.ShouldBeTrue();
            repository.Update(parent);
            repository.Add(followUp);
        });
        result.IsSuccess.ShouldBeTrue();
        return followUp!;
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
    public async Task Recent_follow_ups_of_a_parent_come_with_their_first_public_message()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var parent = await scenario.CreateTicketAsync();
        await scenario.UpdateAsync(parent.Id, t =>
        {
            t.ChangeStatus(TicketStatus.Solved, scenario.AgentActor, host.Clock);
            t.ChangeStatus(TicketStatus.Closed, Actor.System, host.Clock);
        });
        host.Clock.Advance(TimeSpan.FromMinutes(5));
        await CreateFollowUpAsync(host, scenario, parent.Id, "<p>older</p>");
        host.Clock.Advance(TimeSpan.FromSeconds(270));
        var newer = await CreateFollowUpAsync(host, scenario, parent.Id, "<p>newer</p>");
        host.Clock.Advance(TimeSpan.FromSeconds(30));
        var since = host.Clock.GetUtcNow().AddMinutes(-2);

        var rows = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().ListRecentFollowUpsAsync(parent.Id, since, Ct));

        var row = rows.ShouldHaveSingleItem();
        row.TicketId.ShouldBe(newer.Id);
        row.Number.ShouldBe(newer.Number.ToString());
        row.FirstMessageBody.ShouldBe("<p>newer</p>");
        var messages = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().GetMessagesAsync(newer.Id, true, Ct));
        row.FirstMessageId.ShouldBe(messages[0].Id);
        row.CreatedAt.ShouldBe(newer.CreatedAt);
    }

    [Fact]
    public async Task A_requesters_recent_tickets_exclude_spam_and_are_capped()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var created = new List<Ticket>();
        for (var i = 0; i < 7; i++)
        {
            created.Add(await scenario.CreateTicketAsync($"t{i}", i % 2 == 0 ? scenario.Acme : scenario.Orbitly));
        }

        await scenario.UpdateAsync(created[6].Id, t => t.MarkSpam(true, scenario.AgentActor, host.Clock));
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        // Someone else's ticket.
        await ExecuteAsync("INSERT INTO requesters (id, email, name) VALUES ('00000000-0000-0000-0000-0000000000aa', 'bob@example.com', 'Bob')");
        var otherId = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
        var otherTicket = await scenario.CreateTicketAsync("other");
        await ExecuteAsync($"UPDATE tickets SET requester_id = '{otherId}' WHERE id = '{otherTicket.Id}'");

        var rows = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().ListRecentTicketsForRequesterAsync(scenario.Requester.Id, 5, Ct));

        rows.Count.ShouldBe(5);
        rows.Select(r => r.Subject).ShouldBe(["t5", "t4", "t3", "t2", "t1"]);
        rows.ShouldNotContain(r => r.TicketId == created[6].Id || r.TicketId == otherTicket.Id);
        rows[0].ProductId.ShouldBe(scenario.Orbitly.Id);
        rows[0].Number.ShouldBe(created[5].Number.ToString());
        rows[0].LastActivityAt.ShouldBe((await scenario.LoadAsync(created[5].Id))!.LastActivityAt);
    }

    [Fact]
    public async Task Solved_spam_is_never_returned_for_auto_close()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var spam = await scenario.CreateTicketAsync("spam");
        var real = await scenario.CreateTicketAsync("real");
        await scenario.UpdateAsync(spam.Id, t => t.ChangeStatus(TicketStatus.Solved, scenario.AgentActor, host.Clock));
        await scenario.UpdateAsync(real.Id, t => t.ChangeStatus(TicketStatus.Solved, scenario.AgentActor, host.Clock));
        await ExecuteAsync($"UPDATE tickets SET is_spam = true WHERE id = '{spam.Id}'");
        host.Clock.Advance(TimeSpan.FromDays(8));
        var cutoff = host.Clock.GetUtcNow().AddDays(-7);

        var due = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().ListSolvedBeforeAsync(cutoff, 10, Ct));

        due.ShouldHaveSingleItem().Id.ShouldBe(real.Id);
    }

    [Fact]
    public async Task Recent_outbox_rows_are_counted_per_kind_and_address_case_insensitively()
    {
        await using var host = new PersistenceTestHost(Database);
        var enqueue = async (string kind, string to) =>
        {
            var item = EmailOutboxItem.Enqueue(kind, to, "{}", null, null, host.Clock).Value;
            (await host.CommitAsync(sp =>
            {
                sp.GetRequiredService<IEmailOutbox>().Enqueue(item);
                return Task.CompletedTask;
            })).IsSuccess.ShouldBeTrue();
            return item;
        };

        await enqueue("access-links", "ann@example.com");
        host.Clock.Advance(TimeSpan.FromHours(2));
        var sent = await enqueue("access-links", "ANN@example.com");
        var claimed = await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().ClaimBatchAsync("w1", 10, TimeSpan.FromMinutes(5), Ct));
        claimed.ShouldContain(c => c.Id == sent.Id);
        (await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().MarkSentAsync(sent.Id, "w1", Ct))).IsSuccess.ShouldBeTrue();
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await enqueue("access-links", "ann@example.com");
        await enqueue("TicketConfirmation", "ann@example.com");
        await enqueue("access-links", "bob@example.com");
        var since = host.Clock.GetUtcNow().AddHours(-1);

        var count = await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().CountRecentAsync("access-links", "Ann@Example.com", since, Ct));

        count.ShouldBe(2);
    }
}
