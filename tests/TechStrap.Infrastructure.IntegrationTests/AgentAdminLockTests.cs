using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Agents;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class AgentAdminLockTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    [Fact]
    public async Task Counting_locked_admins_needs_a_unit_of_work()
    {
        await using var host = new PersistenceTestHost(Database);
        await using var scope = host.CreateScope();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IAgentRepository>().CountActiveAdminsLockedAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Only_active_admins_are_counted()
    {
        await using var host = new PersistenceTestHost(Database);
        (await host.CommitAsync(provider =>
        {
            var agents = provider.GetRequiredService<IAgentRepository>();
            agents.Add(Agent.Create("a1", "A1", "a1@example.com", AgentRole.Admin, host.Clock).Value);
            var inactive = Agent.Create("a2", "A2", "a2@example.com", AgentRole.Admin, host.Clock).Value;
            inactive.SetActive(false);
            agents.Add(inactive);
            agents.Add(Agent.Create("a3", "A3", "a3@example.com", AgentRole.Agent, host.Clock).Value);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();

        var count = 0;
        (await host.CommitAsync(async provider =>
            count = await provider.GetRequiredService<IAgentRepository>().CountActiveAdminsLockedAsync(TestContext.Current.CancellationToken))).IsSuccess.ShouldBeTrue();

        count.ShouldBe(1);
    }
}
