using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tickets;
using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>The queue views (FR-TKT-01, D-024) against a seeded set of tickets in every state.</summary>
public sealed class TicketQueueTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed record Seeded(TicketScenario Scenario, Guid BugTagId);

    private async Task<Seeded> SeedAsync(PersistenceTestHost host)
    {
        var scenario = await TicketScenario.CreateAsync(host);
        var bug = Tag.Create("bug", "Bug", "#FF0000", host.Clock).Value;
        await host.CommitAsync(sp => { sp.GetRequiredService<ITagRepository>().Add(bug); return Task.CompletedTask; });
        var agent = scenario.AgentActor;

        await scenario.CreateTicketAsync("T1 new unassigned", change: t => t.AddTag(bug.Id, agent, host.Clock));
        var t2 = await scenario.CreateTicketAsync("T2 open mine");
        var t3 = await scenario.CreateTicketAsync("T3 pending mine");
        var t4 = await scenario.CreateTicketAsync("T4 solved", change: t => t.AddTag(bug.Id, agent, host.Clock));
        var t5 = await scenario.CreateTicketAsync("T5 closed");
        var t6 = await scenario.CreateTicketAsync("T6 spam");
        var t7 = await scenario.CreateTicketAsync("T7 other product open unassigned", product: scenario.Orbitly);
        await scenario.CreateTicketAsync("T8 urgent new unassigned", change: t => t.ChangePriority(TicketPriority.Urgent, agent, host.Clock));

        await Mutate(t2, t => { t.ChangeStatus(TicketStatus.Open, agent, host.Clock); t.Assign(scenario.Agent.Id, agent, host.Clock); });
        await Mutate(t3, t => { t.ChangeStatus(TicketStatus.Pending, agent, host.Clock); t.Assign(scenario.Agent.Id, agent, host.Clock); });
        await Mutate(t4, t => t.ChangeStatus(TicketStatus.Solved, agent, host.Clock));
        await Mutate(t5, t => { t.ChangeStatus(TicketStatus.Solved, agent, host.Clock); t.ChangeStatus(TicketStatus.Closed, Actor.System, host.Clock); });
        await Mutate(t6, t => t.MarkSpam(true, agent, host.Clock));
        await Mutate(t7, t => t.ChangeStatus(TicketStatus.Open, agent, host.Clock));
        return new Seeded(scenario, bug.Id);

        async Task Mutate(Ticket ticket, Action<Ticket> change)
        {
            (await scenario.UpdateAsync(ticket.Id, change)).IsSuccess.ShouldBeTrue();
            host.Clock.Advance(TimeSpan.FromMinutes(1));
        }
    }

    private static async Task<List<string>> SubjectsAsync(PersistenceTestHost host, TicketQuery query)
    {
        var page = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().ListAsync(query, Ct));
        return [.. page.Items.Select(i => i.Subject[..2])];
    }

    [Theory]
    [InlineData(TicketView.Unassigned, new[] { "T1", "T7", "T8" })]
    [InlineData(TicketView.Mine, new[] { "T2", "T3" })]
    [InlineData(TicketView.Open, new[] { "T1", "T2", "T7", "T8" })]
    [InlineData(TicketView.Pending, new[] { "T3" })]
    [InlineData(TicketView.All, new[] { "T1", "T2", "T3", "T4", "T5", "T7", "T8" })]
    [InlineData(TicketView.Spam, new[] { "T6" })]
    public async Task Each_view_returns_exactly_its_tickets(TicketView view, string[] expected)
    {
        await using var host = new PersistenceTestHost(Database);
        var seeded = await SeedAsync(host);

        var subjects = await SubjectsAsync(host, new TicketQuery(view, AgentId: seeded.Scenario.Agent.Id, PageSize: 50));

        subjects.Order().ShouldBe(expected);
    }

    [Fact]
    public async Task The_Mine_view_without_an_agent_is_empty_not_everything_unassigned()
    {
        await using var host = new PersistenceTestHost(Database);
        await SeedAsync(host);

        (await SubjectsAsync(host, new TicketQuery(TicketView.Mine))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Results_are_newest_activity_first()
    {
        await using var host = new PersistenceTestHost(Database);
        await SeedAsync(host);

        var page = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().ListAsync(new TicketQuery(TicketView.All, PageSize: 50), Ct));

        page.Items.Select(i => i.LastActivityAt).ShouldBe(page.Items.Select(i => i.LastActivityAt).OrderDescending());
        page.Items[0].Subject.ShouldStartWith("T7");
    }

    [Fact]
    public async Task Filters_narrow_a_view_by_product_status_priority_assignee_tag_and_requester()
    {
        await using var host = new PersistenceTestHost(Database);
        var seeded = await SeedAsync(host);
        var scenario = seeded.Scenario;

        (await SubjectsAsync(host, new TicketQuery(TicketView.All, ProductId: scenario.Orbitly.Id))).ShouldBe(["T7"]);
        (await SubjectsAsync(host, new TicketQuery(TicketView.All, Status: TicketStatus.Solved))).ShouldBe(["T4"]);
        (await SubjectsAsync(host, new TicketQuery(TicketView.All, Priority: TicketPriority.Urgent))).ShouldBe(["T8"]);
        (await SubjectsAsync(host, new TicketQuery(TicketView.All, AssigneeId: scenario.Agent.Id, PageSize: 50))).Order().ShouldBe(["T2", "T3"]);
        (await SubjectsAsync(host, new TicketQuery(TicketView.All, TagId: seeded.BugTagId, PageSize: 50))).Order().ShouldBe(["T1", "T4"]);
        (await SubjectsAsync(host, new TicketQuery(TicketView.All, RequesterId: Guid.NewGuid()))).ShouldBeEmpty();
        (await SubjectsAsync(host, new TicketQuery(TicketView.All, RequesterId: scenario.Requester.Id, PageSize: 50))).Count.ShouldBe(7);
    }

    [Fact]
    public async Task Paging_reports_the_total_and_slices_the_list()
    {
        await using var host = new PersistenceTestHost(Database);
        await SeedAsync(host);
        var repository = (Func<int, Task<SyntaxCircus.Common.PagedResult<TicketSummary>>>)(page =>
            host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().ListAsync(new TicketQuery(TicketView.All, Page: page, PageSize: 3), Ct)));

        var first = await repository(1);
        var third = await repository(3);
        var beyond = await repository(9);

        first.TotalCount.ShouldBe(7);
        first.Items.Count.ShouldBe(3);
        third.Items.Count.ShouldBe(1);
        beyond.Items.ShouldBeEmpty();
        beyond.TotalCount.ShouldBe(7);
    }

    [Fact]
    public async Task A_summary_carries_the_number_status_priority_and_requester_email()
    {
        await using var host = new PersistenceTestHost(Database);
        var seeded = await SeedAsync(host);

        var page = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().ListAsync(new TicketQuery(TicketView.Pending), Ct));

        var summary = page.Items.ShouldHaveSingleItem();
        summary.Number.ShouldBe("ACME-3");
        summary.Status.ShouldBe(TicketStatus.Pending);
        summary.Priority.ShouldBe(TicketPriority.Normal);
        summary.RequesterEmail.ShouldBe("ann@example.com");
        summary.AssigneeId.ShouldBe(seeded.Scenario.Agent.Id);
        summary.IsSpam.ShouldBeFalse();
    }
}
