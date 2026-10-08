using Microsoft.Extensions.DependencyInjection;
using Npgsql;
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

    [Fact(Timeout = 60_000)]
    public async Task A_second_counter_waits_for_the_first_unit_of_work_and_sees_its_commit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = new PersistenceTestHost(Database);
        Guid secondAdminId = default;
        (await host.CommitAsync(provider =>
        {
            var agents = provider.GetRequiredService<IAgentRepository>();
            agents.Add(Agent.Create("a1", "A1", "a1@example.com", AgentRole.Admin, host.Clock).Value);
            var second = Agent.Create("a2", "A2", "a2@example.com", AgentRole.Admin, host.Clock).Value;
            agents.Add(second);
            secondAdminId = second.Id;
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();

        await using var scope1 = host.CreateScope();
        await using var unitOfWork1 = await scope1.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(ct);
        var agents1 = scope1.ServiceProvider.GetRequiredService<IAgentRepository>();
        (await agents1.CountActiveAdminsLockedAsync(ct)).ShouldBe(2);

        await using var scope2 = host.CreateScope();
        await using var unitOfWork2 = await scope2.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(ct);
        var waiting = Task.Run(() => scope2.ServiceProvider.GetRequiredService<IAgentRepository>().CountActiveAdminsLockedAsync(ct), ct);

        // Poll the server until a backend is blocked on a row lock running the FOR UPDATE query (each probe is an awaited query).
        while (await ScalarAsync("SELECT count(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND query LIKE '%FOR UPDATE%'") == 0)
        {
            waiting.IsCompleted.ShouldBeFalse("the second counter finished without waiting for the lock");
            await Task.Yield();
        }

        waiting.IsCompleted.ShouldBeFalse();

        var second = (await agents1.GetByIdAsync(secondAdminId, ct)).ShouldNotBeNull();
        second.SetActive(false);
        agents1.Update(second);
        (await unitOfWork1.CommitAsync(ct)).IsSuccess.ShouldBeTrue();

        (await waiting).ShouldBe(1);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(Ct));
    }
}
