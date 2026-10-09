using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tags;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>The real DeleteTagRequestHandler against Postgres: forced delete detaches open and Closed tickets in one transaction (D-030).</summary>
public sealed class DeleteTagIntegrationTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed class StubClaims(AgentClaims claims) : ICurrentAgentClaims
    {
        public AgentClaims? Current { get; } = claims;
    }

    private async Task<long> CountAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<(PersistenceTestHost Host, Tag Tag, Ticket Open, Ticket Closed)> ArrangeAsync()
    {
        var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var admin = Agent.Create("oidc|admin", "Ada", "ada@example.com", AgentRole.Admin, host.Clock).Value;
        var tag = Tag.Create("bug", "Bug", "#DC2626", host.Clock).Value;
        (await host.CommitAsync(sp =>
        {
            sp.GetRequiredService<IAgentRepository>().Add(admin);
            sp.GetRequiredService<ITagRepository>().Add(tag);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();

        var open = await scenario.CreateTicketAsync("Open one", change: ticket => ticket.AddTag(tag.Id, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue());
        var closed = await scenario.CreateTicketAsync("Closed one", change: ticket =>
        {
            ticket.AddTag(tag.Id, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
            ticket.ChangeStatus(TicketStatus.Solved, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
            ticket.ChangeStatus(TicketStatus.Closed, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
        });
        return (host, tag, open, closed);
    }

    private static async Task<Result> DeleteAsync(PersistenceTestHost host, Guid tagId, bool force)
    {
        await using var scope = host.CreateScope();
        var sp = scope.ServiceProvider;
        var handler = new DeleteTagRequestHandler(
            new StubClaims(new AgentClaims("oidc|admin", "Ada", "ada@example.com", AgentRole.Admin)),
            sp.GetRequiredService<IAgentRepository>(),
            sp.GetRequiredService<ITagRepository>(),
            sp.GetRequiredService<ITicketRepository>(),
            sp.GetRequiredService<IAdminEventRepository>(),
            sp.GetRequiredService<IUnitOfWork>(),
            host.Clock);
        return await handler.HandleAsync(tagId, force, Ct);
    }

    [Fact]
    public async Task Force_delete_detaches_open_and_closed_tickets_with_events()
    {
        var (host, tag, open, closed) = await ArrangeAsync();
        await using var _ = host;

        var result = await DeleteAsync(host, tag.Id, force: true);

        result.IsSuccess.ShouldBeTrue();
        (await CountAsync($"SELECT count(*) FROM ticket_tags WHERE tag_id = '{tag.Id}'")).ShouldBe(0);
        (await CountAsync($"SELECT count(*) FROM tags WHERE id = '{tag.Id}'")).ShouldBe(0);
        (await CountAsync("SELECT count(*) FROM ticket_events WHERE type = 'TagRemoved' AND payload->>'reason' = 'tag-deleted'")).ShouldBe(2);
        (await CountAsync($"SELECT count(*) FROM tickets WHERE id = '{closed.Id}' AND status = 'Closed'")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM tickets WHERE id = '{open.Id}' AND status = 'New'")).ShouldBe(1);
        (await CountAsync("SELECT count(*) FROM admin_events WHERE type = 'TagDeleted'")).ShouldBe(1);
    }

    [Fact(Timeout = 180_000)]
    public async Task A_forced_delete_across_two_batches_detaches_every_ticket()
    {
        const int carrierCount = 205;
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var admin = Agent.Create("oidc|admin", "Ada", "ada@example.com", AgentRole.Admin, host.Clock).Value;
        var tag = Tag.Create("bug", "Bug", "#DC2626", host.Clock).Value;
        (await host.CommitAsync(sp =>
        {
            sp.GetRequiredService<IAgentRepository>().Add(admin);
            sp.GetRequiredService<ITagRepository>().Add(tag);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        var target = scenario.Acme;
        (await host.CommitAsync(async sp =>
        {
            var allocator = sp.GetRequiredService<ITicketNumberAllocator>();
            var repository = sp.GetRequiredService<ITicketRepository>();
            for (var i = 0; i < carrierCount; i++)
            {
                var number = (await allocator.AllocateAsync(target.Id, Ct)).Value;
                var ticket = Ticket.Create(number, target.Id, scenario.Requester.Id, "Carrier " + i, TicketChannel.Web, null, false, host.Clock).Value;
                ticket.AddCustomerReply(scenario.Requester.Id, "<p>hi</p>", host.Clock).IsSuccess.ShouldBeTrue();
                ticket.AddTag(tag.Id, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
                repository.Add(ticket);
            }
        })).IsSuccess.ShouldBeTrue();

        var result = await DeleteAsync(host, tag.Id, force: true);

        result.IsSuccess.ShouldBeTrue();
        (await CountAsync($"SELECT count(*) FROM ticket_tags WHERE tag_id = '{tag.Id}'")).ShouldBe(0);
        (await CountAsync($"SELECT count(*) FROM tags WHERE id = '{tag.Id}'")).ShouldBe(0);
        (await CountAsync("SELECT count(*) FROM ticket_events WHERE type = 'TagRemoved' AND payload->>'reason' = 'tag-deleted'")).ShouldBe(carrierCount);
        (await CountAsync("SELECT count(DISTINCT ticket_id) FROM ticket_events WHERE type = 'TagRemoved' AND payload->>'reason' = 'tag-deleted'")).ShouldBe(carrierCount);
    }

    [Fact]
    public async Task Without_force_a_tag_in_use_is_refused_and_nothing_changes()
    {
        var (host, tag, _, _) = await ArrangeAsync();
        await using var _ = host;

        var result = await DeleteAsync(host, tag.Id, force: false);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Conflict),
            error => error.Code.ShouldBe("tag-in-use"));
        (await CountAsync($"SELECT count(*) FROM ticket_tags WHERE tag_id = '{tag.Id}'")).ShouldBe(2);
        (await CountAsync($"SELECT count(*) FROM tags WHERE id = '{tag.Id}'")).ShouldBe(1);
        (await CountAsync("SELECT count(*) FROM ticket_events WHERE type = 'TagRemoved'")).ShouldBe(0);
    }
}
