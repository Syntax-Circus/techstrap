using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>The grouped count behind GET /api/tags/summary runs against real Postgres: one row per tag, a tag nobody carries reports 0, and the order is by name (D-041).</summary>
public sealed class TagSummaryIntegrationTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_tag_reports_how_many_tickets_carry_it_and_an_unused_tag_reports_zero()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var bug = Tag.Create("bug", "Bug", "#DC2626", host.Clock).Value;
        var billing = Tag.Create("billing", "Billing", "#2563EB", host.Clock).Value;
        var unused = Tag.Create("unused", "Aardvark", "#16A34A", host.Clock).Value;
        (await host.CommitAsync(sp =>
        {
            var tags = sp.GetRequiredService<ITagRepository>();
            tags.Add(bug);
            tags.Add(billing);
            tags.Add(unused);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        await scenario.CreateTicketAsync("Open one", change: ticket => ticket.AddTag(bug.Id, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue());
        await scenario.CreateTicketAsync("Closed one", change: ticket =>
        {
            ticket.AddTag(bug.Id, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
            ticket.AddTag(billing.Id, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
            ticket.ChangeStatus(TicketStatus.Solved, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
            ticket.ChangeStatus(TicketStatus.Closed, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
        });

        var usage = await host.ReadAsync(sp => sp.GetRequiredService<ITagRepository>().ListWithTicketCountsAsync(Ct));

        // Ordered by name: Aardvark (unused), Billing, Bug.
        usage.Select(row => (row.Tag.Slug, row.TicketCount)).ShouldBe([("unused", 0), ("billing", 1), ("bug", 2)]);
        usage.Select(row => row.Tag.Id).ShouldBe([unused.Id, billing.Id, bug.Id]);
    }

    [Fact]
    public async Task With_no_tags_the_summary_is_empty()
    {
        await using var host = new PersistenceTestHost(Database);

        var usage = await host.ReadAsync(sp => sp.GetRequiredService<ITagRepository>().ListWithTicketCountsAsync(Ct));

        usage.ShouldBeEmpty();
    }
}
