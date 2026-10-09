using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class TicketBatchLoadTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact(Timeout = 120_000)]
    public async Task GetByIdsAsync_returns_the_same_graph_as_GetByIdAsync_and_omits_missing_ids()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var tag = Tag.Create("bug", "Bug", "#DC2626", host.Clock).Value;
        (await host.CommitAsync(sp =>
        {
            sp.GetRequiredService<ITagRepository>().Add(tag);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        var tagged = await scenario.CreateTicketAsync("Tagged", change: t => t.AddTag(tag.Id, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue());
        var plain = await scenario.CreateTicketAsync("Plain");

        var (single, batch) = await host.ReadAsync(async sp =>
        {
            var tickets = sp.GetRequiredService<ITicketRepository>();
            var one = (await tickets.GetByIdAsync(tagged.Id, TestContext.Current.CancellationToken))!;
            var many = await tickets.GetByIdsAsync([tagged.Id, plain.Id, Guid.CreateVersion7()], TestContext.Current.CancellationToken);
            return (one, many);
        });

        batch.Count.ShouldBe(2);
        var loaded = batch.Single(t => t.Id == tagged.Id);
        loaded.TagIds.ShouldBe(single.TagIds, ignoreOrder: true);
        loaded.TagIds.ShouldBe([tag.Id]);
        loaded.Subject.ShouldBe(single.Subject);
        loaded.Number.ShouldBe(single.Number);
        loaded.Status.ShouldBe(single.Status);
        loaded.Version.ShouldBe(single.Version);
        batch.Single(t => t.Id == plain.Id).TagIds.ShouldBeEmpty();
        (await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().GetByIdsAsync([], TestContext.Current.CancellationToken))).ShouldBeEmpty();
    }
}
